using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Auspex.Control.Data;

namespace Auspex.Control.Services.Geo;

/// <summary>
/// Fills in who owns an address and where it sits.
///
/// <para>
/// Filled in later rather than at ingest: ingest runs every few seconds and
/// must not wait for a lookup. A new address therefore stands in the table
/// without an origin at first and gets one shortly after — first the
/// operator, which is there immediately from the small file, then the city,
/// which costs a pass over the big one.
/// </para>
///
/// <para>
/// The two carry different weight. The <em>operator</em> is correct: it is
/// in the routing, and whoever announces an address is its operator. The
/// <em>city</em> is an estimate, and with anycast a poor one — the same
/// address answers in Frankfurt and in Sydney. Anything that looks like that
/// is therefore marked uncertain rather than left out.
/// </para>
/// </summary>
public sealed class GeoService(
    IServiceScopeFactory scopes,
    INetworkRanges ranges,
    GeoSources sources,
    CityLookup cities,
    IOptions<GeoOptions> options,
    ILogger<GeoService> log) : BackgroundService
{
    private readonly GeoOptions _opt = options.Value;

    /// <summary>
    /// From how many pending addresses a pass over the city file is worth
    /// it. Below that it waits — unless it has been pending for a long time.
    /// </summary>
    private const int CityThreshold = 50;

    private DateTime _lastCityLookup = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_opt.Enabled)
        {
            log.LogInformation("Origin lookup is switched off");
            return;
        }

        ranges.Prepare();

        // Wait a little: at startup the application has better things to do
        // than download 90 MB.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await MaintainSourcesAsync(ct);
                await FillOperatorsAsync(ct);
                await FillCitiesAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // A missing origin is a blemish, not a reason to stop the
                // service.
                log.LogWarning(ex, "Herkunftsbestimmung fehlgeschlagen");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private string AsnFile => Path.Combine(_opt.Path, "ip2asn-combined.tsv.gz");
    private string CityFile => Path.Combine(_opt.Path, "dbip-city-lite.csv.gz");

    /// <summary>Fetches the lookup data when it is missing or old.</summary>
    private async Task MaintainSourcesAsync(CancellationToken ct)
    {
        var maxAge = TimeSpan.FromDays(Math.Max(1, _opt.RefreshDays));

        var asn = await sources.FetchAsync(_opt.AsnUrl, AsnFile, maxAge, ct);
        if (asn is not null)
        {
            var (fetched, lines) = ranges.State();
            var eingelesen = fetched is not null && lines > 0;
            var fresher = fetched is null || File.GetLastWriteTimeUtc(asn) > fetched;

            if (!eingelesen || fresher)
            {
                ranges.Import(GeoSources.AsnRows(asn));
            }
        }

        if (_opt.City)
        {
            var url = GeoSources.CityUrlFor(_opt.CityUrl, DateTime.UtcNow);
            await sources.FetchAsync(url, CityFile, maxAge, ct);
        }
    }

    /// <summary>
    /// Fills in the operator. That is one index lookup per address and
    /// therefore happens on every pass for everything pending.
    /// </summary>
    private async Task FillOperatorsAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        var open = await db.Destinations
            .Where(z => !z.IsPrivate && z.CheckedUtc == null)
            .OrderByDescending(z => z.LastUtc)
            .Take(2000)
            .ToListAsync(ct);

        if (open.Count == 0)
        {
            return;
        }

        var numbers = new Dictionary<UInt128, List<Destination>>();
        foreach (var z in open)
        {
            if (AddressSpace.AsNumber(z.Ip) is not { } value)
            {
                // Not a readable address: mark it as checked, or it turns up
                // again on every pass.
                z.CheckedUtc = DateTime.UtcNow;
                continue;
            }
            if (!numbers.TryGetValue(value, out var list))
            {
                numbers[value] = list = [];
            }
            list.Add(z);
        }

        var lookups = ranges.Lookup(numbers.Keys);

        foreach (var (value, destinations) in numbers)
        {
            lookups.TryGetValue(value, out var a);
            foreach (var z in destinations)
            {
                z.Asn = a?.Asn;
                z.Country = a?.Country;
                z.Operator = a?.Operator;
                // Mark it checked even without a hit: not every address sits
                // in an announced range, and an unsuccessful lookup should
                // not repeat forever.
                z.CheckedUtc = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Operators filled in for {Count} addresses, {Matched} mapped",
            open.Count, lookups.Count);
    }

    /// <summary>
    /// Fills in the city — in batches, because every pass reads the whole
    /// file.
    /// </summary>
    private async Task FillCitiesAsync(CancellationToken ct)
    {
        if (!_opt.City || !File.Exists(CityFile))
        {
            return;
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        // Pending is whatever has not been searched for yet. "Found nothing"
        // is a result and gets recorded; "uncertain" is something else and
        // hangs off a flag of its own.
        var open = await db.Destinations
            .Where(z => !z.IsPrivate && z.CheckedUtc != null && z.CityCheckedUtc == null)
            .OrderByDescending(z => z.LastUtc)
            .Take(20000)
            .ToListAsync(ct);

        var lange = DateTime.UtcNow - _lastCityLookup > TimeSpan.FromHours(6);
        if (open.Count == 0 || (open.Count < CityThreshold && !lange))
        {
            return;
        }
        _lastCityLookup = DateTime.UtcNow;

        var numbers = new Dictionary<UInt128, List<Destination>>();
        foreach (var z in open)
        {
            if (AddressSpace.AsNumber(z.Ip) is not { } value)
            {
                continue;
            }
            if (!numbers.TryGetValue(value, out var list))
            {
                numbers[value] = list = [];
            }
            list.Add(z);
        }

        var orte = await Task.Run(() => cities.Lookup(CityFile, numbers.Keys, ct), ct);

        foreach (var (value, destinations) in numbers)
        {
            orte.TryGetValue(value, out var place);
            foreach (var z in destinations)
            {
                z.City = place?.City;

                // The country belongs to the city, not to the operator.
                //
                // The operator file names the country the AUTONOMOUS SYSTEM
                // is registered in - for Google that is the USA, even when
                // the address sits in a Frankfurt node. Read next to
                // "Frankfurt am Main", "US" produced a contradiction nobody
                // can resolve without knowing the difference.
                //
                // Where the city file names a country, its one applies: it
                // says something about the range, not about the company
                // behind it. Both values then come from the same source and
                // no longer contradict each other.
                if (place?.Country is { Length: > 0 } ortsland)
                {
                    z.Country = ortsland;
                }
                z.Country ??= place?.Country;
                // Uncertain means: there is a city, but it names a node and not a
                // headquarters. With no city there is nothing that could be
                // uncertain.
                z.CityUncertain = place?.City is not null && LooksAnycast(z);
                // And separately from that: it was searched for, with or without a
                // result.
                z.CityCheckedUtc = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Whether the city for this address is to be read with care.
    ///
    /// <para>
    /// Large distribution networks announce the same address in many places.
    /// What a city database says about it is the location of one node — often
    /// the nearest one. A map that turns that into a company headquarters
    /// claims something nobody has checked.
    /// </para>
    ///
    /// <para>
    /// Recognised by the operator, not by the address: the big anycast
    /// networks are a manageable list, and whoever is on it almost certainly
    /// distributes. That is a rule of thumb and is labelled as one.
    /// </para>
    /// </summary>
    internal static bool LooksAnycast(Destination z)
    {
        if (z.Operator is not { Length: > 0 } b)
        {
            return false;
        }

        string[] verteiler =
        [
            "CLOUDFLARE", "GOOGLE", "AKAMAI", "FASTLY", "AMAZON", "AMAZO",
            "MICROSOFT", "EDGECAST", "LLNW", "LIMELIGHT", "CDN77", "BUNNY",
            "STACKPATH", "INCAPSULA", "IMPERVA", "APPLE",
            "FACEBOOK", "META", "NETFLIX", "TWITTER", "AUTOMATTIC",
        ];

        // Hetzner and OVH stood here and are out again. They run data
        // centres, not distribution networks: an address from Hetzner really
        // is in Falkenstein. Marking it uncertain would sow doubt about a
        // value that is correct - and a marker that appears everywhere gets
        // read as little as an alarm that is always lit.

        return verteiler.Any(v => b.Contains(v, StringComparison.OrdinalIgnoreCase));
    }
}
