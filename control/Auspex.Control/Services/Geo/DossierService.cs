using Microsoft.EntityFrameworkCore;
using Auspex.Control.Data;

namespace Auspex.Control.Services.Geo;

/// <summary>A domain a device has talked to.</summary>
public sealed record DossierDomain(
    string Domain,
    long Queries,
    DateTime First,
    DateTime Last,
    IReadOnlyList<string> Addresses);

/// <summary>
/// A program that has talked to this operator.
///
/// <para>
/// This row does not come from the resolver but from the sensor on the
/// machine itself. A DNS filter sees the query but not who made it — between
/// it and the program lies the operating system.
/// </para>
/// </summary>
public sealed record DossierProgram(
    string Process,
    long Connections,
    long? BytesOut,
    long? BytesIn);

/// <summary>Everything behind one operator.</summary>
public sealed record DossierOperator(
    string? Operator,
    int? Asn,
    string? Country,
    string? City,
    bool CityUncertain,
    long Queries,
    IReadOnlyList<DossierDomain> Domains)
{
    /// <summary>
    /// The local network — router, NAS, printer.
    ///
    /// <para>
    /// These queries were resolved and answered, but the address behind them
    /// is in the house. Filing them under "operator unknown" would be the
    /// most misleading line on the whole page: it looks like a destination
    /// you know nothing about, and is in truth the only one you know
    /// everything about.
    /// </para>
    /// </summary>
    public bool Local { get; init; }

    /// <summary>
    /// Which programs have talked to this operator. Empty as long as no
    /// sensor runs on the device.
    /// </summary>
    public IReadOnlyList<DossierProgram> Programs { get; init; } = [];
}

/// <summary>Where a device has sent things — and where it has not.</summary>
public sealed record Dossier(
    string Device,
    IReadOnlyList<string> Addresses,
    long Total,
    long Blocked,
    long PassedThrough,
    long Local,
    long WithoutDestination,
    DateTime? First,
    IReadOnlyList<DossierOperator> Operator)
{
    /// <summary>
    /// Whether connection data exists for this device.
    ///
    /// <para>
    /// Important for the display: missing program names do not mean "no
    /// program sent anything" but "no sensor runs here". Without that
    /// distinction an empty column reads as a statement.
    /// </para>
    /// </summary>
    public bool SensorRunning { get; init; }

    /// <summary>Programs whose destination could not be attributed to an operator.</summary>
    public IReadOnlyList<DossierProgram> ProgramsWithoutMapping { get; init; } = [];
}

/// <summary>
/// Puts together where a device has sent things.
///
/// <para>
/// The page answers a question the query log only half answers: there you
/// find which <em>names</em> a device asked for. Who owns them is written
/// nowhere — and "144 queries to <c>dc.services.visualstudio.com</c>" says
/// less than "your machine talks to Microsoft, over nine different names".
/// </para>
///
/// <para>
/// Three figures stand side by side, and the distinction is the page's
/// actual value:
/// </para>
/// <list type="bullet">
/// <item><term>Blocked</term><description> — the query got no address. No
/// connection came about; the destination never heard from this
/// device.</description></item>
/// <item><term>Allowed with a known destination</term><description> — the
/// name was resolved, and we know who owns the address.</description></item>
/// <item><term>Allowed, destination unknown</term><description> — mostly
/// queries from before this feature, plus types like HTTPS/SVCB that supply
/// no address at all. They are shown and not hidden: a summary that only
/// shows what it can explain looks more complete than it
/// is.</description></item>
/// </list>
/// </summary>
public sealed class DossierService(AnalyticsDbContext db)
{
    /// <summary>
    /// The devices a dossier is worth having for — sorted by traffic.
    ///
    /// <para>
    /// Grouped by <em>name</em>, not by address: the same machine turns up
    /// with IPv4 and with changing IPv6 addresses and would otherwise stand
    /// in the list three or four times.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<(string Device, long Queries)>> DevicesAsync(
        TimeSpan window, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow - window;
        var raw = await db.Queries
            .Where(q => q.TimeUtc >= from)
            .GroupBy(q => new { q.Client, q.ClientName })
            .Select(g => new { g.Key.Client, g.Key.ClientName, Count = g.LongCount() })
            .ToListAsync(ct);

        return [.. raw
            .GroupBy(x => string.IsNullOrEmpty(x.ClientName) ? x.Client : x.ClientName)
            .Select(g => (Device: g.Key, Queries: g.Sum(x => x.Count)))
            .OrderByDescending(x => x.Queries)];
    }

    public async Task<Dossier?> ErstellenAsync(
        string device, TimeSpan window, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow - window;

        // Everything from this device, whatever address it came under.
        var own = db.Queries.Where(q => q.TimeUtc >= from
            && (q.ClientName == device || (q.ClientName == null && q.Client == device)));

        var names = await own
            .GroupBy(q => new { q.Name, q.Domain })
            .Select(g => new
            {
                g.Key.Name,
                g.Key.Domain,
                Count = g.LongCount(),
                Blocked = g.LongCount(x => x.Action == "blocked"),
                First = g.Min(x => x.TimeUtc),
                Last = g.Max(x => x.TimeUtc),
            })
            .ToListAsync(ct);

        if (names.Count == 0)
        {
            return null;
        }

        var addresses = await own.Select(q => q.Client).Distinct().ToListAsync(ct);
        var total = names.Sum(n => n.Count);
        var blocked = names.Sum(n => n.Blocked);
        var first = names.Min(n => n.First);

        // Which addresses stood behind these names?
        var namesOnly = names.Select(n => n.Name).Distinct().ToList();
        var mapping = await db.Resolutions
            .Where(a => namesOnly.Contains(a.Name))
            .Select(a => new { a.Name, a.Ip })
            .ToListAsync(ct);

        var ips = mapping.Select(z => z.Ip).Distinct().ToList();
        var destinations = await db.Destinations
            .Where(z => ips.Contains(z.Ip))
            .ToDictionaryAsync(z => z.Ip, ct);

        var perName = mapping
            .GroupBy(z => z.Name)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Ip).Distinct().ToList());

        // Bundle by operator. The key is the AS number where there is one -
        // two operators can share a name, two numbers cannot.
        var bundled = new Dictionary<string, (DossierOperator Header, Dictionary<string, (long Count, DateTime First, DateTime Last, HashSet<string> Ips)> Domains)>();
        long withoutDestination = 0;

        foreach (var n in names)
        {
            var durchgelassen = n.Count - n.Blocked;
            if (durchgelassen <= 0)
            {
                continue;
            }

            if (!perName.TryGetValue(n.Name, out var eigeneIps) || eigeneIps.Count == 0)
            {
                withoutDestination += durchgelassen;
                continue;
            }

            // A name often points at several addresses of the same operator. So
            // the queries must not be counted per address, or a multiple of
            // the traffic ends up on display.
            var operatorByName = eigeneIps
                .Select(ip => destinations.GetValueOrDefault(ip))
                .Where(z => z is not null)
                .GroupBy(Key!)
                .ToList();

            if (operatorByName.Count == 0)
            {
                withoutDestination += durchgelassen;
                continue;
            }

            // With several operators per name (which happens after a move) the
            // one with the most addresses counts.
            var group = operatorByName.OrderByDescending(g => g.Count()).First();
            var vertreter = group.First()!;
            var key = group.Key;

            if (!bundled.TryGetValue(key, out var entry))
            {
                // The uncertainty is derived HERE, not read from the stored
                // column.
                //
                // It hangs off the operator alone, and the list of
                // distribution networks changes: when Hetzner dropped out of
                // it, the stored rows carried the marker onwards - it is only
                // set again while searching for the city, and that had long
                // since been found. A derived value that gets stored goes
                // stale exactly like that.
                entry = (new DossierOperator(
                    vertreter.Operator, vertreter.Asn, vertreter.Country,
                    vertreter.City, GeoService.LooksAnycast(vertreter), 0, [])
                {
                    Local = key == "lokal",
                }, []);
                bundled[key] = entry;
            }

            var domainKey = string.IsNullOrEmpty(n.Domain) ? n.Name : n.Domain;
            if (!entry.Domains.TryGetValue(domainKey, out var d))
            {
                d = (0, n.First, n.Last, []);
            }

            d.Count += durchgelassen;
            if (n.First < d.First) d.First = n.First;
            if (n.Last > d.Last) d.Last = n.Last;
            foreach (var ip in group.Select(z => z!.Ip))
            {
                d.Ips.Add(ip);
            }
            entry.Domains[domainKey] = d;

            bundled[key] = (
                entry.Header with { Queries = entry.Header.Queries + durchgelassen },
                entry.Domains);
        }

        var local = bundled.TryGetValue("lokal", out var l) ? l.Header.Queries : 0;

        // What the sensor on the device reported - if one runs there. It
        // attributes programs to destinations; the mapping from destination
        // to operator is already in place.
        var (perOperator, withoutMapping, sensorRunning) =
            await ProgrammeAsync(device, addresses, from, destinations, ct);

        var carrier = bundled.Values
            .Select(e => e.Header with
            {
                Programs = perOperator.GetValueOrDefault(KeyOf(e.Header), []),
                Domains = [.. e.Domains
                    .Select(d => new DossierDomain(d.Key, d.Value.Count, d.Value.First,
                        d.Value.Last, [.. d.Value.Ips.Order()]))
                    .OrderByDescending(d => d.Queries)],
            })
            .OrderByDescending(b => b.Queries)
            .ToList();

        return new Dossier(
            device, [.. addresses.Order()], total, blocked,
            total - blocked, local, withoutDestination, first, carrier)
        {
            SensorRunning = sensorRunning,
            ProgramsWithoutMapping = withoutMapping,
        };
    }

    /// <summary>
    /// Which programs have talked to which operator.
    ///
    /// <para>
    /// The connections come from the sensor and carry only a destination
    /// address. Which operator that belongs to is in the destinations table —
    /// the same mapping the names are bundled by. So program and domain end
    /// up under the same heading even though they come from two different
    /// sources.
    /// </para>
    /// </summary>
    private async Task<(Dictionary<string, IReadOnlyList<DossierProgram>> PerOperator,
                       IReadOnlyList<DossierProgram> Without,
                       bool Running)>
        ProgrammeAsync(
            string device, List<string> addresses, DateTime from,
            Dictionary<string, Destination> knownDestinations, CancellationToken ct)
    {
        var connections = await db.Connections
            .Where(v => v.LastUtc >= from
                        && (v.Device == device || addresses.Contains(v.Client)))
            .ToListAsync(ct);

        if (connections.Count == 0)
        {
            return ([], [], false);
        }

        // Destinations only the sensor knows about: an address no name
        // resolved to - because the program has it hard-wired, say.
        var fehlende = connections
            .Select(v => v.Destination)
            .Where(ip => !knownDestinations.ContainsKey(ip))
            .Distinct()
            .ToList();

        var allDestinations = new Dictionary<string, Destination>(knownDestinations);
        if (fehlende.Count > 0)
        {
            foreach (var z in await db.Destinations.Where(z => fehlende.Contains(z.Ip)).ToListAsync(ct))
            {
                allDestinations[z.Ip] = z;
            }
        }

        var perOperator = new Dictionary<string, Dictionary<string, DossierProgram>>();
        var without = new Dictionary<string, DossierProgram>();

        foreach (var v in connections)
        {
            var eimer = allDestinations.TryGetValue(v.Destination, out var z)
                ? perOperator.TryGetValue(Key(z), out var existing)
                    ? existing
                    : perOperator[Key(z)] = []
                : without;

            eimer[v.Process] = eimer.TryGetValue(v.Process, out var p)
                ? p with
                {
                    Connections = p.Connections + v.Count,
                    BytesOut = Zusammen(p.BytesOut, v.BytesOut),
                    BytesIn = Zusammen(p.BytesIn, v.BytesIn),
                }
                : new DossierProgram(v.Process, v.Count, v.BytesOut, v.BytesIn);
        }

        return (
            perOperator.ToDictionary(
                x => x.Key,
                IReadOnlyList<DossierProgram> (x) =>
                    [.. x.Value.Values.OrderByDescending(p => p.Connections)]),
            [.. without.Values.OrderByDescending(p => p.Connections)],
            true);
    }

    private static long? Zusammen(long? a, long? b) =>
        a is null && b is null ? null : (a ?? 0) + (b ?? 0);

    /// <summary>The bundle key for a heading that has already been built.</summary>
    private static string KeyOf(DossierOperator b) =>
        b.Local ? "lokal"
        : b.Asn is { } a ? $"as{a}"
        : b.Operator is { Length: > 0 } n ? "n" + n
        : "?";

    /// <summary>
    /// A destination's bundle key. The AS number where there is one — two
    /// operators can share a name, two numbers cannot.
    /// </summary>
    private static string Key(Destination z) =>
        z.IsPrivate ? "lokal"
        : z.Asn is { } a ? $"as{a}"
        : z.Operator is { Length: > 0 } b ? "n" + b
        : "?";
}
