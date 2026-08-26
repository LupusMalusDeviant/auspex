namespace Auspex.Control.Services.Localization;

// Portfreigaben, Ereignisprotokoll, IPv4, Katalog.

public abstract partial class Strings
{
    // ── Portfreigaben ─────────────────────────────────────────────────────
    public abstract string TitlePortMappings { get; }
    public abstract string PortMappingsIntro { get; }
    public abstract string PortMappingCount(int count);
    /// <summary>
    /// The distinction the page once lied about: "no mappings" and "did not
    /// look" are not the same thing.
    /// </summary>
    public abstract string PortMappingsUnknown { get; }
    public abstract string NoPortMappings { get; }
    public abstract string ColumnLabel { get; }
    public abstract string ColumnFromOutside { get; }
    public abstract string ColumnToInside { get; }
    public abstract string Unlabelled { get; }
    public abstract string OnlyFrom(string remote);
    public abstract string MappingActive { get; }
    public abstract string MappingInactive { get; }
    public abstract string Really { get; }
    public abstract string No { get; }
    public abstract string Remove { get; }
    public abstract string MappingRemoved(string port);
    public abstract string NoNewMappingsBefore { get; }
    public abstract string NoNewMappingsLink { get; }
    public abstract string NoNewMappingsAfter { get; }

    // ── Ereignisprotokoll ─────────────────────────────────────────────────
    public abstract string TitleEventLog { get; }
    public abstract string EventLogIntro { get; }
    public abstract string SearchEventLog { get; }
    public abstract string AllAreas { get; }
    public abstract string MatchesOf(int hits, int total);
    public abstract string NoEventLog { get; }
    public abstract string NoEntryMatches { get; }
    public abstract string ColumnWhen { get; }
    public abstract string ColumnArea { get; }
    public abstract string ColumnMessage { get; }
    public abstract string ShownOf(int gezeigt, int total);
    public abstract string AnotherHundred { get; }
    public abstract string EventLogNotReadable(string reason);
    public abstract string EventArea(string kategorie);
}

public sealed partial class StringsDe
{
    public override string TitlePortMappings => "Portfreigaben";
    public override string PortMappingsIntro =>
        "Jede Zeile hier ist eine Tür von außen nach innen. Eine Freigabe, an die "
        + "sich niemand mehr erinnert, ist der häufigste Weg, auf dem ein Heimnetz "
        + "offener ist als gedacht.";
    public override string PortMappingCount(int count) =>
        count == 1 ? "1 Freigabe" : $"{count} Freigaben";
    public override string PortMappingsUnknown =>
        "Ob Freigaben bestehen, ist damit unbekannt — nicht beantwortet mit „keine\".";
    public override string NoPortMappings =>
        "Keine Portfreigabe eingerichtet — von außen führt keine Tür herein.";
    public override string ColumnLabel => "Bezeichnung";
    public override string ColumnFromOutside => "von außen";
    public override string ColumnToInside => "nach innen";
    public override string Unlabelled => "(ohne Bezeichnung)";
    public override string OnlyFrom(string remote) => $"nur von {remote}";
    public override string MappingActive => "aktiv";
    public override string MappingInactive => "inaktiv";
    public override string Really => "Wirklich";
    public override string No => "Nein";
    public override string Remove => "Entfernen";
    public override string MappingRemoved(string port) =>
        $"Freigabe auf Port {port} entfernt.";
    public override string NoNewMappingsBefore =>
        "Neue Freigaben legt Auspex bewusst nicht an. Eine Tür von außen zu öffnen "
        + "ist die eine Änderung am Router, die im Zweifel wirklich weh tut — dafür "
        + "lohnt der Umweg über das Menü der Fritz!Box, wo daneben steht, was sie "
        + "bedeutet. Über den ";
    public override string NoNewMappingsLink => "Katalog";
    public override string NoNewMappingsAfter =>
        " geht es trotzdem, wenn du es genau so willst.";

    public override string TitleEventLog => "Ereignisse";
    public override string EventLogIntro =>
        "Das Protokoll des Routers, aufgetrennt und durchsuchbar. TR-064 liefert es "
        + "als eine einzige Zeichenkette ohne Zeilenumbrüche — als Textwand ist es "
        + "da, aber unbrauchbar.";
    public override string SearchEventLog => "Im Protokoll suchen";
    public override string AllAreas => "alle Bereiche";
    public override string MatchesOf(int hits, int total) =>
        $"{hits:N0} von {total:N0}";
    public override string NoEventLog => "Kein Protokoll gelesen.";
    public override string NoEntryMatches => "Kein Eintrag passt.";
    public override string ColumnWhen => "Wann";
    public override string ColumnArea => "Bereich";
    public override string ColumnMessage => "Meldung";
    public override string ShownOf(int gezeigt, int total) =>
        $"{gezeigt:N0} von {total:N0} gezeigt";
    public override string AnotherHundred => "Weitere 100";
    public override string EventLogNotReadable(string reason) =>
        $"Protokoll nicht lesbar: {reason}";
    public override string EventArea(string kategorie) => kategorie switch
    {
        "fehler" => "Fehler",
        "wlan" => "WLAN",
        "internet" => "Internet",
        "anmeldung" => "Anmeldung",
        "telefonie" => "Telefonie",
        _ => "Sonstiges",
    };
}

public sealed partial class StringsEn
{
    public override string TitlePortMappings => "Port forwards";
    public override string PortMappingsIntro =>
        "Every line here is a door from the outside in. A forward nobody remembers "
        + "setting up is the most common way a home network ends up more open than "
        + "anyone thinks.";
    public override string PortMappingCount(int count) =>
        count == 1 ? "1 forward" : $"{count} forwards";
    public override string PortMappingsUnknown =>
        "Whether any forwards exist is therefore unknown — which is not the same "
        + "answer as \"none\".";
    public override string NoPortMappings =>
        "No port forward configured — no door leads in from outside.";
    public override string ColumnLabel => "Label";
    public override string ColumnFromOutside => "outside";
    public override string ColumnToInside => "inside";
    public override string Unlabelled => "(no label)";
    public override string OnlyFrom(string remote) => $"only from {remote}";
    public override string MappingActive => "active";
    public override string MappingInactive => "inactive";
    public override string Really => "Confirm";
    public override string No => "No";
    public override string Remove => "Remove";
    public override string MappingRemoved(string port) =>
        $"Forward on port {port} removed.";
    public override string NoNewMappingsBefore =>
        "Auspex deliberately does not create forwards. Opening a door from outside "
        + "is the one router change that can genuinely hurt — for that, the detour "
        + "through the Fritz!Box menu is worth it, where the meaning is spelled out "
        + "next to it. The ";
    public override string NoNewMappingsLink => "catalogue";
    public override string NoNewMappingsAfter =>
        " will still do it, if that is exactly what you want.";

    public override string TitleEventLog => "Events";
    public override string EventLogIntro =>
        "The router's own log, split apart and searchable. TR-064 hands it over as "
        + "one single string without line breaks — present as a wall of text, and "
        + "useless in that form.";
    public override string SearchEventLog => "Search the log";
    public override string AllAreas => "all areas";
    public override string MatchesOf(int hits, int total) =>
        $"{hits:N0} of {total:N0}";
    public override string NoEventLog => "No log read.";
    public override string NoEntryMatches => "No entry matches.";
    public override string ColumnWhen => "When";
    public override string ColumnArea => "Area";
    public override string ColumnMessage => "Message";
    public override string ShownOf(int gezeigt, int total) =>
        $"showing {gezeigt:N0} of {total:N0}";
    public override string AnotherHundred => "100 more";
    public override string EventLogNotReadable(string reason) =>
        $"Cannot read the log: {reason}";
    public override string EventArea(string kategorie) => kategorie switch
    {
        "fehler" => "Error",
        "wlan" => "Wi-Fi",
        "internet" => "Internet",
        "anmeldung" => "Sign-in",
        "telefonie" => "Telephony",
        _ => "Other",
    };
}
