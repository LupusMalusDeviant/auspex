using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Auspex.Control.Data;

namespace Auspex.Control.Services;

/// <summary>
/// Warms the resolver's cache with the names the network asks for constantly
/// anyway.
///
/// The resolver's prefetch only renews what is hot right now. Two gaps
/// remain: after a restart the cache is empty, and a name asked for three
/// times a day never reaches the prefetch threshold within one TTL. The
/// history closes both — it knows what counts.
/// </summary>
public sealed class CacheWarmingService(
    IServiceScopeFactory scopes,
    IAuspexClient auspex,
    IOptions<AnalyticsOptions> options,
    ILogger<CacheWarmingService> log) : BackgroundService
{
    private readonly AnalyticsOptions _opt = options.Value;

    /// <summary>
    /// Boot id of the resolver last warmed. If it changes, there was a
    /// restart and the cache is empty — then it is worth doing straight away
    /// rather than waiting for the next tick.
    /// </summary>
    private string? _warmedInstance;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_opt.Enabled || !_opt.WarmingEnabled) return;

        // Give the ingest a head start, or there is no history yet.
        await Task.Delay(TimeSpan.FromSeconds(45), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await WarmAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Warming failed");
            }

            try
            {
                // After a resolver restart, do not wait for the next big tick:
                // check more often, but only warm when there is something to
                // do.
                await Task.Delay(Shorter(_opt.WarmingInterval), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static TimeSpan Shorter(TimeSpan beat)
    {
        var half = TimeSpan.FromMinutes(2);
        return beat < half ? beat : half;
    }

    private DateTime _last = DateTime.MinValue;

    private async Task WarmAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        var state = await db.IngestStates.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
        var restart = state is not null
            && !string.IsNullOrEmpty(state.Boot)
            && state.Boot != _warmedInstance;

        var due = DateTime.UtcNow - _last >= _opt.WarmingInterval;
        if (!restart && !due) return;

        var from = DateTime.UtcNow.AddDays(-_opt.WarmingDays);
        var names = await db.Queries
            .Where(q => q.TimeUtc >= from && q.Action == "allowed")
            .GroupBy(q => q.Name)
            .Select(g => new { Name = g.Key, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .Take(_opt.WarmingTop)
            .Select(x => x.Name)
            .ToListAsync(ct);

        if (names.Count == 0) return;

        if (await auspex.WarmCacheAsync(names, ct))
        {
            _last = DateTime.UtcNow;
            _warmedInstance = state?.Boot;
            log.LogInformation("{Count} names handed over for warming{Reason}",
                names.Count, restart ? " (the resolver was restarted)" : "");
        }
    }
}
