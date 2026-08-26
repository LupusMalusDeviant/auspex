using System.Text.Json;

namespace Auspex.Control.Services.Router;

/// <summary>
/// Writes the router's device list into the shared folder as a MAC-to-name
/// mapping, so the resolver can read it.
///
/// Why the detour through a file: the control plane talks to the router, the
/// resolver does not — and should not. It is the data plane, and its path
/// through a query must not depend on somebody else's device. The same split
/// as with the rules: discovery and writing here, reading only there.
///
/// It is needed because of IPv6. Windows and Android rotate their temporary
/// addresses regularly; the resolver resolves them to a MAC through the
/// host's neighbour table and then only needs the name to go with it. The
/// router knows that.
/// </summary>
public class DeviceNameExportService(
    IServiceProvider services,
    IRouterSettingsStore store,
    ILogger<DeviceNameExportService> log) : BackgroundService
{
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            if (store.Current.Configured)
            {
                try
                {
                    await WriteAsync(stop);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Unreachable is a state, not a crash. The resolver carries on
                    // with the last version.
                    log.LogWarning(ex, "The device list could not be written");
                }
            }

            try
            {
                await Task.Delay(Gap, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task WriteAsync(CancellationToken ct)
    {
        using var range = services.CreateScope();
        var router = range.ServiceProvider.GetRequiredService<RouterAdmin>();

        var devices = await router.GetDevicesAsync(ct);
        if (devices.Count == 0)
        {
            // An empty list is almost always a disturbance, not a statement.
            // Writing it would mean deleting every device name.
            log.LogDebug("The router reports no devices - the existing list stays as it is");
            return;
        }

        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in devices)
        {
            var name = g.Name;
            if (string.IsNullOrWhiteSpace(name) || IsPlaceholder(name))
            {
                continue;
            }
            mapping[g.Mac.ToLowerInvariant()] = name;
        }

        var path = store.Current.DeviceNamePath;
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var fresh = JsonSerializer.Serialize(mapping, new JsonSerializerOptions { WriteIndented = true });

        // Only write when something has changed: the resolver sees the file by
        // its timestamp and would otherwise reload the same thing every ten
        // minutes.
        if (File.Exists(path) && await File.ReadAllTextAsync(path, ct) == fresh)
        {
            return;
        }

        // Beside it first, then rename - an aborted write should not leave half
        // a file behind that the resolver cannot read.
        var provisional = path + ".neu";
        await File.WriteAllTextAsync(provisional, fresh, ct);
        File.Move(provisional, path, overwrite: true);

        log.LogInformation("Device list written: {Count} names to {Path}", mapping.Count, path);
    }

    /// <summary>
    /// Names that are not names. Some devices report a random identifier
    /// instead of a name — showing that in the query log would be no gain
    /// over the bare address.
    /// </summary>
    private static bool IsPlaceholder(string name) =>
        Guid.TryParse(name, out _)
        || name.Equals("unknown", StringComparison.OrdinalIgnoreCase)
        || name.Equals("android", StringComparison.OrdinalIgnoreCase);
}
