using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Auspex.Control.Data;
using Auspex.Control.Services.Localization;

namespace Auspex.Control.Services;

/// <summary>
/// Writes new findings to stdout as a single greppable line. Whiskers' log
/// alerts work on container logs — so exactly that is enough, with no second
/// notification channel.
/// </summary>
public sealed class FindingNotifier(
    AnalyticsDbContext db,
    IOptions<NotificationOptions> options,
    ILogger<FindingNotifier> log)
{
    private readonly NotificationOptions _opt = options.Value;

    /// <summary>
    /// The language reports go out in. Not the viewer's — there is no viewer
    /// at this point.
    /// </summary>
    private static readonly Strings Default = Strings.For(Strings.Kulturen[0]);

    private static readonly Dictionary<string, int> Rank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["info"] = 0,
        ["warn"] = 1,
        ["high"] = 2,
    };

    /// <summary>
    /// Reports everything not yet reported and records that. Idempotent: a
    /// second call sends nothing out twice.
    /// </summary>
    public async Task<int> FlushAsync(CancellationToken ct = default)
    {
        if (!_opt.Enabled) return 0;

        var minRank = Rank.GetValueOrDefault(_opt.MinSeverity, 1);
        var levels = Rank.Where(kv => kv.Value >= minRank).Select(kv => kv.Key).ToList();
        var cutoff = DateTime.UtcNow - _opt.MaxAge;

        var pending = await db.Findings
            .Where(f => f.NotifiedUtc == null
                        && !f.Dismissed
                        && f.DetectedUtc >= cutoff
                        && levels.Contains(f.Severity))
            .OrderByDescending(f => f.Score)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var send = pending.Take(_opt.MaxPerRun).ToList();

        foreach (var finding in send)
        {
            // Warning and deliberately not Error: a finding is not a fault in the
            // application, and the existing error rule should not fire on it.
            // The marker does the attribution.
            log.LogWarning("{Line}", Format(finding, _opt.Marker, Escalation(finding)));
        }

        var rest = pending.Count - send.Count;
        if (rest > 0)
        {
            log.LogWarning("{Line}", $"{_opt.Marker} [sammel] {rest} weitere Funde in diesem Durchgang "
                                     + "not reported individually (MaxPerRun reached)");
        }

        // The ones not reported individually count as done as well - otherwise
        // the flood repeats on the next pass. As one UPDATE rather than
        // through the change tracker: unambiguous and independent of how the
        // context is configured.
        var ids = pending.Select(f => f.Id).ToList();
        await db.Findings
            .Where(f => ids.Contains(f.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.NotifiedUtc, now), ct);

        return send.Count;
    }

    /// <summary>
    /// One line, no line breaks. Log rules work line by line — a wrapped
    /// finding would only match halfway.
    /// </summary>
    internal static string Format(Finding f, string marker, string? escalatePrefix = null)
    {
        var line = new StringBuilder();
        if (!string.IsNullOrEmpty(escalatePrefix))
        {
            line.Append(escalatePrefix).Append(' ');
        }
        line.Append(marker).Append(" [").Append(f.Severity).Append("] ").Append(f.Detector);
        line.Append(" client=").Append(Clean(f.Client));
        if (!string.IsNullOrEmpty(f.ClientName))
        {
            // As a field of its own rather than mixed into the address: the line
            // stays machine-splittable and the alert stays readable.
            line.Append(" name=\"").Append(Clean(f.ClientName)).Append('"');
        }
        if (!string.IsNullOrEmpty(f.Subject))
        {
            line.Append(" subject=").Append(Clean(f.Subject));
        }
        // A report going outwards has no reader whose language could be known -
        // it goes into a channel, not to a browser. So it takes the
        // installation's default language. Whoever wants that changed changes
        // the first line in Strings.Languages.
        var text = Default.Finding(f);
        line.Append(" :: ").Append(Clean(text.Titel));
        line.Append(" :: ").Append(Clean(text.Numbers));
        return line.ToString();
    }

    /// <summary>Only hard findings are lifted into the general alert channel.</summary>
    private string? Escalation(Finding f)
        => _opt.EscalateHigh && string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase)
            ? _opt.EscalatePrefix
            : null;

    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "-";
        var cleaned = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return cleaned.Length == 0 ? "-" : cleaned;
    }
}
