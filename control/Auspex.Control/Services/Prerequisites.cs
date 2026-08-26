using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Auspex.Control.Data;
using Auspex.Control.Services.Extension;
using Auspex.Control.Services.Geo;
using Auspex.Control.Services.Router;

namespace Auspex.Control.Services;

/// <summary>How an additional source is doing.</summary>
public enum PartState
{
    /// <summary>Set up and delivering.</summary>
    Active,

    /// <summary>Set up but switched off — or on, but with no data.</summary>
    Idle,

    /// <summary>Not set up. The columns hanging off it stay empty.</summary>
    Missing,
}

/// <summary>
/// A part Auspex uses when it is there.
/// </summary>
/// <param name="Key">For looking the text up; not for display.</param>
/// <param name="State">Active, idle or absent.</param>
/// <param name="Detail">A number or a time, if there is something to say.</param>
public sealed record Part(string Key, PartState State, string? Detail = null);

/// <summary>
/// What Auspex can do depends on parts it does not ship: a router account, a
/// browser extension, a sensor on the machine, an origin database.
///
/// <para>
/// <strong>None of them start by themselves.</strong> A tool that pulls
/// 800 MB over a home line unasked, or probes a router nobody gave it access
/// to, is helping itself. So: consent first.
/// </para>
///
/// <para>
/// This class is the counterpart to that. Whoever switches nothing on gets
/// empty columns — and empty columns look like "there is nothing here", not
/// like "something is missing here". So the settings page says for every
/// part whether it is there, what it contributes and how to switch it on.
/// Without this list, the restraint above would be nothing but a silent gap.
/// </para>
/// </summary>
public sealed class Prerequisites(
    IRouterSettingsStore router,
    IExtensionTokenStore token,
    SensorPackage sensor,
    INetworkRanges ranges,
    IOptions<GeoOptions> geo,
    IOptions<AnalyticsOptions> analytics,
    AnalyticsDbContext db)
{
    /// <summary>Every part, in the order you set them up.</summary>
    public async Task<IReadOnlyList<Part>> AllAsync(CancellationToken ct = default)
    {
        return
        [
            Analytics(),
            Router(),
            Extension(),
            await SensorAsync(ct),
            Origin(),
        ];
    }

    /// <summary>
    /// The analysis. Without it there is no time series, no findings and no
    /// dossier history — only the live stream.
    /// </summary>
    private Part Analytics() =>
        new("analytics",
            analytics.Value.Enabled ? PartState.Active : PartState.Idle);

    private Part Router() =>
        new("router",
            router.Current.Configured ? PartState.Active : PartState.Missing,
            router.Current.Configured ? router.Current.Host : null);

    private Part Extension() =>
        new("extension",
            token.Present ? PartState.Active : PartState.Missing,
            token.Created?.ToString("yyyy-MM-dd"));

    /// <summary>
    /// The sensor does not sign in — it reports. Whether one is running is
    /// therefore not in a setting but in the data: has anybody sent
    /// connections in the last twenty-four hours?
    /// </summary>
    private async Task<Part> SensorAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var last = await db.Connections
            .Where(c => c.LastUtc >= since)
            .MaxAsync(c => (DateTime?)c.LastUtc, ct);

        if (last is { } t)
        {
            return new Part("sensor", PartState.Active,
                Localization.DisplayTime.ToDisplay(t).ToString("HH:mm"));
        }

        // Between "no sensor installed" and "sensor installed but not
        // reporting" lies the whole difference when something is wrong.
        // Whether the package is even in the image does not say the one - but
        // whether there ever was data says the other.
        var ever = await db.Connections.AnyAsync(ct);
        return new Part("sensor",
            ever ? PartState.Idle : PartState.Missing,
            sensor.Available ? null : "no package in the image");
    }

    /// <summary>
    /// Origin. The switch says whether refreshing happens; the row count
    /// says whether there is anything at all. The two belong side by side —
    /// a source switched on with no data is not yet information.
    /// </summary>
    private Part Origin()
    {
        var (fetched, rows) = ranges.State();
        if (rows <= 0)
        {
            return new Part("origin", PartState.Missing);
        }

        return new Part("origin",
            geo.Value.Enabled ? PartState.Active : PartState.Idle,
            $"{rows:N0}"
            + (fetched is { } f ? $" · {Localization.DisplayTime.ToDisplay(f):yyyy-MM-dd}" : ""));
    }
}
