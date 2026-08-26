using Microsoft.EntityFrameworkCore;
using Auspex.Control.Data;

namespace Auspex.Control.Services.Geo;

/// <summary>
/// Takes the resolved addresses out of a batch of log entries.
///
/// <para>
/// The resolver supplies, with every answered query, what the name pointed
/// at. That used to be discarded. So the log kept the name and nothing else
/// — and the question "where does this device actually send things?" was
/// only answerable as far as the name, not as far as the operator behind it.
/// </para>
///
/// <para>
/// What gets written is <em>one</em> row per address and one per pair of
/// name and address, not one per query. At around 140,000 queries a day and
/// a few thousand addresses involved, that is the difference between a table
/// you can analyse and one that merely grows.
/// </para>
/// </summary>
public static class DestinationCapture
{
    /// <summary>
    /// How many keys go into one query at most. SQLite tolerates more, but a
    /// query with thousands of parameters gets slow, and the ingest runs
    /// every few seconds.
    /// </summary>
    private const int Batch = 400;

    /// <summary>A pair of name and address, as it occurred in the batch.</summary>
    private sealed record Observation
    {
        public required string Name { get; init; }
        public required string Domain { get; init; }
        public required string Ip { get; init; }
        public DateTime First { get; set; }
        public DateTime Last { get; set; }
        public long Count { get; set; }
    }

    public static async Task RecordAsync(
        AnalyticsDbContext db, IEnumerable<QueryLogEntry> entries, CancellationToken ct)
    {
        var seen = Sammeln(entries);
        if (seen.Count == 0)
        {
            return;
        }

        await CarryDestinationsForwardAsync(db, seen.Values, ct);
        await CarryResolutionsForwardAsync(db, seen.Values, ct);
    }

    /// <summary>
    /// Folds the batch down to distinct pairs.
    ///
    /// <para>
    /// Whatever is not an address falls out here: the resolver puts
    /// everything that was in the answer into <c>answers</c> — for a CNAME
    /// chain that means names, for a TXT record its text.
    /// </para>
    /// </summary>
    private static Dictionary<(string, string), Observation> Sammeln(
        IEnumerable<QueryLogEntry> entries)
    {
        var seen = new Dictionary<(string, string), Observation>();

        foreach (var e in entries)
        {
            if (e.Answers is not { Length: > 0 } answers || string.IsNullOrEmpty(e.Name))
            {
                continue;
            }

            var time = e.Time.UtcDateTime;
            foreach (var raw in answers)
            {
                if (AddressSpace.Normalise(raw) is not { } ip)
                {
                    continue;
                }

                var key = (e.Name, ip);
                if (seen.TryGetValue(key, out var b))
                {
                    b.Count++;
                    if (time < b.First) b.First = time;
                    if (time > b.Last) b.Last = time;
                    continue;
                }

                seen[key] = new Observation
                {
                    Name = e.Name,
                    Domain = string.IsNullOrEmpty(e.Domain) ? e.Name : e.Domain,
                    Ip = ip,
                    First = time,
                    Last = time,
                    Count = 1,
                };
            }
        }

        return seen;
    }

    private static async Task CarryDestinationsForwardAsync(
        AnalyticsDbContext db, IEnumerable<Observation> seen, CancellationToken ct)
    {
        var perAddress = seen
            .GroupBy(b => b.Ip)
            .ToDictionary(g => g.Key, g => (First: g.Min(x => x.First), Last: g.Max(x => x.Last)));

        foreach (var chunk in perAddress.Keys.Chunk(Batch))
        {
            var existing = await db.Destinations
                .Where(z => chunk.Contains(z.Ip))
                .ToDictionaryAsync(z => z.Ip, ct);

            foreach (var ip in chunk)
            {
                var (first, last) = perAddress[ip];

                if (existing.TryGetValue(ip, out var z))
                {
                    if (last > z.LastUtc) z.LastUtc = last;
                    if (first < z.FirstUtc) z.FirstUtc = first;
                    continue;
                }

                db.Destinations.Add(new Destination
                {
                    Ip = ip,
                    IsPrivate = AddressSpace.IsPrivate(ip),
                    FirstUtc = first,
                    LastUtc = last,
                });
            }
        }
    }

    private static async Task CarryResolutionsForwardAsync(
        AnalyticsDbContext db, IEnumerable<Observation> seen, CancellationToken ct)
    {
        foreach (var chunk in seen.Chunk(Batch))
        {
            // Pre-filter by address and narrow to the pair in memory: a composite
            // IN over two columns translates badly in SQLite, a simple one
            // hits the index.
            var addresses = chunk.Select(b => b.Ip).Distinct().ToArray();
            var existing = (await db.Resolutions
                    .Where(a => addresses.Contains(a.Ip))
                    .ToListAsync(ct))
                .ToDictionary(a => (a.Name, a.Ip));

            foreach (var b in chunk)
            {
                if (existing.TryGetValue((b.Name, b.Ip), out var a))
                {
                    a.Count += b.Count;
                    if (b.Last > a.LastUtc) a.LastUtc = b.Last;
                    if (b.First < a.FirstUtc) a.FirstUtc = b.First;
                    continue;
                }

                db.Resolutions.Add(new Resolution
                {
                    Name = b.Name,
                    Domain = b.Domain,
                    Ip = b.Ip,
                    FirstUtc = b.First,
                    LastUtc = b.Last,
                    Count = b.Count,
                });
            }
        }
    }
}
