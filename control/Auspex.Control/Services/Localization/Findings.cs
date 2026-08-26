using Auspex.Control.Services;

namespace Auspex.Control.Services.Localization;

// Findings and rule impact.

public abstract partial class Strings
{
    // ── Findings ──────────────────────────────────────────────────────────
    public abstract string TitleFindings { get; }
    public abstract string FindingsIntro { get; }
    public abstract string ShowDismissed { get; }
    public abstract string LastChecked(string clockTime) ;
    public abstract string CheckNow { get; }
    public abstract string BaselineTitle { get; }
    public abstract string BaselineExplained { get; }
    public abstract string NothingUnusual { get; }
    public abstract string Dismissed { get; }
    public abstract string Client { get; }
    public abstract string Domain { get; }
    public abstract string Numbers { get; }
    public abstract string WindowFromTo(string from, string until);
    public abstract string Suggestion { get; }
    public abstract string Applied(string wann);
    public abstract string CreateException { get; }
    public abstract string ExceptionCreated(string rule);
    public abstract string ExceptionWrittenOffline(string rule);
    public abstract string ExceptionFailed(string? error);
    public abstract string SeverityHigh { get; }
    public abstract string SeverityWarn { get; }
    public abstract string SeverityInfo { get; }

    // ── Regelwirkung ──────────────────────────────────────────────────────
    public abstract string TitleImpact { get; }
    public abstract string ImpactHeading { get; }
    public abstract string ImpactIntro { get; }
    public abstract string LastDays(int days);
    public abstract string Calculate { get; }
    public abstract string FormatsNote { get; }
    public abstract string NotADnsRule { get; }
    public abstract string AffectedQueries { get; }
    public abstract string WouldChange { get; }
    public abstract string Period { get; }
    public abstract string FirstToLast { get; }
    public abstract string BlockedTodayAlready(string count);
    public abstract string ImpactSentence(bool isException, long wouldChange);
    public abstract string NoMatch { get; }
    public abstract string ColumnMatches { get; }
    public abstract string ColumnAffected { get; }
    public abstract string AffectedNames { get; }

    /// <summary>How far a rule reaches.</summary>
    public abstract string RuleKindLabel(RuleKind kind);

    /// <summary>Whether the rule blocks or allows.</summary>
    public abstract string RuleImpact(bool isException);
}

public sealed partial class StringsDe
{
    public override string TitleFindings => "Auffälligkeiten";
    public override string FindingsIntro =>
        "Heuristiken, keine Wahrheit. Jeder Fund nennt die Zahlen, auf denen er "
        + "beruht — prüfen musst du selbst.";
    public override string ShowDismissed => "erledigte anzeigen";
    public override string LastChecked(string clockTime) => $"zuletzt geprüft {clockTime}";
    public override string CheckNow => "Jetzt prüfen";
    public override string BaselineTitle => "Grundlinie wird noch aufgebaut.";
    public override string BaselineExplained =>
        " Vier Detektoren vergleichen mit dem Normalzustand und bleiben so lange "
        + "still: neue Domain, Wiederholungssturm, Gleichlauf und Dauersender. "
        + "Ohne Vorgeschichte wäre jede Beobachtung „neu\" und damit jeder Fund "
        + "wertlos. NXDOMAIN-Flut, Tunneling-Verdacht und Fehlalarm-Verdacht "
        + "arbeiten dagegen sofort, ebenso die Beobachtung des Routers.";
    public override string NothingUnusual => "Nichts Auffälliges.";
    public override string Dismissed => "erledigt";
    public override string Client => "Client";
    public override string Domain => "Domain";
    public override string Numbers => "Zahlen";
    public override string WindowFromTo(string from, string until) => $"{from} bis {until}";
    public override string Suggestion => "Vorschlag";
    public override string Applied(string wann) => $"übernommen {wann}";
    public override string CreateException => "Ausnahme anlegen";
    public override string ExceptionCreated(string rule) =>
        $"Ausnahme {rule} angelegt, Regelsatz neu geladen.";
    public override string ExceptionWrittenOffline(string rule) =>
        $"Ausnahme {rule} geschrieben, aber der Resolver war nicht erreichbar — "
        + "sie greift beim nächsten Neuladen.";
    public override string ExceptionFailed(string? error) =>
        $"Ausnahme konnte nicht geschrieben werden: {error}";
    public override string SeverityHigh => "hoch";
    public override string SeverityWarn => "auffällig";
    public override string SeverityInfo => "Hinweis";

    public override string TitleImpact => "Wirkung";
    public override string ImpactHeading => "Auswertung · Regelwirkung";
    public override string ImpactIntro =>
        "Rechnet eine Regel gegen die gespeicherte Historie: was hätte sie "
        + "tatsächlich bewirkt? Besser, als sie scharf zu schalten und abzuwarten, "
        + "was kaputtgeht.";
    public override string LastDays(int days) => $"letzte {days} Tage";
    public override string Calculate => "Rechnen";
    public override string FormatsNote =>
        "Verstanden werden dieselben Formate wie in den Filterlisten: "
        + "||domain^, @@||domain^, *.domain, 0.0.0.0 host und die nackte Domain.";
    public override string NotADnsRule =>
        "Das ist keine Regel, die im DNS abbildbar wäre. Element- und "
        + "Cosmetic-Filter sowie Regeln mit Modifiern ($…) brauchen den "
        + "HTTP-Kontext, den ein Resolver nicht hat.";
    public override string AffectedQueries => "Betroffene Anfragen";
    public override string WouldChange => "Würde sich ändern";
    public override string Period => "Zeitraum";
    public override string FirstToLast => "erster bis letzter Treffer";
    public override string BlockedTodayAlready(string count) =>
        $"{count} heute schon geblockt";
    public override string ImpactSentence(bool isException, long wouldChange) =>
        isException
            ? $"{wouldChange:N0} bisher geblockte Anfragen würden durchgelassen"
            : $"{wouldChange:N0} bisher erlaubte Anfragen würden geblockt";
    public override string NoMatch =>
        "In diesem Zeitraum hat kein Gerät etwas gefragt, das auf die Regel passt. "
        + "Entweder ist sie überflüssig, oder die Historie reicht nicht weit genug "
        + "zurück.";
    public override string ColumnMatches => "Treffer";
    public override string ColumnAffected => "davon betroffen";
    public override string AffectedNames => "Betroffene Namen";

    public override string RuleKindLabel(RuleKind kind) => kind switch
    {
        RuleKind.Suffix => "Domain und Subdomains",
        RuleKind.SubOnly => "nur Subdomains",
        _ => "exakt",
    };

    public override string RuleImpact(bool isException) => isException ? "Ausnahme" : "Block";
}

public sealed partial class StringsEn
{
    public override string TitleFindings => "Anomalies";
    public override string FindingsIntro =>
        "Heuristics, not truth. Every finding names the numbers it rests on — "
        + "checking them is on you.";
    public override string ShowDismissed => "show handled";
    public override string LastChecked(string clockTime) => $"last checked {clockTime}";
    public override string CheckNow => "Check now";
    public override string BaselineTitle => "The baseline is still building.";
    public override string BaselineExplained =>
        " Four detectors compare against normal and stay quiet until then: new "
        + "domain, repeat storm, lockstep and steady talker. Without a past, "
        + "every observation would be \"new\" and every finding worthless. The "
        + "NXDOMAIN flood, tunnelling suspicion and false-positive suspicion "
        + "detectors work straight away, as does watching the router.";
    public override string NothingUnusual => "Nothing out of the ordinary.";
    public override string Dismissed => "handled";
    public override string Client => "Client";
    public override string Domain => "Domain";
    public override string Numbers => "Numbers";
    public override string WindowFromTo(string from, string until) => $"{from} to {until}";
    public override string Suggestion => "Suggestion";
    public override string Applied(string wann) => $"applied {wann}";
    public override string CreateException => "Add exception";
    public override string ExceptionCreated(string rule) =>
        $"Exception {rule} added, rule set reloaded.";
    public override string ExceptionWrittenOffline(string rule) =>
        $"Exception {rule} written, but the resolver was unreachable — it takes "
        + "effect on the next reload.";
    public override string ExceptionFailed(string? error) =>
        $"Could not write the exception: {error}";
    public override string SeverityHigh => "high";
    public override string SeverityWarn => "notable";
    public override string SeverityInfo => "Note";

    public override string TitleImpact => "Rule impact";
    public override string ImpactHeading => "Analysis · Rule impact";
    public override string ImpactIntro =>
        "Runs a rule against the stored history: what would it actually have done? "
        + "Better than switching it live and waiting to see what breaks.";
    public override string LastDays(int days) => $"last {days} days";
    public override string Calculate => "Run the numbers";
    public override string FormatsNote =>
        "The same formats as in the filter lists are understood: ||domain^, "
        + "@@||domain^, *.domain, 0.0.0.0 host and the bare domain.";
    public override string NotADnsRule =>
        "That is not a rule DNS could express. Element and cosmetic filters, and "
        + "rules carrying modifiers ($…), need the HTTP context a resolver does "
        + "not have.";
    public override string AffectedQueries => "Queries affected";
    public override string WouldChange => "Would change";
    public override string Period => "Period";
    public override string FirstToLast => "first to last hit";
    public override string BlockedTodayAlready(string count) =>
        $"{count} blocked already today";
    public override string ImpactSentence(bool isException, long wouldChange) =>
        isException
            ? $"{wouldChange:N0} queries blocked so far would be let through"
            : $"{wouldChange:N0} queries allowed so far would be blocked";
    public override string NoMatch =>
        "In this period no device asked for anything the rule matches. Either it "
        + "is redundant, or the history does not reach back far enough.";
    public override string ColumnMatches => "Hits";
    public override string ColumnAffected => "of those affected";
    public override string AffectedNames => "Names affected";

    public override string RuleKindLabel(RuleKind kind) => kind switch
    {
        RuleKind.Suffix => "domain and subdomains",
        RuleKind.SubOnly => "subdomains only",
        _ => "exact",
    };

    public override string RuleImpact(bool isException) => isException ? "Exception" : "Block";
}
