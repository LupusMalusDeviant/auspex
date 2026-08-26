namespace Auspex.Control.Services.Localization;

// The router catalogue: everything the firmware offers.

public abstract partial class Strings
{
    public abstract string TitleCatalogue { get; }
    public abstract string CatalogueIntro { get; }
    public abstract string SearchPrompt { get; }
    public abstract string OnlyChanging { get; }
    public abstract string NothingFound { get; }
    public abstract string ActionCount(int count);
    public abstract string ActionReads { get; }
    public abstract string ActionInvasive { get; }
    public abstract string ActionChanges { get; }
    public abstract string Close { get; }
    public abstract string Open { get; }
    public abstract string YesOne { get; }
    public abstract string NoZero { get; }
    public abstract string ActionLocked { get; }
    public abstract string ActionDangerous { get; }
    public abstract string RunAnyway { get; }
    public abstract string Run { get; }
    public abstract string Ran { get; }

    /// <summary>
    /// The display name of an area. Its key comes from
    /// <c>RouterSearch.Area</c> and stays the same in every language.
    /// </summary>
    public abstract string RouterArea(string key);
}

public sealed partial class StringsDe
{
    public override string TitleCatalogue => "Katalog";
    public override string CatalogueIntro =>
        "Alles, was der Router kann — auch das, wofür es keine eigene Seite gibt. "
        + "Suchen lässt sich auf Deutsch: „sperren\", „portfreigabe\", „gastnetz\".";
    public override string SearchPrompt => "Wonach suchst du?";
    public override string OnlyChanging => "nur verändernde";
    public override string NothingFound =>
        "Nichts gefunden. Die Aktionen heißen englisch — versuch es mit einem "
        + "Bereich aus der Liste, oder tippe einen Teil des englischen Namens.";
    public override string ActionCount(int count) =>
        count == 1 ? "1 Aktion" : $"{count} Aktionen";
    public override string ActionReads => "liest";
    public override string ActionInvasive => "greift tief ein";
    public override string ActionChanges => "verändert";
    public override string Close => "schließen";
    public override string Open => "öffnen";
    public override string YesOne => "ja (1)";
    public override string NoZero => "nein (0)";
    public override string ActionLocked =>
        "Nur-Lesen ist eingeschaltet — diese Aktion ist gesperrt.";
    public override string ActionDangerous =>
        "Diese Aktion kann den Zugang zum Router selbst kappen — etwa das WLAN "
        + "abschalten, in dem dein Rechner hängt. Dann kommst du an die Stelle, "
        + "an der du sie zurücknimmst, nicht mehr heran.";
    public override string RunAnyway => "Trotzdem ausführen";
    public override string Run => "Ausführen";
    public override string Ran => "Ausgeführt.";

    public override string RouterArea(string key) => key switch
    {
        "heimnetz" => "Heimnetz",
        "funknetz" => "Funknetz",
        "internet" => "Internet",
        "system" => "System",
        "smarthome" => "Smart Home",
        "telefonie" => "Telefonie",
        "speicher" => "Speicher",
        _ => "Weiteres",
    };
}

public sealed partial class StringsEn
{
    public override string TitleCatalogue => "Catalogue";
    public override string CatalogueIntro =>
        "Everything the router can do — including what has no page of its own. "
        + "Search in plain words: \"block\", \"port forward\", \"guest\".";
    public override string SearchPrompt => "What are you after?";
    public override string OnlyChanging => "changing only";
    public override string NothingFound =>
        "Nothing found. The actions carry their raw firmware names — try an area "
        + "from the list, or type part of the name itself.";
    public override string ActionCount(int count) =>
        count == 1 ? "1 action" : $"{count} actions";
    public override string ActionReads => "reads";
    public override string ActionInvasive => "cuts deep";
    public override string ActionChanges => "changes";
    public override string Close => "close";
    public override string Open => "open";
    public override string YesOne => "yes (1)";
    public override string NoZero => "no (0)";
    public override string ActionLocked =>
        "Read-only is on — this action is locked.";
    public override string ActionDangerous =>
        "This action can cut off access to the router itself — switching off the "
        + "very Wi-Fi your machine is on, for instance. The place where you would "
        + "undo it goes out of reach with it.";
    public override string RunAnyway => "Run it anyway";
    public override string Run => "Run";
    public override string Ran => "Done.";

    public override string RouterArea(string key) => key switch
    {
        "heimnetz" => "Home network",
        "funknetz" => "Wireless",
        "internet" => "Internet",
        "system" => "System",
        "smarthome" => "Smart home",
        "telefonie" => "Telephony",
        "speicher" => "Storage",
        _ => "Other",
    };
}
