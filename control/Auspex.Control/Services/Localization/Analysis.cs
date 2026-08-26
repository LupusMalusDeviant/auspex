namespace Auspex.Control.Services.Localization;

// Auswertung · Verlauf.

public abstract partial class Strings
{
    public abstract string TitleAnalysis { get; }
    public abstract string AnalysisHeading { get; }
    public abstract string AnalysisIntroDaily { get; }
    public abstract string AnalysisIntroRaw { get; }

    public abstract string LastHours(int stunden);
    public abstract string LastDaysValues(int days);
    public abstract string LastYearValues { get; }
    public abstract string QueriesStored(string count);
    public abstract string Missed(string count);

    public abstract string NoDataYet { get; }
    public abstract string OldestEntry(string wann);
    public abstract string IngestCatchingUp { get; }

    public abstract string CardDnssec { get; }
    public abstract string DnssecNote(string validiert, string total);
    public abstract string CardDomains { get; }
    public abstract string RegistrableDomains { get; }
    public abstract string CardClients { get; }
    public abstract string Since(string wann);
    public abstract string AverageMs(string ms);

    public abstract string HistoryPer(bool perDay);
    public abstract string BarTitle(string wann, long total, long blocked, string quote);
    public abstract string PeakNote(string peak, bool perDay);

    public abstract string TopDomains { get; }
    public abstract string OnlyBlockedOnes { get; }
    public abstract string ColumnBlocked { get; }
    public abstract string ColumnShare { get; }
    public abstract string ColumnClients { get; }

    public abstract string Clients { get; }
    public abstract string ColumnProfile { get; }
    public abstract string ColumnBlockRate { get; }

    public abstract string WhereBlocksComeFrom { get; }
    public abstract string ColumnBlocks { get; }

    /// <summary>
    /// The example in the empty rule field. It carries the syntax, so the
    /// two example domains stay as they are — what changes with the language
    /// is only the word between them.
    /// </summary>
    public abstract string RuleExample { get; }
}

public sealed partial class StringsDe
{
    public override string TitleAnalysis => "Analyse";
    public override string AnalysisHeading => "Auswertung · Verlauf";
    public override string AnalysisIntroDaily =>
        "Verdichtete Tageswerte. Sie überleben das Löschen der Rohdaten — dafür "
        + "gibt es keine Stundenauflösung und keine Geräte je Domain.";
    public override string AnalysisIntroRaw =>
        "Ausgewertet wird der dauerhaft gespeicherte Query-Log, nicht der "
        + "Ringpuffer der Datenebene.";

    public override string LastHours(int stunden) => $"letzte {stunden} Stunden";
    public override string LastDaysValues(int days) => $"letzte {days} Tage (Tageswerte)";
    public override string LastYearValues => "letztes Jahr (Tageswerte)";
    public override string QueriesStored(string count) => $"{count} Anfragen gespeichert";
    public override string Missed(string count) => $" · {count} verpasst";

    public override string NoDataYet => "Noch keine Daten im gewählten Zeitraum.";
    public override string OldestEntry(string wann) => $"Ältester Eintrag: {wann}.";
    public override string IngestCatchingUp =>
        "Der Ingest holt alle paar Sekunden nach — läuft der Resolver?";

    public override string CardDnssec => "DNSSEC-validiert";
    public override string DnssecNote(string validiert, string total) =>
        $"{validiert} von {total} Upstream-Antworten";
    public override string CardDomains => "Domains";
    public override string RegistrableDomains => "registrierbare Domains";
    public override string CardClients => "Clients";
    public override string Since(string wann) => $"seit {wann}";
    public override string AverageMs(string ms) => $"Ø {ms} ms";

    public override string HistoryPer(bool perDay) => perDay ? "Verlauf je Tag" : "Verlauf je Stunde";
    public override string BarTitle(string wann, long total, long blocked, string quote) =>
        $"{wann} — {total:N0} Anfragen, {blocked:N0} geblockt ({quote})";
    public override string PeakNote(string peak, bool perDay) =>
        $"Spitze: {peak} Anfragen{(perDay ? "/Tag" : "/h")} · rot = geblockt";

    public override string TopDomains => "Top-Domains";
    public override string OnlyBlockedOnes => "nur geblockte";
    public override string ColumnBlocked => "Geblockt";
    public override string ColumnShare => "Anteil";
    public override string ColumnClients => "Clients";

    public override string Clients => "Clients";
    public override string ColumnProfile => "Profil";
    public override string ColumnBlockRate => "Blockrate";

    public override string WhereBlocksComeFrom => "Woher die Blocks kommen";
    public override string ColumnBlocks => "Blocks";
    public override string RuleExample => "||beispiel.de^ oder @@||shop.beispiel.de^";
}

public sealed partial class StringsEn
{
    public override string TitleAnalysis => "Analysis";
    public override string AnalysisHeading => "Analysis · History";
    public override string AnalysisIntroDaily =>
        "Rolled-up daily totals. They outlive the deletion of the raw data — in "
        + "exchange there is no hourly resolution and no devices per domain.";
    public override string AnalysisIntroRaw =>
        "What is evaluated is the permanently stored query log, not the data "
        + "plane's ring buffer.";

    public override string LastHours(int stunden) => $"last {stunden} hours";
    public override string LastDaysValues(int days) => $"last {days} days (daily totals)";
    public override string LastYearValues => "last year (daily totals)";
    public override string QueriesStored(string count) => $"{count} queries stored";
    public override string Missed(string count) => $" · {count} missed";

    public override string NoDataYet => "No data yet in the chosen period.";
    public override string OldestEntry(string wann) => $"Oldest entry: {wann}.";
    public override string IngestCatchingUp =>
        "Ingest catches up every few seconds — is the resolver running?";

    public override string CardDnssec => "DNSSEC validated";
    public override string DnssecNote(string validiert, string total) =>
        $"{validiert} of {total} upstream answers";
    public override string CardDomains => "Domains";
    public override string RegistrableDomains => "registrable domains";
    public override string CardClients => "Clients";
    public override string Since(string wann) => $"since {wann}";
    public override string AverageMs(string ms) => $"avg {ms} ms";

    public override string HistoryPer(bool perDay) => perDay ? "Per day" : "Per hour";
    public override string BarTitle(string wann, long total, long blocked, string quote) =>
        $"{wann} — {total:N0} queries, {blocked:N0} blocked ({quote})";
    public override string PeakNote(string peak, bool perDay) =>
        $"Peak: {peak} queries{(perDay ? "/day" : "/h")} · red = blocked";

    public override string TopDomains => "Top domains";
    public override string OnlyBlockedOnes => "blocked only";
    public override string ColumnBlocked => "Blocked";
    public override string ColumnShare => "Share";
    public override string ColumnClients => "Clients";

    public override string Clients => "Clients";
    public override string ColumnProfile => "Profile";
    public override string ColumnBlockRate => "Block rate";

    public override string WhereBlocksComeFrom => "Where the blocks come from";
    public override string ColumnBlocks => "Blocks";
    public override string RuleExample => "||example.com^ or @@||shop.example.com^";
}
