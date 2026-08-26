namespace Auspex.Control.Services.Localization;

// The overview: key figures, rule set, upstreams.

public abstract partial class Strings
{
    public abstract string TitleOverview { get; }
    public abstract string DataPlaneLabel { get; }

    public abstract string DataPlaneGone { get; }
    /// <param name="address">The address the resolver is expected at.</param>
    public abstract string DataPlaneGoneWhy(string address);

    // ── Kennzahlen ────────────────────────────────────────────────────────
    public abstract string CardQueries { get; }
    /// <param name="anzahl">Bereits formatierte Fehlerzahl.</param>
    public abstract string CardErrorNote(string count);
    public abstract string CardBlocked { get; }
    public abstract string CardQueriesNote(string count);
    public abstract string CardCacheRate { get; }
    public abstract string CardCacheNote(string entries, string prefetches);
    public abstract string CardUptime { get; }
    public abstract string CardVersion(string version);

    /// <summary>
    /// Uptime as "3d 4h". The unit abbreviations are language-dependent: in
    /// German the day is "t", not "d".
    /// </summary>
    public abstract string Uptime(TimeSpan span);

    // ── Regelsatz ─────────────────────────────────────────────────────────
    public abstract string RuleSet { get; }
    public abstract string RuleSetCounts(string block, string allow);
    public abstract string ReloadLists { get; }
    public abstract string ClearCache { get; }
    public abstract string ListsLoading { get; }
    public abstract string ListsReloaded { get; }
    public abstract string ListsFailed { get; }
    public abstract string CacheCleared { get; }
    public abstract string CacheClearFailed { get; }

    public abstract string ColumnList { get; }
    public abstract string ColumnLines { get; }
    public abstract string ColumnRules { get; }
    public abstract string ColumnSkipped { get; }
    public abstract string ColumnDuplicates { get; }
    public abstract string NoLists { get; }

    /// <param name="count">How many patterns are on both lists.</param>
    public abstract string ConflictsIntro(int count);

    // ── Upstreams ─────────────────────────────────────────────────────────
    public abstract string Upstreams { get; }
    public abstract string ColumnTarget { get; }
    public abstract string ColumnProtocol { get; }
    public abstract string ColumnStatus { get; }
    public abstract string ColumnErrors { get; }
    public abstract string ColumnLatency { get; }
    public abstract string Bench { get; }
    public abstract string Active { get; }
}

public sealed partial class StringsDe
{
    public override string TitleOverview => "Übersicht";
    public override string DataPlaneLabel => "Datenebene";

    public override string DataPlaneGone => "Datenebene nicht erreichbar";
    public override string DataPlaneGoneWhy(string address) =>
        $"Der Resolver antwortet nicht unter {address}. "
        + "Läuft auspex, und ist api.enabled gesetzt?";

    public override string CardQueries => "Anfragen";
    public override string CardErrorNote(string count) => $"{count} Fehler";
    public override string CardBlocked => "Geblockt";
    public override string CardQueriesNote(string count) => $"{count} Anfragen";
    public override string CardCacheRate => "Cache-Trefferquote";
    public override string CardCacheNote(string entries, string prefetches) =>
        $"{entries} Einträge, {prefetches} Prefetches";
    public override string CardUptime => "Laufzeit";
    public override string CardVersion(string version) => $"Version {version}";

    public override string Uptime(TimeSpan s) =>
        s.TotalDays >= 1 ? $"{(int)s.TotalDays}t {s.Hours}h"
        : s.TotalHours >= 1 ? $"{(int)s.TotalHours}h {s.Minutes}m"
        : $"{s.Minutes}m {s.Seconds}s";

    public override string RuleSet => "Regelsatz";
    public override string RuleSetCounts(string block, string allow) =>
        $"{block} Block · {allow} Allow";
    public override string ReloadLists => "Listen neu laden";
    public override string ClearCache => "Cache leeren";
    public override string ListsLoading => "Listen werden geladen …";
    public override string ListsReloaded => "Listen neu geladen.";
    public override string ListsFailed => "Reload fehlgeschlagen — siehe Resolver-Log.";
    public override string CacheCleared => "Cache geleert.";
    public override string CacheClearFailed => "Cache-Leeren fehlgeschlagen.";

    public override string ColumnList => "Liste";
    public override string ColumnLines => "Zeilen";
    public override string ColumnRules => "Regeln";
    public override string ColumnSkipped => "Übersprungen";
    public override string ColumnDuplicates => "Duplikate";
    public override string NoLists =>
        "Keine Listen geladen — es gelten nur die Regeln aus der Konfiguration.";

    public override string ConflictsIntro(int count) =>
        count == 1
            ? "Muster steht auf Block- und Allowlist (Allow gewinnt):"
            : "Muster stehen auf Block- und Allowlist (Allow gewinnt):";

    public override string Upstreams => "Upstreams";
    public override string ColumnTarget => "Ziel";
    public override string ColumnProtocol => "Protokoll";
    public override string ColumnStatus => "Status";
    public override string ColumnErrors => "Fehler";
    public override string ColumnLatency => "Ø Antwortzeit";
    public override string Bench => "Ersatzbank";
    public override string Active => "aktiv";
}

public sealed partial class StringsEn
{
    public override string TitleOverview => "Overview";
    public override string DataPlaneLabel => "Data plane";

    public override string DataPlaneGone => "Data plane unreachable";
    public override string DataPlaneGoneWhy(string address) =>
        $"The resolver is not answering at {address}. "
        + "Is auspex running, and is api.enabled turned on?";

    public override string CardQueries => "Queries";
    public override string CardErrorNote(string count) => $"{count} errors";
    public override string CardBlocked => "Blocked";
    public override string CardQueriesNote(string count) => $"{count} queries";
    public override string CardCacheRate => "Cache hit rate";
    public override string CardCacheNote(string entries, string prefetches) =>
        $"{entries} entries, {prefetches} prefetches";
    public override string CardUptime => "Uptime";
    public override string CardVersion(string version) => $"Version {version}";

    // "t" would not be a day in English. Otherwise the same form.
    public override string Uptime(TimeSpan s) =>
        s.TotalDays >= 1 ? $"{(int)s.TotalDays}d {s.Hours}h"
        : s.TotalHours >= 1 ? $"{(int)s.TotalHours}h {s.Minutes}m"
        : $"{s.Minutes}m {s.Seconds}s";

    public override string RuleSet => "Rule set";
    public override string RuleSetCounts(string block, string allow) =>
        $"{block} block · {allow} allow";
    public override string ReloadLists => "Reload lists";
    public override string ClearCache => "Purge cache";
    public override string ListsLoading => "Loading lists …";
    public override string ListsReloaded => "Lists reloaded.";
    public override string ListsFailed => "Reload failed — check the resolver log.";
    public override string CacheCleared => "Cache purged.";
    public override string CacheClearFailed => "Could not purge the cache.";

    public override string ColumnList => "List";
    public override string ColumnLines => "Lines";
    public override string ColumnRules => "Rules";
    public override string ColumnSkipped => "Skipped";
    public override string ColumnDuplicates => "Duplicates";
    public override string NoLists =>
        "No lists loaded — only the rules from the configuration apply.";

    public override string ConflictsIntro(int count) =>
        count == 1
            ? "pattern is on both the block and allow list (allow wins):"
            : "patterns are on both the block and allow list (allow wins):";

    public override string Upstreams => "Upstreams";
    public override string ColumnTarget => "Target";
    public override string ColumnProtocol => "Protocol";
    public override string ColumnStatus => "Status";
    public override string ColumnErrors => "Errors";
    public override string ColumnLatency => "Avg. response";
    public override string Bench => "Benched";
    public override string Active => "active";
}
