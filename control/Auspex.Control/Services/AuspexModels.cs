using System.Text.Json.Serialization;

namespace Auspex.Control.Services;

// The data plane delivers snake_case; an explicit JsonPropertyName only
// appears where the Go name differs from it.

public record ResolverStats(
    long Queries,
    long Blocked,
    long Rewritten,
    long CacheHits,
    long Errors,
    long Prefetches);

public record CacheStats(
    int Entries,
    long Hits,
    long Misses,
    long StaleHits,
    long Evictions)
{
    public double HitRate => Hits + Misses == 0 ? 0 : (double)Hits / (Hits + Misses);
}

public record ListStats(string Name, int Lines, int Rules, int Skipped, int Duplicates);

public record RuleStats(
    int BlockRules,
    int AllowRules,
    int Skipped,
    int Duplicates,
    string[]? Conflicts,
    ListStats[]? Lists);

public record QueryLogSummary(long Total, long Blocked, int Buffer)
{
    public double BlockRate => Total == 0 ? 0 : (double)Blocked / Total;
}

public record UpstreamHealth(
    string Addr,
    string Proto,
    int Failures,
    bool Benched,
    long Queries,
    long Errors,
    [property: JsonPropertyName("avg_ms")] double AvgMs);

public record AuspexStatus(
    string Version,
    double UptimeSec,
    ResolverStats Resolver,
    CacheStats Cache,
    RuleStats Rules,
    [property: JsonPropertyName("querylog")] QueryLogSummary QueryLog,
    UpstreamHealth[] Upstreams,
    LearnStats[]? Learning);

/// <summary>State of one learn store.</summary>
public record LearnStats(
    string Profile,
    string Policy,
    string Granularity,
    int Names,
    int Domains,
    DateTimeOffset Created,
    DateTimeOffset LastNew,
    bool Overflow,
    double QuietForSec)
{
    /// <summary>
    /// Time since the last new domain — the signal for whether a learn
    /// window has run long enough.
    /// </summary>
    public TimeSpan QuietFor => TimeSpan.FromSeconds(QuietForSec);
}

public record LearnEntry(
    string Name,
    string Domain,
    long Count,
    DateTimeOffset First,
    DateTimeOffset Last,
    string[]? Types);

public record Allowlist(string Profile, string Granularity, string[] Rules);

/// <summary>A filter list managed through the interface.</summary>
public record ManagedList(
    string Name,
    string Url,
    bool Allow,
    bool Enabled,
    DateTimeOffset Added);

/// <summary>An entry from the catalogue of proven lists.</summary>
public record KnownList(string Name, string Url, string Description, bool Allow);

/// <summary>A device profile managed through the interface.</summary>
public class ManagedClient
{
    public string Name { get; set; } = "";
    public List<string> Match { get; set; } = [];

    /// <summary>
    /// Binds the profile to devices rather than addresses. Necessary under
    /// IPv6: temporary addresses change daily, and a profile on an address
    /// then silently stops applying from tomorrow.
    /// </summary>
    public List<string> Macs { get; set; } = [];
    public string? Policy { get; set; }
    public bool? Filtering { get; set; }
    public List<string> BlockRules { get; set; } = [];
    public List<string> AllowRules { get; set; } = [];
    public List<string> BlockServices { get; set; } = [];

    /// <summary>
    /// Search engines this profile is sent to their filtered host for. Per
    /// profile, because a household is not one setting: the children's tablet
    /// and the workshop computer want different answers.
    /// </summary>
    public List<string> SafeSearch { get; set; } = [];
    public List<ManagedSchedule> Schedules { get; set; } = [];

    /// <summary>
    /// For input in the interface only — one line instead of a list.
    ///
    /// Explicitly not part of what goes to the resolver: it rejects unknown
    /// fields, and with a message nobody connects to a convenience in a view
    /// model ("unknown field match_text").
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string MatchText
    {
        get => string.Join(", ", Match);
        set => Match = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    /// <summary>
    /// A working copy for the editor, so that abandoning an edit has not
    /// already changed what is on screen.
    /// </summary>
    /// <remarks>
    /// It lives here rather than in the page because the failure mode is
    /// silent and expensive: saving replaces the stored profile whole, so a
    /// field the copy forgets is not left alone but deleted. Here it can be
    /// held to the model by a test that walks the properties; in the page it
    /// was a list somebody had to remember to extend, and twice nobody did.
    /// </remarks>
    public ManagedClient Copy() => new()
    {
        Name = Name,
        Match = [.. Match],
        Macs = [.. Macs],
        Policy = Policy,
        Filtering = Filtering,
        BlockRules = [.. BlockRules],
        AllowRules = [.. AllowRules],
        BlockServices = [.. BlockServices],
        SafeSearch = [.. SafeSearch],
        Schedules = [.. Schedules],
    };
}

public class ManagedSchedule
{
    public string Name { get; set; } = "";
    public List<string> Days { get; set; } = ["all"];
    public string From { get; set; } = "21:00";
    public string To { get; set; } = "07:00";
    public List<string> Block { get; set; } = [];
    public List<string> BlockServices { get; set; } = [];

    /// <summary>Applies inside the window, on top of the profile's own.</summary>
    public List<string> SafeSearch { get; set; } = [];

    public string DaysText
    {
        get => string.Join(", ", Days);
        set => Days = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }
}

public record ServiceEntry(string Key, string Name, string[]? Domains);

/// <summary>One entry from the SafeSearch catalogue.</summary>
public record SafeSearchProvider(string Key, string Name);

public record ListsResponse(
    ManagedList[]? Managed,
    KnownList[]? Known,
    Dictionary<string, ListStats>? Stats);

public record QueryLogEntry(
    long Seq,
    DateTimeOffset Time,
    string Client,
    string? ClientName,
    string? Profile,
    string Name,
    string? Domain,
    string Type,
    string Action,
    string Source,
    string? Rule,
    string? Cname,
    string? RuleKind,
    string? List,
    string? Schedule,
    string? Upstream,
    string Rcode,
    bool Validated,
    string[]? Answers,
    [property: JsonPropertyName("ms")] double Millis,
    string? Error);

/// <summary>Cursor fetch: everything after <c>since</c>, oldest first.</summary>
public record QueryLogBatch(
    string Boot,
    long Next,
    QueryLogEntry[] Entries,
    long Lost);

public record Explanation(
    string Name,
    string? Client,
    string? Profile,
    bool Blocked,
    string Action,
    string? Rule,
    string? Cname,
    string? RuleKind,
    string? List,
    int Line,
    string? Schedule,
    string Reason);

/// <summary>The resolver's answer to "who is behind this address?".</summary>
public class WhoEntry
{
    public string Ip { get; set; } = "";
    public string? Name { get; set; }
    public string? Mac { get; set; }
    public string? Profile { get; set; }
    public bool Known { get; set; }
}
