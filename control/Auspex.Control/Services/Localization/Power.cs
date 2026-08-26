namespace Auspex.Control.Services.Localization;

// The query log — the strip chart.
//
// The page with the most prose on it: every row says in half a sentence what
// was decided and why. Which is exactly why this is not a list of captions.

public abstract partial class Strings
{
    public abstract string TitleQueryLog { get; }
    public abstract string QueryLogIntro { get; }

    // Key help. The keys themselves are deliberately NOT translated: j/k/f/b/w
    // sit at a place on the keyboard, not in a language, and whoever has
    // learned them once should keep them across a language switch.
    public abstract string KeyMove { get; }
    public abstract string KeyAllow { get; }
    public abstract string KeyBlock { get; }
    public abstract string KeyWhy { get; }

    // ── Werkzeugleiste ────────────────────────────────────────────────────
    public abstract string AllDevices { get; }
    public abstract string AllDecisions { get; }
    public abstract string OnlyBlocked { get; }
    public abstract string OnlyAllowed { get; }
    public abstract string OnlyRewritten { get; }
    public abstract string OnlyErrors { get; }
    /// <param name="anzahl">Bereits formatierte Zahl.</param>
    public abstract string QuerySelection(string count);
    public abstract string Live { get; }
    public abstract string Refresh { get; }

    /// <param name="anfragen">Wie viele Anfragen im Ausschnitt.</param>
    /// <param name="zeilen">Zu wie vielen Zeilen sie zusammengefasst wurden.</param>
    public abstract string QueryLogTally(string queries, string lines);
    public abstract string QueryLogBlockedShare(int prozent);

    public abstract string NoEntries { get; }

    // ── Tabelle ───────────────────────────────────────────────────────────
    public abstract string QueryLogCaption { get; }
    public abstract string ColumnTime { get; }
    public abstract string ColumnDecision { get; }
    public abstract string ColumnQuery { get; }
    public abstract string ColumnActions { get; }
    public abstract string SameSecond { get; }
    public abstract string RepeatedTitle(int count);

    // ── Die Entscheidung als Satz ─────────────────────────────────────────
    public abstract string DecisionBlocked(string rcode);
    public abstract string DecisionAllowed { get; }
    public abstract string DecisionFromCache { get; }
    public abstract string DecisionStale { get; }
    public abstract string DecisionLocal { get; }
    public abstract string DecisionRewritten { get; }
    public abstract string DecisionSafeSearch { get; }
    public abstract string DecisionError(string rcode);

    public abstract string FromList { get; }
    public abstract string ViaCnameTo { get; }
    public abstract string Signed { get; }
    public abstract string SignedTitle { get; }
    public abstract string ProfilePrefix { get; }

    // ── Actions in the row ────────────────────────────────────────────────
    public abstract string Allow { get; }
    public abstract string Block { get; }
    public abstract string CreateProfile { get; }
    public abstract string CreateProfileTitle { get; }
    public abstract string Why { get; }
    public abstract string WhyTitle { get; }

    // ── Feedback after a rule change ──────────────────────────────────────
    public abstract string RuleReason(string device);
    public abstract string RuleApplied(string rule);
    public abstract string RuleWrittenButOffline(string rule);
    public abstract string RuleFailed(string? error);
}

public sealed partial class StringsDe
{
    public override string TitleQueryLog => "Query-Log";
    public override string QueryLogIntro =>
        "Ein Ereignis, eine Zeile. Die Uhrzeit steht nur beim Sekundenwechsel — "
        + "was darunter ohne Zeitangabe folgt, geschah im selben Takt.";

    public override string KeyMove => "bewegen";
    public override string KeyAllow => "freigeben";
    public override string KeyBlock => "blocken";
    public override string KeyWhy => "warum?";

    public override string AllDevices => "alle Geräte";
    public override string AllDecisions => "alle Entscheidungen";
    public override string OnlyBlocked => "nur geblockt";
    public override string OnlyAllowed => "nur durchgelassen";
    public override string OnlyRewritten => "nur umgeschrieben";
    public override string OnlyErrors => "nur Fehler";
    public override string QuerySelection(string count) => $"{count} Anfragen";
    public override string Live => "live";
    public override string Refresh => "Aktualisieren";

    public override string QueryLogTally(string queries, string lines) =>
        $"{queries} Anfragen in {lines} Zeilen";
    public override string QueryLogBlockedShare(int prozent) => $" · {prozent} % geblockt";

    public override string NoEntries =>
        "Keine Einträge. Läuft der Resolver, und ist querylog.enabled gesetzt?";

    public override string QueryLogCaption =>
        "Anfragen, gruppiert nach Name, Gerät und Sekunde";
    public override string ColumnTime => "Zeit";
    public override string ColumnDecision => "Entscheidung";
    public override string ColumnQuery => "Anfrage";
    public override string ColumnActions => "Handlungen";
    public override string SameSecond => "gleiche Sekunde";
    public override string RepeatedTitle(int count) =>
        $"{count} Anfragen in derselben Sekunde";

    public override string DecisionBlocked(string rcode) => $"geblockt · {rcode}";
    public override string DecisionAllowed => "durchgelassen";
    public override string DecisionFromCache => "durchgelassen · aus dem Zwischenspeicher";
    public override string DecisionStale => "durchgelassen · veraltete Antwort";
    public override string DecisionLocal => "durchgelassen · lokale Zone";
    public override string DecisionRewritten => "umgeschrieben";
    public override string DecisionSafeSearch => "umgeschrieben · gefilterte Suche";
    public override string DecisionError(string rcode) => $"Fehler · {rcode}";

    public override string FromList => " aus ";
    public override string ViaCnameTo => " · über CNAME auf ";
    public override string Signed => "signiert";
    public override string SignedTitle => "DNSSEC-validiert";
    public override string ProfilePrefix => " · Profil ";

    public override string Allow => "freigeben";
    public override string Block => "blocken";
    public override string CreateProfile => "Profil anlegen";
    public override string CreateProfileTitle => "Für dieses Gerät ein Profil anlegen";
    public override string Why => "warum?";
    public override string WhyTitle => "Warum diese Entscheidung?";

    public override string RuleReason(string device) =>
        $"aus dem Query-Log, Gerät {device}";
    public override string RuleApplied(string rule) =>
        $"{rule} übernommen, Regelsatz neu geladen.";
    public override string RuleWrittenButOffline(string rule) =>
        $"{rule} geschrieben, aber der Resolver war nicht erreichbar — "
        + "greift beim nächsten Neuladen.";
    public override string RuleFailed(string? error) =>
        $"Regel konnte nicht geschrieben werden: {error}";
}

public sealed partial class StringsEn
{
    public override string TitleQueryLog => "Query log";
    public override string QueryLogIntro =>
        "One event, one line. The clock shows only when the second changes — "
        + "whatever follows without a time happened in the same beat.";

    public override string KeyMove => "move";
    public override string KeyAllow => "allow";
    public override string KeyBlock => "block";
    public override string KeyWhy => "why?";

    public override string AllDevices => "all devices";
    public override string AllDecisions => "all decisions";
    public override string OnlyBlocked => "blocked only";
    public override string OnlyAllowed => "allowed only";
    public override string OnlyRewritten => "rewritten only";
    public override string OnlyErrors => "errors only";
    public override string QuerySelection(string count) => $"{count} queries";
    public override string Live => "live";
    public override string Refresh => "Refresh";

    public override string QueryLogTally(string queries, string lines) =>
        $"{queries} queries in {lines} lines";
    public override string QueryLogBlockedShare(int prozent) => $" · {prozent}% blocked";

    public override string NoEntries =>
        "Nothing logged. Is the resolver running, and is querylog.enabled turned on?";

    public override string QueryLogCaption =>
        "Queries, grouped by name, device and second";
    public override string ColumnTime => "Time";
    public override string ColumnDecision => "Decision";
    public override string ColumnQuery => "Query";
    public override string ColumnActions => "Actions";
    public override string SameSecond => "same second";
    public override string RepeatedTitle(int count) =>
        $"{count} queries within the same second";

    public override string DecisionBlocked(string rcode) => $"blocked · {rcode}";
    public override string DecisionAllowed => "allowed";
    public override string DecisionFromCache => "allowed · from cache";
    public override string DecisionStale => "allowed · stale answer";
    public override string DecisionLocal => "allowed · local zone";
    public override string DecisionRewritten => "rewritten";
    public override string DecisionSafeSearch => "rewritten · filtered search";
    public override string DecisionError(string rcode) => $"Error · {rcode}";

    public override string FromList => " from ";
    public override string ViaCnameTo => " · via CNAME to ";
    public override string Signed => "signed";
    public override string SignedTitle => "DNSSEC validated";
    public override string ProfilePrefix => " · profile ";

    public override string Allow => "allow";
    public override string Block => "block";
    public override string CreateProfile => "create profile";
    public override string CreateProfileTitle => "Create a profile for this device";
    public override string Why => "why?";
    public override string WhyTitle => "Why this decision?";

    public override string RuleReason(string device) =>
        $"from the query log, device {device}";
    public override string RuleApplied(string rule) =>
        $"{rule} applied, rule set reloaded.";
    public override string RuleWrittenButOffline(string rule) =>
        $"{rule} written, but the resolver was unreachable — "
        + "it takes effect on the next reload.";
    public override string RuleFailed(string? error) =>
        $"Could not write the rule: {error}";
}
