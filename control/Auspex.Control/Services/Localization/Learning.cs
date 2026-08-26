namespace Auspex.Control.Services.Localization;

// Learn mode, "Why?" and backup.

public abstract partial class Strings
{
    // ── Lernmodus ─────────────────────────────────────────────────────────
    public abstract string TitleLearnMode { get; }
    public abstract string LearnIntro { get; }
    public abstract string NoProfileLearning { get; }
    public abstract string DomainsAndSilence(int domains, string stille);
    public abstract string LimitReached { get; }
    public abstract string NothingLearnedYet { get; }
    /// <param name="silence">How long nothing new has arrived.</param>
    public abstract string NothingNewSince(TimeSpan stille);

    public abstract string AllowlistFor(string profile);
    public abstract string PerDomain { get; }
    public abstract string ExactNames { get; }
    public abstract string DiscardLearned { get; }
    public abstract string AllowlistNote { get; }
    public abstract string ObservedNames(int count);
    public abstract string ColumnRegistrableDomain { get; }
    public abstract string ColumnTypes { get; }
    public abstract string ColumnLastSeen { get; }
    public abstract string Forget { get; }
    public abstract string NothingLearnedYetComment { get; }

    // ── Warum? ────────────────────────────────────────────────────────────
    public abstract string TitleWhy { get; }
    public abstract string WhyHeading { get; }
    public abstract string WhyIntro { get; }
    public abstract string ClientIpOptional { get; }
    public abstract string Check { get; }
    public abstract string WithClientIp { get; }
    public abstract string DataPlaneGoneShort { get; }
    public abstract string Blocked { get; }
    public abstract string Allowed { get; }
    public abstract string Reason { get; }
    public abstract string Rule { get; }
    public abstract string Origin { get; }
    public abstract string LineNumber(int line);
    public abstract string Profile { get; }
    public abstract string Schedule { get; }
    public abstract string CurrentlyActive { get; }

    // ── Sicherung ─────────────────────────────────────────────────────────
    public abstract string TitleBackup { get; }
    public abstract string BackupIntro { get; }
    public abstract string CreateBackup { get; }
    public abstract string CreateBackupNote { get; }
    public abstract string Download { get; }
    public abstract string RestoreBackup { get; }
    public abstract string RestoreBackupNote { get; }
    public abstract string Reading { get; }
    public abstract string NewlyApplied(string count);
    public abstract string Findings { get; }
    public abstract string DailyTotals { get; }
    public abstract string OwnRules { get; }
    public abstract string LearnedState { get; }
    public abstract string NameCount(string count);
    public abstract string FileNotReadable(string reason);
}

public sealed partial class StringsDe
{
    public override string TitleLearnMode => "Lernmodus";
    public override string LearnIntro =>
        "Beobachten, was ein Gerät tatsächlich braucht — dann alles andere "
        + "dichtmachen. Gelernt wird nur, was der Filter durchgelassen hat.";
    public override string NoProfileLearning =>
        "Kein Profil lernt gerade. In der Konfiguration bei einem Client "
        + "policy: \"learn\" setzen.";
    public override string DomainsAndSilence(int domains, string stille) =>
        $"{domains} Domains · {stille}";
    public override string LimitReached => "Limit erreicht — Store unvollständig";
    public override string NothingLearnedYet => "noch nichts gelernt";
    public override string NothingNewSince(TimeSpan s) =>
        s.TotalDays >= 1 ? $"seit {(int)s.TotalDays} t nichts Neues"
        : s.TotalHours >= 1 ? $"seit {(int)s.TotalHours} h nichts Neues"
        : $"seit {(int)s.TotalMinutes} min nichts Neues";

    public override string AllowlistFor(string profile) => $"Allowlist für {profile}";
    public override string PerDomain => "je Domain (CDN-tauglich)";
    public override string ExactNames => "exakte Namen (streng)";
    public override string DiscardLearned => "Lernstand verwerfen";
    public override string AllowlistNote =>
        "Diese Zeilen unter allow_rules des Profils eintragen, danach "
        + "policy: \"enforce\" setzen.";
    public override string ObservedNames(int count) => $"Beobachtete Namen ({count:N0})";
    public override string ColumnRegistrableDomain => "Registrierbare Domain";
    public override string ColumnTypes => "Typen";
    public override string ColumnLastSeen => "Zuletzt";
    public override string Forget => "vergessen";
    public override string NothingLearnedYetComment => "# noch nichts gelernt";

    public override string TitleWhy => "Warum?";
    public override string WhyHeading => "Warum wurde das geblockt?";
    public override string WhyIntro =>
        "Prüft eine Domain gegen den aktiven Regelsatz, ohne eine echte Anfrage "
        + "zu stellen.";
    public override string ClientIpOptional => "Client-IP (optional)";
    public override string Check => "Prüfen";
    public override string WithClientIp =>
        "Mit Client-IP werden auch Profil-Regeln und aktive Zeitfenster mitgeprüft.";
    public override string DataPlaneGoneShort => "Datenebene nicht erreichbar.";
    public override string Blocked => "geblockt";
    public override string Allowed => "erlaubt";
    public override string Reason => "Grund";
    public override string Rule => "Regel";
    public override string Origin => "Herkunft";
    public override string LineNumber(int line) => $", Zeile {line}";
    public override string Profile => "Profil";
    public override string Schedule => "Zeitfenster";
    public override string CurrentlyActive => "(gerade aktiv)";

    public override string TitleBackup => "Sicherung";
    public override string BackupIntro =>
        "Gesichert wird alles, was beim Verlust der Volumes weh tut: die "
        + "Geschichte, die Funde, die Tageswerte, eigene Regeln, verwaltete "
        + "Listen und der Lernstand.";
    public override string CreateBackup => "Sicherung erstellen";
    public override string CreateBackupNote =>
        "Ein ZIP-Archiv. Die Datenbank wird dabei nicht roh kopiert, sondern "
        + "konsistent herausgeschrieben — eine Dateikopie würde den noch nicht "
        + "eingearbeiteten Teil verlieren.";
    public override string Download => "Herunterladen";
    public override string RestoreBackup => "Sicherung zurückspielen";
    public override string RestoreBackupNote =>
        "Zusammenführend, nicht ersetzend: was seit der Sicherung dazugekommen "
        + "ist, bleibt erhalten. Stammt die Sicherung aus einem anderen "
        + "Schema-Stand, wird sie abgelehnt statt verbogen.";
    public override string Reading => "wird eingelesen …";
    public override string NewlyApplied(string count) => $"{count} neu übernommen";
    public override string Findings => "Funde";
    public override string DailyTotals => "Tageswerte";
    public override string OwnRules => "Eigene Regeln";
    public override string LearnedState => "Lernstand";
    public override string NameCount(string count) => $"{count} Namen";
    public override string FileNotReadable(string reason) =>
        $"Datei konnte nicht gelesen werden: {reason}";
}

public sealed partial class StringsEn
{
    public override string TitleLearnMode => "Learning mode";
    public override string LearnIntro =>
        "Watch what a device actually needs — then shut everything else. Only "
        + "what the filter let through is ever learned.";
    public override string NoProfileLearning =>
        "No profile is learning right now. Set policy: \"learn\" on a client in "
        + "the configuration.";
    public override string DomainsAndSilence(int domains, string stille) =>
        $"{domains} domains · {stille}";
    public override string LimitReached => "Limit reached — store incomplete";
    public override string NothingLearnedYet => "nothing learned yet";
    public override string NothingNewSince(TimeSpan s) =>
        s.TotalDays >= 1 ? $"nothing new for {(int)s.TotalDays} d"
        : s.TotalHours >= 1 ? $"nothing new for {(int)s.TotalHours} h"
        : $"nothing new for {(int)s.TotalMinutes} min";

    public override string AllowlistFor(string profile) => $"Allow list for {profile}";
    public override string PerDomain => "per domain (works with CDNs)";
    public override string ExactNames => "exact names (strict)";
    public override string DiscardLearned => "Discard what was learned";
    public override string AllowlistNote =>
        "Put these lines under the profile's allow_rules, then set "
        + "policy: \"enforce\".";
    public override string ObservedNames(int count) => $"Names seen ({count:N0})";
    public override string ColumnRegistrableDomain => "Registrable domain";
    public override string ColumnTypes => "Types";
    public override string ColumnLastSeen => "Last seen";
    public override string Forget => "forget";
    public override string NothingLearnedYetComment => "# nothing learned yet";

    public override string TitleWhy => "Why?";
    public override string WhyHeading => "Why was that blocked?";
    public override string WhyIntro =>
        "Checks a domain against the active rule set without making a real query.";
    public override string ClientIpOptional => "Client IP (optional)";
    public override string Check => "Check";
    public override string WithClientIp =>
        "With a client IP, profile rules and active time windows are checked too.";
    public override string DataPlaneGoneShort => "Data plane unreachable.";
    public override string Blocked => "blocked";
    public override string Allowed => "allowed";
    public override string Reason => "Reason";
    public override string Rule => "Rule";
    public override string Origin => "Origin";
    public override string LineNumber(int line) => $", line {line}";
    public override string Profile => "Profile";
    public override string Schedule => "Time window";
    public override string CurrentlyActive => "(active right now)";

    public override string TitleBackup => "Backup";
    public override string BackupIntro =>
        "What gets backed up is everything that would hurt to lose with the "
        + "volumes: the history, the findings, the daily totals, your own rules, "
        + "the managed lists and what learning mode has gathered.";
    public override string CreateBackup => "Create a backup";
    public override string CreateBackupNote =>
        "A ZIP archive. The database is not copied raw but written out "
        + "consistently — a file copy would lose the part not yet committed.";
    public override string Download => "Download";
    public override string RestoreBackup => "Restore a backup";
    public override string RestoreBackupNote =>
        "Merging, not replacing: whatever arrived since the backup stays. If the "
        + "backup comes from a different schema version, it is refused rather "
        + "than forced.";
    public override string Reading => "reading …";
    public override string NewlyApplied(string count) => $"{count} newly taken over";
    public override string Findings => "Findings";
    public override string DailyTotals => "Daily totals";
    public override string OwnRules => "Your own rules";
    public override string LearnedState => "Learned";
    public override string NameCount(string count) => $"{count} names";
    public override string FileNotReadable(string reason) =>
        $"Could not read the file: {reason}";
}
