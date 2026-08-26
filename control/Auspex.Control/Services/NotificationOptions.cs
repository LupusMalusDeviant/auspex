namespace Auspex.Control.Services;

/// <summary>
/// Reporting findings outwards. Whiskers alerts through rules on container
/// logs — so writing a recognisable line to stdout is enough. No additional
/// channel, no coupling to an API.
/// </summary>
public class NotificationOptions
{
    public const string SectionName = "Notifications";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The marker word the Whiskers rule matches on. Has to be distinctive
    /// enough not to turn up by accident in other log lines.
    /// </summary>
    public string Marker { get; set; } = "AUSPEX-FUND";

    /// <summary>From which severity upwards to report: info | warn | high.</summary>
    public string MinSeverity { get; set; } = "warn";

    /// <summary>
    /// A cap per pass. After a longer disturbance hundreds of lines could
    /// otherwise go out at once and make the alerting useless. The rest is
    /// reported as a single summary line.
    /// </summary>
    public int MaxPerRun { get; set; } = 20;

    /// <summary>
    /// Lifts hard findings into Whiskers' existing error rule. That listens
    /// to every container and matches on "[ERROR]" among other things — so
    /// no log alert rule of our own is needed, which is convenient, because
    /// rules can only be read over MCP and not created.
    ///
    /// Deliberately for "high" only: the error rule is the general alert
    /// channel, and it loses its value if every new domain lands in it. Once
    /// a rule of its own exists on AUSPEX-FUND, this belongs back on false.
    /// </summary>
    public bool EscalateHigh { get; set; }

    /// <summary>The prefix the existing error rule matches on.</summary>
    public string EscalatePrefix { get; set; } = "[ERROR]";

    /// <summary>
    /// Findings older than this are no longer reported. Stops half the
    /// history going out retroactively when MinSeverity is lowered.
    /// </summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(6);
}
