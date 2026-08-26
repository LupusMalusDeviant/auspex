namespace Auspex.Control.Services.Localization;

// The device dossier: where does this device send things?

public abstract partial class Strings
{
    public abstract string NavDossier { get; }
    public abstract string TitleDossier { get; }
    public abstract string DossierIntro { get; }
    public abstract string DossierLimit { get; }

    public abstract string ChooseDevice { get; }
    public abstract string NoTraffic { get; }

    // ── Die drei Zahlen, um die es geht ───────────────────────────────────
    public abstract string CardTotal { get; }
    public abstract string CardNeverLeft { get; }
    public abstract string CardNeverLeftNote { get; }
    public abstract string CardKnownTarget { get; }
    public abstract string CardKnownTargetNote(int carrier);
    public abstract string CardStayedHome { get; }
    public abstract string CardStayedHomeNote { get; }
    public abstract string OwnNetwork { get; }
    public abstract string OwnNetworkTitle { get; }
    public abstract string CardUnknownTarget { get; }
    public abstract string CardUnknownTargetNote { get; }
    public abstract string WatchedSince(string wann);

    // ── Die Betreiberliste ────────────────────────────────────────────────
    public abstract string Recipients { get; }
    public abstract string UnknownOperator { get; }
    public abstract string QueryShare(string count, int prozent);
    public abstract string DomainCountText(int count);
    public abstract string CityUncertainTitle { get; }
    public abstract string CityUncertainMark { get; }
    public abstract string ColumnFirstSeen { get; }
    public abstract string NothingAllowed { get; }
    public abstract string OriginNotYet { get; }
    public abstract string ViewDossier { get; }

    // ── What the sensor contributes ───────────────────────────────────────
    public abstract string Programs { get; }
    public abstract string ConnectionCount(long count);
    public abstract string DataVolume(long bytes);
    public abstract string BytesLowerBound { get; }
    public abstract string NoSensor { get; }
    public abstract string NoSensorExplained { get; }
    public abstract string SensorGap { get; }
    public abstract string ProgramsWithoutTarget { get; }

    /// <summary>Required attribution for the city database, CC BY 4.0.</summary>
    public abstract string GeoSourcesNote { get; }
}

public sealed partial class StringsDe
{
    public override string NavDossier => "Wohin?";
    public override string TitleDossier => "Wohin funkt dieses Gerät?";
    public override string DossierIntro =>
        "Jede Zeile ist ein Empfänger, mit dem dieses Gerät gesprochen hat — "
        + "gebündelt nach dem Betreiber des Netzes, nicht nach dem Namen. Neun "
        + "verschiedene Namen bei einer Firma sind eine Beziehung, keine neun.";
    public override string DossierLimit =>
        "Was hier steht, sind Namensauflösungen und die Adressen dahinter. "
        + "Auspex sieht nicht, ob danach wirklich eine Verbindung zustande kam, "
        + "wie viel geflossen ist oder was darin stand — das liegt im "
        + "verschlüsselten Teil, an den ein DNS-Filter nicht herankommt und "
        + "nicht herankommen soll.";

    public override string ChooseDevice => "Gerät";
    public override string NoTraffic => "Für dieses Gerät liegt im Zeitraum nichts vor.";

    public override string CardTotal => "Anfragen";
    public override string CardNeverLeft => "Nie hinausgegangen";
    public override string CardNeverLeftNote =>
        "geblockt — keine Adresse, keine Verbindung";
    public override string CardKnownTarget => "Ziel bekannt";
    public override string CardKnownTargetNote(int carrier) =>
        carrier == 1 ? "1 Betreiber" : $"{carrier} Betreiber";
    public override string CardStayedHome => "Im Haus geblieben";
    public override string CardStayedHomeNote => "Ziel liegt im eigenen Netz";
    public override string OwnNetwork => "Eigenes Netz";
    public override string OwnNetworkTitle =>
        "Router, NAS, Drucker — die Anfrage wurde beantwortet, aber die Adresse "
        + "dahinter liegt im Haus. Diese Verbindungen verlassen dein Netz nicht.";
    public override string CardUnknownTarget => "Ziel offen";
    public override string CardUnknownTargetNote =>
        "durchgelassen, Adresse nicht mitgeschrieben";
    public override string WatchedSince(string wann) => $"beobachtet seit {wann}";

    public override string Recipients => "Empfänger";
    public override string UnknownOperator => "(Betreiber unbekannt)";
    public override string QueryShare(string count, int prozent) =>
        $"{count} Anfragen · {prozent} %";
    public override string DomainCountText(int count) =>
        count == 1 ? "1 Domain" : $"{count} Domains";
    public override string CityUncertainTitle =>
        "Großes Verteilnetz: dieselbe Adresse antwortet an vielen Orten. Die "
        + "Stadt nennt einen Knoten, nicht den Sitz des Betreibers.";
    public override string CityUncertainMark => "ungefähr";
    public override string ColumnFirstSeen => "Erstkontakt";
    public override string NothingAllowed =>
        "Nichts davon ist durchgekommen — alle Anfragen dieses Geräts wurden "
        + "geblockt.";
    public override string OriginNotYet =>
        "Betreiber und Ort werden im Hintergrund nachgetragen. Bei einer frisch "
        + "gesehenen Adresse dauert das einen Moment, bei der Stadt bis zum "
        + "nächsten Sammeldurchlauf.";
    public override string ViewDossier => "wohin?";

    public override string Programs => "Programme";
    public override string ConnectionCount(long count) =>
        count == 1 ? "1 Verbindung" : $"{count:N0} Verbindungen";
    public override string DataVolume(long bytes) => Size(bytes, "B", "kB", "MB", "GB");
    public override string BytesLowerBound =>
        "Mindestens so viel: gezählt wird erst ab dem Moment, in dem der "
        + "Sensor die Verbindung sieht.";
    public override string NoSensor => "Auf diesem Gerät läuft kein Sensor.";
    public override string NoSensorExplained =>
        "Welches Programm eine Anfrage gestellt hat, kann ein DNS-Filter nicht "
        + "wissen — zwischen ihm und dem Programm liegt das Betriebssystem. "
        + "Dafür gibt es den Sensor: ein kleines Programm auf dem Rechner, das "
        + "die Verbindungstabelle liest und meldet, wer mit wem spricht. Es "
        + "liegt im Repo unter sensor/.";
    public override string SensorGap =>
        "Der Sensor sieht nur TCP. Windows führt für UDP keine Gegenstelle, "
        + "und damit fehlt hier, was über QUIC läuft — bei Chrome, Edge und "
        + "den Google-Diensten ein erheblicher Teil.";
    public override string ProgramsWithoutTarget => "Programme mit unbekanntem Ziel";

    /// <summary>
    /// A data volume in readable units. Steps of a thousand, not 1024: this
    /// is about bytes transferred, not storage space.
    /// </summary>
    private static string Size(long bytes, params string[] units)
    {
        double value = bytes;
        var i = 0;
        while (value >= 1000 && i < units.Length - 1)
        {
            value /= 1000;
            i++;
        }
        return i == 0 ? $"{bytes} {units[0]}" : $"{value:N1} {units[i]}";
    }

    public override string GeoSourcesNote =>
        "Herkunft der Adressen: Betreiber und Land aus den Ankündigungen im "
        + "Routing (iptoasn.com), Stadt aus DB-IP Lite (CC BY 4.0). Beides "
        + "liegt als Datei auf dieser Anlage — es wird keine Adresse bei einem "
        + "fremden Dienst nachgeschlagen.";
}

public sealed partial class StringsEn
{
    public override string NavDossier => "Where to?";
    public override string TitleDossier => "Where does this device transmit?";
    public override string DossierIntro =>
        "Every row is a recipient this device has spoken to — bundled by who "
        + "runs the network, not by name. Nine different names at one company "
        + "are one relationship, not nine.";
    public override string DossierLimit =>
        "What you see here are name lookups and the addresses behind them. "
        + "Auspex cannot see whether a connection actually followed, how much "
        + "flowed, or what was in it — that sits inside the encrypted part, "
        + "which a DNS filter cannot reach and should not.";

    public override string ChooseDevice => "Device";
    public override string NoTraffic => "Nothing recorded for this device in the period.";

    public override string CardTotal => "Queries";
    public override string CardNeverLeft => "Never left";
    public override string CardNeverLeftNote =>
        "blocked — no address, no connection";
    public override string CardKnownTarget => "Destination known";
    public override string CardKnownTargetNote(int carrier) =>
        carrier == 1 ? "1 operator" : $"{carrier} operators";
    public override string CardStayedHome => "Stayed indoors";
    public override string CardStayedHomeNote => "destination is on your own network";
    public override string OwnNetwork => "Your own network";
    public override string OwnNetworkTitle =>
        "Router, NAS, printer — the query was answered, but the address behind it "
        + "is in the house. These connections never leave your network.";
    public override string CardUnknownTarget => "Destination open";
    public override string CardUnknownTargetNote =>
        "allowed, address not recorded";
    public override string WatchedSince(string wann) => $"observed since {wann}";

    public override string Recipients => "Recipient";
    public override string UnknownOperator => "(operator unknown)";
    public override string QueryShare(string count, int prozent) =>
        $"{count} queries · {prozent}%";
    public override string DomainCountText(int count) =>
        count == 1 ? "1 domain" : $"{count} domains";
    public override string CityUncertainTitle =>
        "Large distribution network: the same address answers in many places. "
        + "The city names one node, not where the operator sits.";
    public override string CityUncertainMark => "approx.";
    public override string ColumnFirstSeen => "First contact";
    public override string NothingAllowed =>
        "None of it got through — every query from this device was blocked.";
    public override string OriginNotYet =>
        "Operator and location are filled in behind the scenes. For a freshly "
        + "seen address that takes a moment; for the city, until the next "
        + "batch pass.";
    public override string ViewDossier => "where to?";

    public override string Programs => "Programs";
    public override string ConnectionCount(long count) =>
        count == 1 ? "1 connection" : $"{count:N0} connections";
    public override string DataVolume(long bytes) => Size(bytes, "B", "kB", "MB", "GB");
    public override string BytesLowerBound =>
        "At least this much: counting starts the moment the sensor first sees "
        + "the connection.";
    public override string NoSensor => "No sensor is running on this device.";
    public override string NoSensorExplained =>
        "Which program made a query is something a DNS filter cannot know — the "
        + "operating system sits between the two. That is what the sensor is "
        + "for: a small program on the machine that reads the connection table "
        + "and reports who talks to whom. It lives in the repository under "
        + "sensor/.";
    public override string SensorGap =>
        "The sensor sees TCP only. Windows keeps no remote address for UDP, so "
        + "whatever runs over QUIC is missing here — with Chrome, Edge and the "
        + "Google services that is a substantial share.";
    public override string ProgramsWithoutTarget => "Programs with unknown destination";

    /// <summary>
    /// A data volume in readable units. Steps of 1000, not 1024: this is
    /// bytes transferred, not storage.
    /// </summary>
    private static string Size(long bytes, params string[] units)
    {
        double value = bytes;
        var i = 0;
        while (value >= 1000 && i < units.Length - 1)
        {
            value /= 1000;
            i++;
        }
        return i == 0 ? $"{bytes} {units[0]}" : $"{value:N1} {units[i]}";
    }

    public override string GeoSourcesNote =>
        "Address origins: operator and country from routing announcements "
        + "(iptoasn.com), city from DB-IP Lite (CC BY 4.0). Both sit as files "
        + "on this machine — no address is ever looked up at an outside "
        + "service.";
}
