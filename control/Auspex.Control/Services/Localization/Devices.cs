namespace Auspex.Control.Services.Localization;

// Device profiles.
//
// The mode names open/learn/enforce stay put in both languages: they appear
// exactly like that in the resolver's configuration file, and whoever reads
// them in the interface and then looks in the file should find the same
// word. Only what is explained about them gets translated.

public abstract partial class Strings
{
    public abstract string TitleDeviceProfiles { get; }
    public abstract string DevicesIntro { get; }
    public abstract string ManagedProfiles { get; }
    public abstract string NewProfile { get; }
    public abstract string NoProfileYet { get; }

    public abstract string ColumnName { get; }
    public abstract string ColumnAddresses { get; }
    public abstract string ColumnMode { get; }
    public abstract string ColumnBlockedServices { get; }
    public abstract string ColumnSchedule { get; }
    public abstract string Edit { get; }
    public abstract string ProfileTitle(string name);

    public abstract string AddressesOrNetworks { get; }
    public abstract string CommaSeparatedNote { get; }
    public abstract string ModeOpen { get; }
    public abstract string ModeLearn { get; }
    public abstract string ModeEnforce { get; }
    public abstract string BlockServices { get; }
    public abstract string SafeSearch { get; }
    public abstract string SafeSearchNote { get; }
    public abstract string Save { get; }

    public abstract string ProfileExists(string name);
    public abstract string ProfileByMac(string name, string mac);
    public abstract string ProfileByAddress(string address);
    public abstract string ProfileSaved(string name);
    public abstract string NotSaved(string error);
    public abstract string ProfileRemoved(string name);
    public abstract string RemoveFailed { get; }
}

public sealed partial class StringsDe
{
    public override string TitleDeviceProfiles => "Geräteprofile";
    public override string DevicesIntro =>
        "Profile aus der Konfigurationsdatei erscheinen hier nicht — die gehören "
        + "dir und werden nicht angetastet. Upstreams und Lauschadressen bleiben "
        + "ebenfalls in der Datei: an denen kann man sich aussperren.";
    public override string ManagedProfiles => "Verwaltete Profile";
    public override string NewProfile => "Neues Profil";
    public override string NoProfileYet =>
        "Noch kein Profil über die Oberfläche angelegt.";

    public override string ColumnName => "Name";
    public override string ColumnAddresses => "Adressen";
    public override string ColumnMode => "Modus";
    public override string ColumnBlockedServices => "Gesperrte Dienste";
    public override string ColumnSchedule => "Zeitfenster";
    public override string Edit => "bearbeiten";
    public override string ProfileTitle(string name) => $"Profil {name}";

    public override string AddressesOrNetworks => "Adressen / Netze";
    public override string CommaSeparatedNote => "Kommagetrennt. Einzeladressen oder CIDR.";
    public override string ModeOpen => "open — nur Blocklisten";
    public override string ModeLearn => "learn — beobachten";
    public override string ModeEnforce => "enforce — nur Gelerntes durchlassen";
    public override string BlockServices => "Dienste sperren";
    public override string SafeSearch => "Gefilterte Suche";
    public override string SafeSearchNote =>
        "Schickt die Suchmaschine auf den Rechner, von dem sie gefilterte "
        + "Ergebnisse ausliefert. Eine Bremsschwelle, kein Schloss: ein Browser "
        + "mit eigenem DNS geht daran vorbei.";
    public override string Save => "Speichern";

    public override string ProfileExists(string name) =>
        $"{name} hat bereits ein Profil — hier ist es.";
    public override string ProfileByMac(string name, string mac) =>
        $"{name} erkannt, Profil hängt an der MAC {mac} — das überlebt einen "
        + "Adresswechsel.";
    public override string ProfileByAddress(string address) =>
        $"Zu {address} ist keine MAC bekannt. Das Profil hängt vorerst an der "
        + "Adresse und greift nicht mehr, sobald sie wechselt.";
    public override string ProfileSaved(string name) =>
        $"Profil {name} gespeichert und sofort wirksam.";
    public override string NotSaved(string error) => $"Nicht gespeichert: {error}";
    public override string ProfileRemoved(string name) => $"Profil {name} entfernt.";
    public override string RemoveFailed => "Entfernen fehlgeschlagen.";
}

public sealed partial class StringsEn
{
    public override string TitleDeviceProfiles => "Device profiles";
    public override string DevicesIntro =>
        "Profiles from the configuration file do not appear here — those are yours "
        + "and stay untouched. Upstreams and listen addresses also stay in the "
        + "file: those are the ones you can lock yourself out with.";
    public override string ManagedProfiles => "Managed profiles";
    public override string NewProfile => "New profile";
    public override string NoProfileYet =>
        "No profile created through the interface yet.";

    public override string ColumnName => "Name";
    public override string ColumnAddresses => "Addresses";
    public override string ColumnMode => "Mode";
    public override string ColumnBlockedServices => "Blocked services";
    public override string ColumnSchedule => "Time windows";
    public override string Edit => "edit";
    public override string ProfileTitle(string name) => $"Profile {name}";

    public override string AddressesOrNetworks => "Addresses / networks";
    public override string CommaSeparatedNote => "Comma separated. Single addresses or CIDR.";
    // open/learn/enforce stay put - they are the values in the
    // configuration file, not captions.
    public override string ModeOpen => "open — block lists only";
    public override string ModeLearn => "learn — watch and record";
    public override string ModeEnforce => "enforce — let through only what was learned";
    public override string BlockServices => "Block services";
    public override string SafeSearch => "Filtered search";
    public override string SafeSearchNote =>
        "Sends the search engine to the host it serves filtered results from. "
        + "A speed bump, not a lock: a browser with a resolver of its own goes "
        + "round it.";
    public override string Save => "Save";

    public override string ProfileExists(string name) =>
        $"{name} already has a profile — here it is.";
    public override string ProfileByMac(string name, string mac) =>
        $"{name} identified; the profile is keyed on MAC {mac} — that survives an "
        + "address change.";
    public override string ProfileByAddress(string address) =>
        $"No MAC is known for {address}. For now the profile is keyed on the "
        + "address, and stops applying as soon as that changes.";
    public override string ProfileSaved(string name) =>
        $"Profile {name} saved and live straight away.";
    public override string NotSaved(string error) => $"Not saved: {error}";
    public override string ProfileRemoved(string name) => $"Profile {name} removed.";
    public override string RemoveFailed => "Could not remove it.";
}
