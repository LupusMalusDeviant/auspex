using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Auspex.Control.Data;

namespace Auspex.Control.Services;

/// <summary>
/// Runs the detectors regularly over the sliding one-hour window and files
/// the findings.
/// </summary>
public sealed class DetectionService(
    IServiceScopeFactory scopes,
    IOptions<AnalyticsOptions> options,
    ILogger<DetectionService> log) : BackgroundService
{
    private readonly AnalyticsOptions _opt = options.Value;

    public DateTime? LastRunUtc { get; private set; }
    public bool BaselineReady { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_opt.Enabled) return;

        // Give the ingest a head start, or the first pass runs over an empty
        // table.
        await Task.Delay(TimeSpan.FromSeconds(20), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Erkennungslauf fehlgeschlagen");
            }

            try
            {
                await Task.Delay(_opt.DetectionInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One pass, triggerable by hand too.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        var oldest = await db.Queries.MinAsync(q => (DateTime?)q.TimeUtc, ct);
        if (oldest is null)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var windowStart = now.AddHours(-1);

        // The baseline lies BEFORE the window by definition. Without the clamp
        // it goes negative on a fresh database - and every calculation built
        // on it turns to nonsense.
        var baselineStart = oldest.Value < windowStart ? oldest.Value : windowStart;

        var ctx = new DetectionContext(
            WindowStartUtc: windowStart,
            WindowEndUtc: now,
            BaselineStartUtc: baselineStart,
            HasBaseline: windowStart - baselineStart >= _opt.BaselineWarmup);

        BaselineReady = ctx.HasBaseline;

        var found = new List<Finding>();
        found.AddRange(await Detectors.NewDomainAsync(db, ctx, ct));
        found.AddRange(await Detectors.NxdomainFloodAsync(db, ctx, ct));
        found.AddRange(await Detectors.RepetitionBurstAsync(db, ctx, ct));
        found.AddRange(await Detectors.TunnelingAsync(db, ctx, ct));
        found.AddRange(await Detectors.FalsePositiveAsync(db, ctx, ct));
        found.AddRange(await Detectors.CorrelationAsync(db, ctx, ct));
        found.AddRange(await Detectors.SteadyTalkerAsync(db, ctx, ct));
        found.AddRange(await Detectors.RebindAsync(db, ctx, ct));
        found.AddRange(await Detectors.UnexplainedConnectionAsync(db, ctx, ct));

        var written = await PersistAsync(db, found, ctx, ct);
        LastRunUtc = now;

        if (written > 0)
        {
            log.LogInformation("{Count} new findings", written);
        }

        // Reporting is a step of its own: it also catches up on whatever an
        // earlier pass found but never got out.
        var notifier = scope.ServiceProvider.GetRequiredService<FindingNotifier>();
        await notifier.FlushAsync(ct);

        return written;
    }

    /// <summary>
    /// Writes findings away. Within the same hour exactly one entry is
    /// created per detector/client/subject - it grows rather than repeating
    /// itself every five minutes.
    /// </summary>
    private static async Task<int> PersistAsync(
        AnalyticsDbContext db, List<Finding> found, DetectionContext ctx, CancellationToken ct)
    {
        if (found.Count == 0) return 0;

        var hourKey = ctx.WindowEndUtc.ToString("yyyyMMddHH");
        foreach (var f in found)
        {
            f.DetectedUtc = DateTime.UtcNow;
            f.WindowStartUtc = ctx.WindowStartUtc;
            f.WindowEndUtc = ctx.WindowEndUtc;
            // A detector may set its own cadence. The default is the hour; what
            // is continuous by nature would otherwise report itself
            // twenty-four times a day with the same message.
            if (string.IsNullOrEmpty(f.Fingerprint))
            {
                f.Fingerprint = $"{f.Detector}|{f.Client}|{f.Subject}|{hourKey}";
            }
        }

        var fingerprints = found.Select(f => f.Fingerprint).ToList();
        var existing = await db.Findings
            .Where(f => fingerprints.Contains(f.Fingerprint))
            .ToDictionaryAsync(f => f.Fingerprint, ct);

        var added = 0;
        foreach (var f in found)
        {
            if (existing.TryGetValue(f.Fingerprint, out var old))
            {
                // Correct upwards only: a storm that is still going should show
                // the higher figure.
                if (f.Score > old.Score)
                {
                    old.Score = f.Score;
                    // The measurements, no longer the sentences: those are
                    // produced at display time.
                    old.Values = f.Values;
                    old.Severity = f.Severity;
                    old.WindowEndUtc = f.WindowEndUtc;
                }
                continue;
            }
            db.Findings.Add(f);
            existing[f.Fingerprint] = f;
            added++;
        }

        await db.SaveChangesAsync(ct);
        return added;
    }
}
