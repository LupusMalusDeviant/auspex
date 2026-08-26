using Auspex.Control.Services.Localization;
using Microsoft.EntityFrameworkCore;
using Auspex.Control.Data;
using Auspex.Control.Services.Geo;

namespace Auspex.Control.Services.Extension;

/// <summary>One reported relation, in the shape the sensor sends it.</summary>
public sealed record ConnectionReport(
    string Process,
    string Destination,
    int Port,
    string Protocol,
    long Count,
    DateTimeOffset First,
    DateTimeOffset Last,
    long? BytesOut,
    long? BytesIn);

/// <summary>A batch of reports.</summary>
public sealed record SensorBatch(ConnectionReport[] Connections);

/// <summary>
/// Takes the sensor's reports.
///
/// <para>
/// The sensor runs on the machine being watched and reports which program
/// talked to which destination. It may report exclusively about
/// <em>itself</em>: which device is meant is said not by the report but by
/// its sender address — the same rule as with the browser extension, and for
/// the same reason.
/// </para>
///
/// <para>
/// It only takes in and hands nothing out. A sensor needs no information
/// about the network; it supplies some.
/// </para>
/// </summary>
public static class SensorApi
{
    /// <summary>
    /// How many reports a batch may carry at most.
    ///
    /// <para>
    /// A machine with a lot of traffic comes to a few hundred relations; a
    /// multiple of that would be either a fault in the sensor or an attempt
    /// to fill the database. Both belong refused, not ingested.
    /// </para>
    /// </summary>
    private const int MaxPerBatch = 2000;

    public static void MapSensorApi(this RouteGroupBuilder group)
    {
        group.MapPost("/connections", async (
            SensorBatch batch,
            HttpContext http,
            IExtensionTokenStore token,
            IAuspexClient auspex,
            AnalyticsDbContext db,
            ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            if (!Allowed(http, token))
            {
                return Results.Json(new { error = Strings.Current.TokenNoLongerValid }, statusCode: 401);
            }

            var sender = Sender(http);
            if (sender is null)
            {
                return Results.BadRequest(new { error = "The sender address is unknown." });
            }

            if (batch.Connections.Length > MaxPerBatch)
            {
                return Results.BadRequest(new
                {
                    error = $"Too many reports at once (at most {MaxPerBatch}).",
                });
            }

            // Who is this? The resolver keeps the neighbour table anyway;
            // building it here a second time would mean maintaining two
            // truths.
            var who = await auspex.WhoAsync(sender, ct);
            var device = who is { Known: true, Name.Length: > 0 } ? who.Name : null;

            var accepted = await ApplyAsync(db, sender, device, batch.Connections, ct);

            loggers.CreateLogger("Auspex.Control.Sensor").LogDebug(
                "{Count} connections from {Device} taken over", accepted, device ?? sender);

            return Results.Ok(new { accepted, device });
        });
    }

    /// <summary>
    /// Carries the relations forward.
    ///
    /// <para>
    /// Public so a test can check it without setting up HTTP — the folding is
    /// the part where you can miscount.
    /// </para>
    /// </summary>
    public static async Task<int> ApplyAsync(
        AnalyticsDbContext db, string sender, string? device,
        IReadOnlyList<ConnectionReport> reports, CancellationToken ct)
    {
        if (reports.Count == 0)
        {
            return 0;
        }

        // Fold in memory first: the same key can occur several times in a
        // batch, and two rows with the same key violate the unique index.
        var folded = new Dictionary<(string, string, int, string), ConnectionReport>();
        foreach (var m in reports)
        {
            var destination = AddressSpace.Normalise(m.Destination);
            if (destination is null || m.Process.Length == 0 || m.Port is < 0 or > 65535)
            {
                continue;
            }

            var protocol = m.Protocol.Equals("udp", StringComparison.OrdinalIgnoreCase)
                ? "udp" : "tcp";
            var process = Shorten(m.Process, 128);
            var key = (process, destination, m.Port, protocol);

            if (folded.TryGetValue(key, out var existing))
            {
                folded[key] = existing with
                {
                    Count = existing.Count + m.Count,
                    First = m.First < existing.First ? m.First : existing.First,
                    Last = m.Last > existing.Last ? m.Last : existing.Last,
                    BytesOut = Sum(existing.BytesOut, m.BytesOut),
                    BytesIn = Sum(existing.BytesIn, m.BytesIn),
                };
                continue;
            }

            folded[key] = m with { Process = process, Destination = destination, Protocol = protocol };
        }

        if (folded.Count == 0)
        {
            return 0;
        }

        var processes = folded.Keys.Select(k => k.Item1).Distinct().ToList();
        var vorhandene = await db.Connections
            .Where(v => v.Client == sender && processes.Contains(v.Process))
            .ToListAsync(ct);

        var byKey = vorhandene.ToDictionary(
            v => (v.Process, v.Destination, v.Port, v.Protocol));

        foreach (var (key, m) in folded)
        {
            if (byKey.TryGetValue(key, out var v))
            {
                v.Count += m.Count;
                if (m.Last.UtcDateTime > v.LastUtc) v.LastUtc = m.Last.UtcDateTime;
                if (m.First.UtcDateTime < v.FirstUtc) v.FirstUtc = m.First.UtcDateTime;
                v.BytesOut = Sum(v.BytesOut, m.BytesOut);
                v.BytesIn = Sum(v.BytesIn, m.BytesIn);
                // The name can follow later: on first contact the resolver
                // sometimes does not know the device yet.
                v.Device ??= device;
                continue;
            }

            db.Connections.Add(new Connection
            {
                Client = Shorten(sender, 64),
                Device = device is null ? null : Shorten(device, 128),
                Process = m.Process,
                Destination = m.Destination,
                Port = m.Port,
                Protocol = m.Protocol,
                FirstUtc = m.First.UtcDateTime,
                LastUtc = m.Last.UtcDateTime,
                Count = m.Count,
                BytesOut = m.BytesOut,
                BytesIn = m.BytesIn,
            });
        }

        await db.SaveChangesAsync(ct);
        return folded.Count;
    }

    /// <summary>
    /// Adds two byte counters, where "not counted" is contagious — but only
    /// as long as nothing at all was counted. Otherwise, after a restart
    /// without administrator rights, a smaller number than before would
    /// suddenly stand there.
    /// </summary>
    private static long? Sum(long? a, long? b) =>
        a is null && b is null ? null : (a ?? 0) + (b ?? 0);

    private static string Shorten(string s, int length) =>
        s.Length <= length ? s : s[..length];

    private static bool Allowed(HttpContext http, IExtensionTokenStore token)
    {
        var header = http.Request.Headers.Authorization.ToString();
        var value = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : http.Request.Headers["X-Auspex-Token"].ToString();

        return token.Checks(value);
    }

    private static string? Sender(HttpContext http)
    {
        var address = http.Connection.RemoteIpAddress;
        if (address is null)
        {
            return null;
        }
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        return address.ToString();
    }
}
