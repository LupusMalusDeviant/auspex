namespace Auspex.Control.Services.Localization;

// Which program on a device talks to which domain. The join between the
// resolver's name-to-address record and the sensor's connection table.

public abstract partial class Strings
{
    public abstract string NavPrograms { get; }
    public abstract string TitlePrograms { get; }
    public abstract string ProgramsIntro { get; }
    public abstract string ProgramsLimit { get; }
    public abstract string NoSensorData { get; }
    public abstract string ColumnProgram { get; }
    public abstract string ColumnDomains { get; }
    public abstract string ColumnConnections { get; }
    public abstract string ProgramDomainCount(int count);
    public abstract string ProgramAddressCount(int count);
    public abstract string ProgramUnexplained(int count);
    public abstract string ProgramUnexplainedTitle { get; }
    public abstract string ProgramNoDomains { get; }
    public abstract string ShowAll { get; }

    // ── Quarantine ────────────────────────────────────────────────────────
    public abstract string Quarantine { get; }
    public abstract string QuarantineNote { get; }
    public abstract string QuarantineActive(string profile, string until);
    public abstract string QuarantineLift { get; }
    public abstract string QuarantineStarted(string profile);
    public abstract string QuarantineLifted(string profile);
    public abstract string QuarantineFailed(string error);
}

public sealed partial class StringsDe
{
    public override string NavPrograms => "Programme";
    public override string TitlePrograms => "Welches Programm spricht mit wem";
    public override string ProgramsIntro =>
        "Auspex weiß, welcher Name zu welcher Adresse gehört. Der Sensor weiß, "
        + "welches Programm zu welcher Adresse verbunden hat. Zusammengelegt "
        + "ergibt das eine Aussage, die keine der beiden Hälften allein trifft.";
    public override string ProgramsLimit =>
        "Der Sensor läuft nur auf Windows und liest die TCP-Tabelle. Telefone "
        + "fehlen hier, und was über QUIC läuft, ist unsichtbar. Eine Adresse "
        + "kann mehrere Namen tragen — die Verbindungstabelle hält fest, wohin "
        + "das Programm ging, nicht wonach es gefragt hat.";
    public override string NoSensorData =>
        "Für dieses Gerät liegen keine Sensordaten im Zeitraum.";
    public override string ColumnProgram => "Programm";
    public override string ColumnDomains => "Domains";
    public override string ColumnConnections => "Verbindungen";
    public override string ProgramDomainCount(int count) =>
        count == 1 ? "1 Domain" : $"{count} Domains";
    public override string ProgramAddressCount(int count) =>
        count == 1 ? "1 Adresse" : $"{count} Adressen";
    public override string ProgramUnexplained(int count) =>
        count == 1
            ? "1 Adresse ohne Auflösung"
            : $"{count} Adressen ohne Auflösung";
    public override string ProgramUnexplainedTitle =>
        "Zu diesen Adressen gibt es keine Auflösung — das Programm hat den "
        + "Filter also nicht gefragt. Übliche Ursachen: eigenes "
        + "DNS-over-HTTPS, fest eingebrannte Adressen, eigener Resolver.";
    public override string ProgramNoDomains =>
        "Keine der Adressen ließ sich einer Auflösung zuordnen.";
    public override string ShowAll => "Alle zeigen";

    public override string Quarantine => "Gerät in Quarantäne";
    public override string QuarantineNote =>
        "Setzt das Profil für eine Stunde auf \"quarantine\": das Gerät bekommt "
        + "keine Auflösungen mehr, ausgenommen ausdrückliche Allow-Regeln. "
        + "Auspex hebt die Sperre danach von selbst wieder auf. Der Router "
        + "bleibt unangetastet — das ist ein eigener, bewusster Schritt.";
    public override string QuarantineActive(string profile, string until) =>
        $"{profile} ist in Quarantäne bis {until}.";
    public override string QuarantineLift => "Jetzt aufheben";
    public override string QuarantineStarted(string profile) =>
        $"{profile} ist jetzt in Quarantäne. Läuft nach einer Stunde von selbst ab.";
    public override string QuarantineLifted(string profile) =>
        $"Quarantäne für {profile} aufgehoben, vorheriger Modus ist zurück.";
    public override string QuarantineFailed(string error) =>
        $"Quarantäne fehlgeschlagen: {error}";
}

public sealed partial class StringsEn
{
    public override string NavPrograms => "Programs";
    public override string TitlePrograms => "Which program talks to whom";
    public override string ProgramsIntro =>
        "Auspex knows which name belongs to which address. The sensor knows "
        + "which program connected to which address. Put together they make a "
        + "statement neither half makes on its own.";
    public override string ProgramsLimit =>
        "The sensor runs on Windows only and reads the TCP table. Phones are "
        + "absent here, and whatever goes over QUIC is invisible. One address "
        + "can carry several names — the connection table records where the "
        + "program went, not what it asked for.";
    public override string NoSensorData =>
        "No sensor data for this device in the period.";
    public override string ColumnProgram => "Program";
    public override string ColumnDomains => "Domains";
    public override string ColumnConnections => "Connections";
    public override string ProgramDomainCount(int count) =>
        count == 1 ? "1 domain" : $"{count} domains";
    public override string ProgramAddressCount(int count) =>
        count == 1 ? "1 address" : $"{count} addresses";
    public override string ProgramUnexplained(int count) =>
        count == 1
            ? "1 address with no lookup"
            : $"{count} addresses with no lookup";
    public override string ProgramUnexplainedTitle =>
        "No resolution accounts for these addresses — so the program did not "
        + "ask the filter. Usual causes: DNS-over-HTTPS of its own, hardcoded "
        + "addresses, its own resolver.";
    public override string ProgramNoDomains =>
        "None of the addresses could be matched to a lookup.";
    public override string ShowAll => "Show all";

    public override string Quarantine => "Quarantine device";
    public override string QuarantineNote =>
        "Sets the profile to \"quarantine\" for an hour: the device gets no more "
        + "lookups, except for explicit allow rules. Auspex lifts it again by "
        + "itself afterwards. The router is left alone — that is a separate, "
        + "deliberate step.";
    public override string QuarantineActive(string profile, string until) =>
        $"{profile} is quarantined until {until}.";
    public override string QuarantineLift => "Lift now";
    public override string QuarantineStarted(string profile) =>
        $"{profile} is now quarantined. It expires by itself after an hour.";
    public override string QuarantineLifted(string profile) =>
        $"Quarantine for {profile} lifted, the previous mode is back.";
    public override string QuarantineFailed(string error) =>
        $"Quarantine failed: {error}";
}
