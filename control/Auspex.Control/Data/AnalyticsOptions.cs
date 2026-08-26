namespace Auspex.Control.Data;

public class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    public bool Enabled { get; set; } = true;

    /// <summary>SQLite connection. The path is relative to the working directory.</summary>
    public string ConnectionString { get; set; } = "Data Source=var/analytics.db";

    /// <summary>How often the data plane is asked for new entries.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many entries per fetch. Has to match the ring buffer.</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Older raw data gets deleted. 0 means: never delete. The daily totals
    /// are unaffected by this — so this may safely be short.
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Retention for the rolled-up daily totals. They are orders of magnitude
    /// smaller than the raw data and may stay correspondingly long.
    /// </summary>
    public int AggregateRetentionDays { get; set; } = 730;

    public TimeSpan DetectionInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Warms the resolver's cache with the most-asked names. Its prefetch
    /// only renews what is hot anyway — after a restart the cache is empty,
    /// and the first session pays the full trip upstream for every name.
    /// </summary>
    public bool WarmingEnabled { get; set; } = true;

    public TimeSpan WarmingInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How many names. More quickly buys nothing: the tail is long.</summary>
    public int WarmingTop { get; set; } = 200;

    /// <summary>From how many days of history the list is built.</summary>
    public int WarmingDays { get; set; } = 7;

    /// <summary>
    /// Lead time before detectors may report anything. Without a baseline
    /// every observation is "new" and therefore every finding worthless.
    /// </summary>
    public TimeSpan BaselineWarmup { get; set; } = TimeSpan.FromDays(2);
}
