using Microsoft.EntityFrameworkCore;
using Auspex.Control.Data;

namespace Auspex.Control.Services;

public record Overview(
    long Total,
    long Blocked,
    long Validated,
    long Upstream,
    int Domains,
    int Clients,
    double AvgMillis,
    DateTime? Oldest)
{
    public double BlockRate => Total == 0 ? 0 : (double)Blocked / Total;

    /// <summary>
    /// The share of validated answers among those that went upstream at all.
    /// Blocked queries and ones answered from cache do not belong in the
    /// denominator — otherwise the figure measures the cache hit rate too.
    /// </summary>
    public double ValidatedRate => Upstream == 0 ? 0 : (double)Validated / Upstream;
}

public record TimeBucket(DateTime StartUtc, long Total, long Blocked)
{
    public double BlockRate => Total == 0 ? 0 : (double)Blocked / Total;
}

public record DomainCount(string Domain, long Total, long Blocked, int Clients);

public record ClientSummary(
    string Client,
    string? ClientName,
    string? Profile,
    long Total,
    long Blocked,
    int Domains,
    DateTime LastSeenUtc)
{
    public double BlockRate => Total == 0 ? 0 : (double)Blocked / Total;

    /// <summary>The name where known, otherwise the address.</summary>
    public string Label => string.IsNullOrEmpty(ClientName) ? Client : ClientName;
}

public record ListCount(string List, long Blocked);

/// <summary>Analysis over the permanently stored query log.</summary>
public sealed class AnalyticsService(AnalyticsDbContext db)
{
    public async Task<Overview> GetOverviewAsync(TimeSpan window, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow - window;
        var rows = db.Queries.Where(q => q.TimeUtc >= from);

        if (!await rows.AnyAsync(ct))
        {
            return new Overview(0, 0, 0, 0, 0, 0, 0, await db.Queries.MinAsync(q => (DateTime?)q.TimeUtc, ct));
        }

        return new Overview(
            await rows.LongCountAsync(ct),
            await rows.LongCountAsync(q => q.Action == "blocked", ct),
            await rows.LongCountAsync(q => q.Validated, ct),
            await rows.LongCountAsync(q => q.Source == "upstream", ct),
            await rows.Select(q => q.Domain).Distinct().CountAsync(ct),
            await rows.Select(q => q.Client).Distinct().CountAsync(ct),
            await rows.AverageAsync(q => q.Millis, ct),
            await db.Queries.MinAsync(q => (DateTime?)q.TimeUtc, ct));
    }

    /// <summary>
    /// An hourly time series. Gaps are filled in — without that a quiet
    /// night would simply vanish from the chart instead of being visible as
    /// a zero.
    /// </summary>
    public async Task<List<TimeBucket>> GetTimelineAsync(TimeSpan window, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow - window;
        var grouped = await db.Queries
            .Where(q => q.TimeUtc >= from)
            .GroupBy(q => new { q.TimeUtc.Date, q.TimeUtc.Hour })
            .Select(g => new
            {
                g.Key.Date,
                g.Key.Hour,
                Total = g.LongCount(),
                Blocked = g.LongCount(x => x.Action == "blocked"),
            })
            .ToListAsync(ct);

        var byHour = grouped.ToDictionary(
            r => DateTime.SpecifyKind(r.Date.AddHours(r.Hour), DateTimeKind.Utc),
            r => (r.Total, r.Blocked));

        var start = new DateTime(from.Year, from.Month, from.Day, from.Hour, 0, 0, DateTimeKind.Utc);
        var end = DateTime.UtcNow;

        var buckets = new List<TimeBucket>();
        for (var t = start; t <= end; t = t.AddHours(1))
        {
            buckets.Add(byHour.TryGetValue(t, out var v)
                ? new TimeBucket(t, v.Total, v.Blocked)
                : new TimeBucket(t, 0, 0));
        }
        return buckets;
    }

    public async Task<List<DomainCount>> GetTopDomainsAsync(
        TimeSpan window, bool blockedOnly, int limit = 20, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow - window;
        var rows = db.Queries.Where(q => q.TimeUtc >= from);
        if (blockedOnly)
        {
            rows = rows.Where(q => q.Action == "blocked");
        }

        // Two steps instead of one: "distinct clients per domain" inside a
        // grouping is something EF cannot translate to SQL. Fetch the totals
        // first, then fill in the variety for the few hits.
        var totals = await rows
            .GroupBy(q => q.Domain)
            .Select(g => new
            {
                Domain = g.Key,
                Total = g.LongCount(),
                Blocked = g.LongCount(x => x.Action == "blocked"),
            })
            .OrderByDescending(d => d.Total)
            .Take(limit)
            .ToListAsync(ct);

        var names = totals.Select(t => t.Domain).ToList();
        var clientsPerDomain = (await rows
                .Where(q => names.Contains(q.Domain))
                .Select(q => new { q.Domain, q.Client })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(x => x.Domain)
            .ToDictionary(g => g.Key, g => g.Count());

        return totals
            .Select(t => new DomainCount(
                t.Domain, t.Total, t.Blocked, clientsPerDomain.GetValueOrDefault(t.Domain)))
            .ToList();
    }

    public async Task<List<ClientSummary>> GetClientsAsync(TimeSpan window, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow - window;
        var totals = await db.Queries
            .Where(q => q.TimeUtc >= from)
            .GroupBy(q => q.Client)
            .Select(g => new
            {
                Client = g.Key,
                ClientName = g.Max(x => x.ClientName),
                Profile = g.Max(x => x.Profile),
                Total = g.LongCount(),
                Blocked = g.LongCount(x => x.Action == "blocked"),
                LastSeen = g.Max(x => x.TimeUtc),
            })
            .OrderByDescending(c => c.Total)
            .ToListAsync(ct);

        var domainsPerClient = (await db.Queries
                .Where(q => q.TimeUtc >= from)
                .Select(q => new { q.Client, q.Domain })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(x => x.Client)
            .ToDictionary(g => g.Key, g => g.Count());

        return totals
            .Select(t => new ClientSummary(
                t.Client, t.ClientName, t.Profile, t.Total, t.Blocked,
                domainsPerClient.GetValueOrDefault(t.Client), t.LastSeen))
            .ToList();
    }

    public async Task<List<ListCount>> GetTopListsAsync(
        TimeSpan window, int limit = 10, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow - window;
        // An anonymous type in the SQL projection, the record only afterwards:
        // EF cannot translate a record constructor inside a grouping. Applies
        // to every grouping in this file.
        var rows = await db.Queries
            .Where(q => q.TimeUtc >= from && q.Action == "blocked" && q.List != null)
            .GroupBy(q => q.List!)
            .Select(g => new { List = g.Key, Blocked = g.LongCount() })
            .OrderByDescending(l => l.Blocked)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(r => new ListCount(r.List, r.Blocked)).ToList();
    }

    public async Task<IngestState?> GetIngestStateAsync(CancellationToken ct = default)
        => await db.IngestStates.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);

    public async Task<List<Finding>> GetFindingsAsync(
        bool includeDismissed = false, int limit = 100, CancellationToken ct = default)
    {
        var rows = db.Findings.AsNoTracking();
        if (!includeDismissed)
        {
            rows = rows.Where(f => !f.Dismissed);
        }
        return await rows
            .OrderByDescending(f => f.Severity == "high")
            .ThenByDescending(f => f.DetectedUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <summary>Records that the suggestion was applied.</summary>
    public async Task MarkAppliedAsync(long id, CancellationToken ct = default)
    {
        await db.Findings.Where(f => f.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.AppliedUtc, DateTime.UtcNow), ct);
    }

    public async Task DismissAsync(long id, CancellationToken ct = default)
    {
        await db.Findings.Where(f => f.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Dismissed, true), ct);
    }
}

/// <summary>
/// Analysis over the rolled-up daily totals. Deliberately separate from the
/// raw-data analysis rather than mixing the two: the two sources have
/// different resolution, and a view that silently switched between them
/// would not be followable.
/// </summary>
public sealed class LongTermService(AnalyticsDbContext db)
{
    public async Task<bool> HasDataAsync(CancellationToken ct = default)
        => await db.DailyTotals.AnyAsync(ct);

    public async Task<Overview> GetOverviewAsync(int days, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow.Date.AddDays(-days);
        var rows = db.DailyTotals.Where(d => d.Day >= from);

        if (!await rows.AnyAsync(ct))
        {
            return new Overview(0, 0, 0, 0, 0, 0, 0, await db.DailyTotals.MinAsync(d => (DateTime?)d.Day, ct));
        }

        return new Overview(
            await rows.SumAsync(d => d.Total, ct),
            await rows.SumAsync(d => d.Blocked, ct),
            await rows.SumAsync(d => d.Validated, ct),
            await rows.SumAsync(d => d.Upstream, ct),
            // Daily totals summed across several days would be wrong: the same
            // device would count again for each day. So take the real count
            // from the per-device daily totals.
            await db.DailyDomains.Where(d => d.Day >= from).Select(d => d.Domain).Distinct().CountAsync(ct),
            await db.DailyClients.Where(d => d.Day >= from).Select(d => d.Client).Distinct().CountAsync(ct),
            0,
            await db.DailyTotals.MinAsync(d => (DateTime?)d.Day, ct));
    }

    public async Task<List<TimeBucket>> GetTimelineAsync(int days, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow.Date.AddDays(-days);
        var rows = await db.DailyTotals
            .Where(d => d.Day >= from)
            .Select(d => new { d.Day, d.Total, d.Blocked })
            .ToListAsync(ct);

        var byDay = rows.ToDictionary(r => r.Day, r => (r.Total, r.Blocked));

        // Fill in quiet days, or they collapse together in the chart and a
        // week of holiday looks like continuous operation.
        var buckets = new List<TimeBucket>();
        for (var day = from; day < DateTime.UtcNow.Date; day = day.AddDays(1))
        {
            buckets.Add(byDay.TryGetValue(day, out var v)
                ? new TimeBucket(DateTime.SpecifyKind(day, DateTimeKind.Utc), v.Total, v.Blocked)
                : new TimeBucket(DateTime.SpecifyKind(day, DateTimeKind.Utc), 0, 0));
        }
        return buckets;
    }

    public async Task<List<DomainCount>> GetTopDomainsAsync(
        int days, bool blockedOnly, int limit = 20, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow.Date.AddDays(-days);
        var rows = await db.DailyDomains
            .Where(d => d.Day >= from && (!blockedOnly || d.Blocked > 0))
            .GroupBy(d => d.Domain)
            .Select(g => new
            {
                Domain = g.Key,
                Total = g.Sum(x => x.Total),
                Blocked = g.Sum(x => x.Blocked),
            })
            .OrderByDescending(x => x.Total)
            .Take(limit)
            .ToListAsync(ct);

        // The number of devices per domain is not in the daily totals - it
        // would only be available with a third table, which would eat the
        // storage saving again. So 0 rather than a guess.
        return rows.Select(r => new DomainCount(r.Domain, r.Total, r.Blocked, 0)).ToList();
    }

    public async Task<List<ClientSummary>> GetClientsAsync(int days, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow.Date.AddDays(-days);
        var rows = await db.DailyClients
            .Where(d => d.Day >= from)
            .GroupBy(d => d.Client)
            .Select(g => new
            {
                Client = g.Key,
                Name = g.Max(x => x.ClientName),
                Total = g.Sum(x => x.Total),
                Blocked = g.Sum(x => x.Blocked),
                LastDay = g.Max(x => x.Day),
            })
            .OrderByDescending(x => x.Total)
            .ToListAsync(ct);

        return rows
            .Select(r => new ClientSummary(r.Client, r.Name, null, r.Total, r.Blocked, 0, r.LastDay))
            .ToList();
    }
}
