using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Auspex.Control.Data;

namespace Auspex.Control.Services;

/// <summary>
/// Collects the data plane's query log and writes it away permanently. The
/// ring buffer there holds only minutes; the history that the analysis can
/// work on at all comes into being here.
/// </summary>
public sealed class IngestService(
    IServiceScopeFactory scopes,
    IAuspexClient auspex,
    IOptions<AnalyticsOptions> options,
    ILogger<IngestService> log) : BackgroundService
{
    private readonly AnalyticsOptions _opt = options.Value;
    private DateTime _lastRetention = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_opt.Enabled)
        {
            log.LogInformation("Analytics is switched off, no ingest");
            return;
        }

        // The resolver is welcome to start later than the dashboard.
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollAsync(ct);
                await RunMaintenanceAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // An error while collecting must not stop the service —
                // otherwise a brief network hiccup leaves a permanent gap.
                log.LogError(ex, "Ingest-Durchlauf fehlgeschlagen");
            }

            try
            {
                await Task.Delay(_opt.PollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        var state = await db.IngestStates.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (state is null)
        {
            state = new IngestState { Id = 1 };
            db.IngestStates.Add(state);
            await db.SaveChangesAsync(ct);
        }

        var batch = await auspex.GetQueryLogStreamAsync(state.LastSeq, _opt.BatchSize, ct);
        if (batch is null)
        {
            return; // data plane not reachable right now
        }

        // The data plane restarted: the sequence begins at 1 again, and the old
        // cursor would be too high from now on and skip everything.
        if (!string.IsNullOrEmpty(state.Boot) && state.Boot != batch.Boot)
        {
            log.LogInformation("The resolver was restarted, the cursor has been reset");
            state.Boot = batch.Boot;
            state.LastSeq = 0;
            await db.SaveChangesAsync(ct);

            batch = await auspex.GetQueryLogStreamAsync(0, _opt.BatchSize, ct);
            if (batch is null) return;
        }
        state.Boot = batch.Boot;

        if (batch.Lost > 0)
        {
            state.LostTotal += batch.Lost;
            log.LogWarning(
                "{Lost} entries missed - the data plane's ring buffer overflowed. " +
                "Shorten the poll interval or raise querylog.size.", batch.Lost);
        }

        if (batch.Entries.Length > 0)
        {
            db.Queries.AddRange(batch.Entries.Select(e => ToRecord(e, batch.Boot)));

            // And the addresses the names pointed at. They always came along and
            // used to be thrown away - without them the question "where does
            // this device send things?" stops at the name.
            await Geo.DestinationCapture.RecordAsync(db, batch.Entries, ct);

            state.LastSeq = batch.Next;
            state.Ingested += batch.Entries.Length;
        }
        state.LastRunUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    private static QueryRecord ToRecord(QueryLogEntry e, string boot) => new()
    {
        Seq = e.Seq,
        Boot = boot,
        TimeUtc = e.Time.UtcDateTime,
        Client = e.Client,
        ClientName = e.ClientName,
        Profile = e.Profile,
        Name = e.Name,
        Domain = string.IsNullOrEmpty(e.Domain) ? e.Name : e.Domain,
        Type = e.Type,
        Action = e.Action,
        Source = e.Source,
        Rule = e.Rule,
        Cname = e.Cname,
        List = e.List,
        Schedule = e.Schedule,
        Upstream = e.Upstream,
        Rcode = e.Rcode,
        Validated = e.Validated,
        Millis = e.Millis,
        Error = e.Error,
        LongestLabel = LongestLabel(e.Name, e.Domain),
    };

    /// <summary>
    /// The longest label to the left of the registrable domain. Short names
    /// are unremarkable; very long labels are the signature of DNS
    /// tunnelling, where payload sits inside the name.
    /// </summary>
    internal static int LongestLabel(string name, string? domain)
    {
        if (string.IsNullOrEmpty(name)) return 0;

        var prefix = name;
        if (!string.IsNullOrEmpty(domain) && name.EndsWith("." + domain, StringComparison.Ordinal))
        {
            prefix = name[..^(domain.Length + 1)];
        }
        else if (name == domain)
        {
            return 0;
        }

        var longest = 0;
        foreach (var label in prefix.Split('.'))
        {
            if (label.Length > longest) longest = label.Length;
        }
        return longest;
    }

    /// <summary>
    /// Roll up and clean out, in that order: the daily totals have to stand
    /// first, then the raw data may go. The other way round the history
    /// would be lost.
    /// </summary>
    private async Task RunMaintenanceAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _lastRetention < TimeSpan.FromHours(6)) return;
        _lastRetention = DateTime.UtcNow;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        await scope.ServiceProvider.GetRequiredService<RollupService>().RunAsync(ct);

        if (_opt.RetentionDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-_opt.RetentionDays);
            var removed = await db.Queries.Where(q => q.TimeUtc < cutoff).ExecuteDeleteAsync(ct);
            if (removed > 0)
            {
                log.LogInformation("{Removed} queries older than {Days} days deleted",
                    removed, _opt.RetentionDays);
            }

            // The same retention for the name -> address mapping. Large
            // providers change their addresses constantly; without cleaning
            // out, the table keeps growing with every rotation while the old
            // rows no longer explain anything.
            var oldMapping = await db.Resolutions
                .Where(a => a.LastUtc < cutoff).ExecuteDeleteAsync(ct);

            // And the destinations no mapping points at any more.
            var orphaned = await db.Destinations
                .Where(z => z.LastUtc < cutoff
                            && !db.Resolutions.Any(a => a.Ip == z.Ip))
                .ExecuteDeleteAsync(ct);

            // And the observed connections. A program that has reported nothing
            // for months no longer explains anything.
            var oldConnections = await db.Connections
                .Where(v => v.LastUtc < cutoff).ExecuteDeleteAsync(ct);
            if (oldConnections > 0)
            {
                log.LogInformation("{Count} connections older than {Days} days deleted",
                    oldConnections, _opt.RetentionDays);
            }

            if (oldMapping > 0 || orphaned > 0)
            {
                log.LogInformation(
                    "{Mappings} mappings and {Destinations} destinations older than {Days} days deleted",
                    oldMapping, orphaned, _opt.RetentionDays);
            }
        }

        var rollup = scope.ServiceProvider.GetRequiredService<RollupService>();
        var old = await rollup.PruneAsync(_opt.AggregateRetentionDays, ct);
        if (old > 0)
        {
            log.LogInformation("{Rows} daily totals beyond the retention period deleted", old);
        }
    }
}
