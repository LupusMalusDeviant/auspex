namespace Auspex.Control.Services.Localization;

// Filterlisten.

public abstract partial class Strings
{
    public abstract string TitleLists { get; }
    public abstract string ListsIntro { get; }
    public abstract string ActiveLists { get; }
    public abstract string NoManagedList { get; }

    public abstract string ColumnKind { get; }
    public abstract string KindExceptions { get; }
    public abstract string KindBlocks { get; }
    public abstract string ListActive { get; }
    public abstract string ListOff { get; }
    public abstract string TurnOffShort { get; }
    public abstract string TurnOnShort { get; }

    public abstract string ProvenLists { get; }
    public abstract string ColumnWhatFor { get; }
    public abstract string AlreadyAdded { get; }

    /// <summary>
    /// What a proven list is good for.
    ///
    /// <para>
    /// The resolver supplies this text — in German, and rightly so: it
    /// answers an API request and does not know the reader's language. Here
    /// it gets replaced, looked up by the list's name. A list the resolver
    /// knows and this table does not keeps the supplied text — then German
    /// prose stands in an English table, which is still better than an empty
    /// cell.
    /// </para>
    /// </summary>
    public abstract string ListDescription(string name, string mitgeliefert);
    public abstract string Add { get; }

    public abstract string OwnList { get; }
    public abstract string ExceptionList { get; }
    public abstract string OnlyHttpAddresses { get; }

    public abstract string ListLoading(string name);
    public abstract string ListAdded(string name);
    public abstract string ListNotAdded(string name);
    public abstract string ListToggled(string name, bool nowOn);
    public abstract string ChangeFailed { get; }
    public abstract string ListRemoved(string name);
}

public sealed partial class StringsDe
{
    public override string TitleLists => "Filterlisten";
    public override string ListsIntro =>
        "Eine große Liste reicht in der Regel. Mehrere zu stapeln bringt kaum mehr "
        + "Blocking, aber deutlich mehr Fehlalarme.";
    public override string ActiveLists => "Aktive Listen";
    public override string NoManagedList =>
        "Noch keine Liste über die Oberfläche verwaltet. Listen aus der "
        + "Konfigurationsdatei erscheinen hier nicht — die gehören dir und werden "
        + "nicht angetastet.";

    public override string ColumnKind => "Art";
    public override string KindExceptions => "Ausnahmen";
    public override string KindBlocks => "Blocks";
    public override string ListActive => "aktiv";
    public override string ListOff => "aus";
    public override string TurnOffShort => "abschalten";
    public override string TurnOnShort => "einschalten";

    public override string ProvenLists => "Bewährte Listen";
    public override string ColumnWhatFor => "Wofür";
    public override string AlreadyAdded => "schon dabei";

    // German is the original: the same thing stands here as in the resolver.
    public override string ListDescription(string name, string mitgeliefert) => mitgeliefert;
    public override string Add => "hinzufügen";

    public override string OwnList => "Eigene Liste";
    public override string ExceptionList => "Ausnahmeliste";
    public override string OnlyHttpAddresses =>
        "Nur http(s)-Adressen. Lokale Dateien gehören in die Konfiguration — die "
        + "Oberfläche schreibt nicht ins Dateisystem des Resolvers.";

    public override string ListLoading(string name) => $"{name} wird geladen …";
    public override string ListAdded(string name) => $"{name} hinzugefügt und aktiv.";
    public override string ListNotAdded(string name) =>
        $"{name} konnte nicht hinzugefügt werden — Adresse erreichbar?";
    public override string ListToggled(string name, bool nowOn) =>
        $"{name} {(nowOn ? "eingeschaltet" : "abgeschaltet")}.";
    public override string ChangeFailed => "Änderung fehlgeschlagen.";
    public override string ListRemoved(string name) => $"{name} entfernt.";
}

public sealed partial class StringsEn
{
    public override string TitleLists => "Filter lists";
    public override string ListsIntro =>
        "One large list is usually enough. Stacking several adds barely any "
        + "blocking, and a great deal more false positives.";
    public override string ActiveLists => "Active lists";
    public override string NoManagedList =>
        "No list managed through the interface yet. Lists from the configuration "
        + "file do not appear here — those are yours and stay untouched.";

    public override string ColumnKind => "Kind";
    public override string KindExceptions => "Exceptions";
    public override string KindBlocks => "Blocks";
    public override string ListActive => "active";
    public override string ListOff => "off";
    public override string TurnOffShort => "switch off";
    public override string TurnOnShort => "switch on";

    public override string ProvenLists => "Lists worth having";
    public override string ColumnWhatFor => "What for";
    public override string AlreadyAdded => "already in";

    public override string ListDescription(string name, string mitgeliefert) => name switch
    {
        "hagezi-multi-pro" =>
            "Ads and tracking, balanced. A good default for everyday use.",
        "hagezi-multi-pro-plus" =>
            "Considerably stricter than Pro. Expect the occasional false positive.",
        "oisd-big" =>
            "A large general-purpose list, curated for few false positives.",
        "hagezi-threat-intelligence" =>
            "Malware, phishing, fraud. Complements an ad list, does not replace it.",
        "stevenblack-hosts" =>
            "The classic hosts file: conservative and widely used.",
        "hagezi-fake" =>
            "Fake shops and scam sites.",
        _ => mitgeliefert,
    };
    public override string Add => "add";

    public override string OwnList => "Your own list";
    public override string ExceptionList => "Exception list";
    public override string OnlyHttpAddresses =>
        "http(s) addresses only. Local files belong in the configuration — the "
        + "interface does not write to the resolver's file system.";

    public override string ListLoading(string name) => $"Loading {name} …";
    public override string ListAdded(string name) => $"{name} added and active.";
    public override string ListNotAdded(string name) =>
        $"{name} could not be added — is the address reachable?";
    public override string ListToggled(string name, bool nowOn) =>
        $"{name} switched {(nowOn ? "on" : "off")}.";
    public override string ChangeFailed => "The change did not go through.";
    public override string ListRemoved(string name) => $"{name} removed.";
}
