using Microsoft.EntityFrameworkCore;
using Auspex.Control.Data;

namespace Auspex.Control.Services;

/// <summary>
/// Rolls completed days up into daily totals. The raw data grows to the
/// retention limit and then disappears — without the roll-up, any analysis
/// over a longer period would be impossible after that.
///
/// Rolling up happens as soon as a day is complete, not shortly before
/// deletion: then the timing is independent of the retention setting, and
/// shortening retention tears no hole.
/// </summary>
public sealed class RollupService(AnalyticsDbContext db, ILogger<RollupService> log)
{
    /// <summary>
    /// Rolls up every completed day that has no daily totals yet. Returns
    /// the number of days rolled up.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var oldest = await db.Queries.MinAsync(q => (DateTime?)q.TimeUtc, ct);
        if (oldest is null) return 0;

        var today = DateTime.UtcNow.Date;
        var already = (await db.DailyTotals
                .Where(d => d.Day >= oldest.Value.Date)
                .Select(d => d.Day)
                .ToListAsync(ct))
            .ToHashSet();

        var rolledUp = 0;
        for (var day = oldest.Value.Date; day < today; day = day.AddDays(1))
        {
            if (already.Contains(day)) continue;
            if (await RollUpDayAsync(day, ct))
            {
                rolledUp++;
            }
        }

        if (rolledUp > 0)
        {
            log.LogInformation("{Days} Tage zu Tageswerten verdichtet", rolledUp);
        }
        return rolledUp;
    }

    private async Task<bool> RollUpDayAsync(DateTime day, CancellationToken ct)
    {
        var until = day.AddDays(1);
        var rows = db.Queries.Where(q => q.TimeUtc >= day && q.TimeUtc < until);

        var total = await rows.LongCountAsync(ct);
        if (total == 0)
        {
            // A day with no raw data was either quiet or already deleted. Neither
            // produces a daily total - creating one full of zeroes would fake
            // a measurement that never happened.
            return false;
        }

        var perClient = await rows
            .GroupBy(q => q.Client)
            .Select(g => new
            {
                Client = g.Key,
                Name = g.Max(x => x.ClientName),
                Total = g.LongCount(),
                Blocked = g.LongCount(x => x.Action == "blocked"),
            })
            .ToListAsync(ct);

        var perDomain = await rows
            .GroupBy(q => q.Domain)
            .Select(g => new
            {
                Domain = g.Key,
                Total = g.LongCount(),
                Blocked = g.LongCount(x => x.Action == "blocked"),
            })
            .ToListAsync(ct);

        db.DailyTotals.Add(new DailyTotal
        {
            Day = day,
            Total = total,
            Blocked = await rows.LongCountAsync(q => q.Action == "blocked", ct),
            Validated = await rows.LongCountAsync(q => q.Validated, ct),
            Upstream = await rows.LongCountAsync(q => q.Source == "upstream", ct),
            Clients = perClient.Count,
            Domains = perDomain.Count,
        });

        db.DailyClients.AddRange(perClient.Select(c => new DailyClient
        {
            Day = day,
            Client = c.Client,
            ClientName = c.Name,
            Total = c.Total,
            Blocked = c.Blocked,
        }));

        db.DailyDomains.AddRange(perDomain.Select(d => new DailyDomain
        {
            Day = day,
            Domain = d.Domain,
            Total = d.Total,
            Blocked = d.Blocked,
        }));

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Entfernt Tageswerte jenseits ihrer eigenen Aufbewahrungsfrist.</summary>
    public async Task<int> PruneAsync(int aggregateRetentionDays, CancellationToken ct = default)
    {
        if (aggregateRetentionDays <= 0) return 0;

        var cutoff = DateTime.UtcNow.Date.AddDays(-aggregateRetentionDays);
        var removed = await db.DailyTotals.Where(d => d.Day < cutoff).ExecuteDeleteAsync(ct);
        removed += await db.DailyClients.Where(d => d.Day < cutoff).ExecuteDeleteAsync(ct);
        removed += await db.DailyDomains.Where(d => d.Day < cutoff).ExecuteDeleteAsync(ct);
        return removed;
    }
}
