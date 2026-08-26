using Auspex.Control.Data;

namespace Auspex.Control.Services.Localization;

/// <summary>A finding, put into words.</summary>
public sealed record FindingText(string Titel, string Explanation, string Numbers);

// The sentences for the findings.
//
// Until now they stood in the detectors and were written at detection time.
// That stopped working as soon as there were two languages — detection runs
// with no reader. Now the detector supplies numbers, and here they become a
// sentence, in the language of whoever is looking.

public abstract partial class Strings
{
    /// <summary>
    /// The sentence for a finding.
    ///
    /// <para>
    /// If the finding carries no values, it dates from before the rework:
    /// then its stored text stands there, untranslated. That is more honest
    /// than showing an empty card — and it goes away by itself once the old
    /// findings have run through the retention period.
    /// </para>
    /// </summary>
    public FindingText Finding(Finding f)
    {
        var w = FindingValues.FromJson(f.Values);
        return w is null
            ? new FindingText(f.Title, f.Explanation, f.Evidence)
            : FindingFrom(f, w);
    }

    protected abstract FindingText FindingFrom(Finding f, FindingValues w);

    /// <summary>The name where known, otherwise the address.</summary>
    protected static string Wer(Finding f) =>
        string.IsNullOrEmpty(f.ClientName) ? f.Client : f.ClientName;

    /// <summary>A span roughly in words — minutes or seconds.</summary>
    public abstract string Span(TimeSpan span);
}

public sealed partial class StringsDe
{
    public override string Span(TimeSpan s) =>
        s.TotalMinutes >= 1 ? $"{s.TotalMinutes:F0} Minuten" : $"{s.TotalSeconds:F0} Sekunden";

    protected override FindingText FindingFrom(Finding f, FindingValues w) => f.Detector switch
    {
        "neue-domain" => new(
            $"Neue Domain: {f.Subject}",
            w.Nachts
                ? $"{Wer(f)} hat {f.Subject} zum ersten Mal gefragt — nachts. "
                  + "Bei Geräten, die nachts eigentlich still sind, lohnt ein Blick."
                : $"{Wer(f)} hat {f.Subject} zum ersten Mal gefragt. "
                  + "Meist harmlos (neue App, neues CDN).",
            $"{w.Count:N0} Anfragen im Fenster, in den letzten {w.BaselineDays:F0} Tagen nie gesehen"),

        "unerklaerte-verbindung" => new(
            $"{f.Subject} verbindet an Auspex vorbei",
            $"{Wer(f)}: das Programm {f.Subject} hat {w.Names} Adressen angesprochen, zu "
            + "denen es keine einzige Auflösung gibt — in der ganzen Datenbank nicht. "
            + "Der Filter wurde also nicht gefragt. Übliche Ursachen: ein Browser mit "
            + "eigenem DNS-over-HTTPS, fest eingebrannte Adressen im Programm, oder eine "
            + "App mit eigenem Resolver. Was es nicht heißt: der Sensor sieht nur Windows "
            + "und nur TCP — über QUIC und über Telefone sagt dieser Befund nichts.",
            $"{w.Names} Adressen ohne Auflösung, {w.Count:N0} Verbindungen, z. B. {w.Example}"),

        "rebind" => new(
            $"{f.Subject} zeigte ins Heimnetz",
            w.Names >= 3
                ? $"Ein Name aus dem Internet antwortete mit {w.Address} — einer Adresse "
                  + $"aus dem eigenen Netz. Bei {Wer(f)} ist das im selben Zeitfenster bei "
                  + $"{w.Names} verschiedenen Namen passiert. So häuft es sich nicht "
                  + "zufällig: das ist das Muster eines DNS-Rebinding-Angriffs, mit dem "
                  + "eine Webseite Geräte im Heimnetz erreichen will — Router, Drucker, "
                  + "Kamera, NAS."
                : $"Ein Name aus dem Internet antwortete mit {w.Address} — einer Adresse "
                  + "aus dem eigenen Netz. Die Antwort wurde verworfen. Einzeln ist das "
                  + "oft ein Dienst, der interne Adressen absichtlich veröffentlicht und "
                  + "den noch niemand freigegeben hat; gehäuft wäre es ein Angriff.",
            $"{w.Count:N0} Anfragen geblockt, Ziel {w.Address}"),

        "nxdomain-flut" => new(
            $"{w.Anteil:P0} der Anfragen von {Wer(f)} laufen ins Leere",
            "Ein hoher Anteil nicht existierender Namen deutet auf eine falsche "
            + "Suchdomain, eine kaputte App — oder auf einen Domain-Generator, wie ihn "
            + "Schadsoftware zum Auffinden ihres Servers nutzt.",
            $"{w.Nx:N0} von {w.Total:N0} Anfragen mit NXDOMAIN, {w.Domains:N0} verschiedene Domains"),

        "wiederholungssturm" => new(
            $"{Wer(f)} fragt {f.Subject} {w.Faktor:F0}x häufiger als sonst",
            $"{w.Count:N0} Anfragen in einer Stunde gegenüber sonst rund {w.PerHour:F1} pro Stunde. "
            + (w.Nachts ? "Und das nachts. " : "")
            + "Typisch für eine Schleife nach einem Fehler, eine hängende App — oder ein "
            + "Gerät, das ungewöhnlich viel meldet.",
            $"Fenster: {w.Count:N0} Anfragen, Grundlinie: {w.PerHour:F1}/h über {w.BaselineDays:F0} Tage"),

        "tunneling-verdacht" => new(
            $"Verdacht auf DNS-Tunneling über {f.Subject}",
            $"{w.Names:N0} verschiedene Namen unter einer Domain, längstes Label "
            + $"{w.MaxLabel} Zeichen. So sieht es aus, wenn Daten im DNS-Namen "
            + "transportiert werden. Fehlalarme kommen von manchen Antivirus- und "
            + "CDN-Diensten, die ebenfalls kodierte Namen nutzen.",
            $"{w.Names:N0} verschiedene Namen, {w.Total:N0} Anfragen, "
            + $"längstes Label {w.MaxLabel} Zeichen"),

        "fehlalarm-verdacht" => new(
            $"{Wer(f)} kommt an {f.Subject} nicht vorbei",
            $"{w.Count:N0} geblockte Anfragen in {Span(w.Span)} — das ist eine "
            + "Wiederholungsschleife, kein normaler Aufruf. Vermutlich funktioniert auf dem "
            + "Gerät gerade etwas nicht. Die vorgeschlagene Ausnahme behebt es; wenn du die "
            + "Domain bewusst blockst, kannst du den Fund einfach abhaken.",
            $"{w.Count:N0} Versuche in {Span(w.Span)} ({w.ProMinute:F0}/min), "
            + $"{w.Names:N0} verschiedene Namen, geblockt durch {w.Rule ?? "?"} aus {w.ListName ?? "?"}"),

        "gleichlauf" => new(
            $"{w.Devices} Geräte entdecken {f.Subject} gleichzeitig",
            $"{w.Devices} Geräte haben {f.Subject} innerhalb von {Span(w.Span)} zum ersten "
            + "Mal gefragt. Einzeln wäre das unauffällig; der Gleichlauf deutet auf eine "
            + "App-Aktualisierung, ein Firmware-Update mit neuem Ziel — oder auf etwas, das "
            + "sich gerade im Netz verbreitet.",
            $"{w.Devices} Geräte, erster Kontakt {DisplayTime.ToDisplay(w.First):HH:mm:ss}, "
            + $"letzter {DisplayTime.ToDisplay(w.Last):HH:mm:ss}, in der Vorgeschichte nie gesehen"),

        "dauersender" => new(
            $"{Wer(f)} fragt {f.Subject} rund um die Uhr",
            $"{w.Count:N0} geblockte Anfragen in der letzten Stunde, und das seit Tagen "
            + $"gleichmäßig — hochgerechnet rund {w.PerDay:N0} am Tag. Das ist kein Ausschlag, "
            + "sondern der Normalzustand dieses Geräts: eine App fragt in festem Takt und nimmt "
            + "die Absage nicht zur Kenntnis. Die Sperre wirkt; sie kostet nur dauerhaft "
            + "Anfragen. Wenn du wissen willst, welches Programm das ist, ist der Name der "
            + "beste Anhaltspunkt.",
            $"{w.Count:N0} Anfragen in {Span(w.Span)} ({w.ProMinute:F1}/min), "
            + $"Grundlinie {w.PerHour:F0}/h über {w.BaselineDays:F1} Tage, "
            + $"{w.Names} Name(n), z. B. {w.Example}"),

        // ── What the router reports ────────────────────────────────────────
        "portfreigabe" => w.ChangeKind switch
        {
            "neu" => new(
                $"Neue Portfreigabe: {w.Protocol} {w.Port} nach außen",
                "Eine Freigabe ist dazugekommen, ohne dass sie hier angelegt wurde. "
                + (w.ForAll
                    ? "Sie gilt für beliebige Gegenstellen — das gesamte Internet erreicht "
                      + "damit dieses Gerät. "
                    : "Sie gilt nur für eine bestimmte Gegenstelle. ")
                + "Typischer Weg ist UPnP: ein Programm im Netz bittet den Router selbst "
                + "darum. Wenn du weißt, welches, ist alles in Ordnung. Wenn nicht, gehört "
                + "sie weg.",
                w.Now ?? ""),

            "geaendert" => new(
                $"Portfreigabe {w.Protocol} {w.Port} zeigt woanders hin",
                "Dieselbe Freigabe führt jetzt zu einem anderen Ziel oder wurde ein- "
                + "beziehungsweise ausgeschaltet. Ein Wechsel des inneren Ziels heißt: der "
                + "Port von außen erreicht ab sofort ein anderes Gerät.",
                $"vorher: {w.Before} — jetzt: {w.Now}"),

            _ => new(
                $"Portfreigabe {w.Protocol} {w.Port} ist verschwunden",
                "Die Freigabe gibt es nicht mehr. Meist harmlos — UPnP-Freigaben laufen ab, "
                + "wenn das Programm sie nicht erneuert. Steht hier, damit die Gegenrichtung "
                + "nicht unbemerkt bleibt.",
                w.Now ?? ""),
        },

        "neues-geraet" => new(
            $"Neues Gerät im Netz: {Wer(f)}",
            $"Zum ersten Mal gesehen, angemeldet über {w.Connection}. "
            + (w.ZufallMac
                ? "Die MAC-Adresse ist zufällig vergeben — das machen Handys pro WLAN. "
                  + "Dasselbe Gerät kann nach dem Vergessen des Netzes erneut als neu "
                  + "auftauchen. "
                : "")
            + "Wenn du es nicht zuordnen kannst, lohnt ein Blick, wer sich da angemeldet hat.",
            $"MAC {f.Subject}, Adresse {w.Address}, "
            + (w.Online ? "verbunden" : "derzeit offline")),

        _ => new(f.Title, f.Explanation, f.Evidence),
    };
}

public sealed partial class StringsEn
{
    public override string Span(TimeSpan s) =>
        s.TotalMinutes >= 1 ? $"{s.TotalMinutes:F0} minutes" : $"{s.TotalSeconds:F0} seconds";

    protected override FindingText FindingFrom(Finding f, FindingValues w) => f.Detector switch
    {
        "neue-domain" => new(
            $"New domain: {f.Subject}",
            w.Nachts
                ? $"{Wer(f)} asked for {f.Subject} for the first time — at night. "
                  + "On devices that are normally quiet after dark, that is worth a look."
                : $"{Wer(f)} asked for {f.Subject} for the first time. "
                  + "Usually harmless (a new app, a new CDN).",
            $"{w.Count:N0} queries in the window, never seen in the last {w.BaselineDays:F0} days"),

        "unerklaerte-verbindung" => new(
            $"{f.Subject} connects around Auspex",
            $"{Wer(f)}: the program {f.Subject} reached {w.Names} addresses that no "
            + "resolution accounts for — not one, anywhere in the database. So the "
            + "filter was never asked. Usual causes: a browser with DNS-over-HTTPS of "
            + "its own, addresses hardcoded into the program, or an app carrying its own "
            + "resolver. What it does not mean: the sensor sees Windows only and TCP "
            + "only — about QUIC, and about phones, this finding says nothing.",
            $"{w.Names} addresses with no lookup, {w.Count:N0} connections, e.g. {w.Example}"),

        "rebind" => new(
            $"{f.Subject} pointed into the home network",
            w.Names >= 3
                ? $"A name from the internet answered with {w.Address} — an address "
                  + $"inside your own network. For {Wer(f)} that happened with {w.Names} "
                  + "different names in the same window. That does not pile up by "
                  + "accident: it is the shape of a DNS rebinding attack, where a web "
                  + "page tries to reach devices on the home network — router, printer, "
                  + "camera, NAS."
                : $"A name from the internet answered with {w.Address} — an address "
                  + "inside your own network. The answer was discarded. On its own this "
                  + "is often a service that publishes internal addresses on purpose and "
                  + "that nobody has allowed yet; repeated, it would be an attack.",
            $"{w.Count:N0} queries blocked, target {w.Address}"),

        "nxdomain-flut" => new(
            $"{w.Anteil:P0} of {Wer(f)}'s queries go nowhere",
            "A high share of names that do not exist points to a wrong search domain, a "
            + "broken app — or to a domain generator of the kind malware uses to find its "
            + "server.",
            $"{w.Nx:N0} of {w.Total:N0} queries returned NXDOMAIN, {w.Domains:N0} distinct domains"),

        "wiederholungssturm" => new(
            $"{Wer(f)} is asking for {f.Subject} {w.Faktor:F0}x more often than usual",
            $"{w.Count:N0} queries in one hour against a usual {w.PerHour:F1} per hour. "
            + (w.Nachts ? "And at night. " : "")
            + "Typical of a retry loop after an error, an app that is stuck — or a device "
            + "reporting unusually much.",
            $"Window: {w.Count:N0} queries, baseline: {w.PerHour:F1}/h over {w.BaselineDays:F0} days"),

        "tunneling-verdacht" => new(
            $"Possible DNS tunnelling through {f.Subject}",
            $"{w.Names:N0} distinct names under a single domain, longest label "
            + $"{w.MaxLabel} characters. That is what it looks like when data is carried "
            + "inside DNS names. False positives come from some antivirus and CDN services, "
            + "which also use encoded names.",
            $"{w.Names:N0} distinct names, {w.Total:N0} queries, "
            + $"longest label {w.MaxLabel} characters"),

        "fehlalarm-verdacht" => new(
            $"{Wer(f)} cannot get past {f.Subject}",
            $"{w.Count:N0} blocked queries in {Span(w.Span)} — that is a retry loop, not "
            + "a normal request. Something on the device is probably not working right now. "
            + "The suggested exception fixes it; if you block the domain on purpose, just "
            + "tick the finding off.",
            $"{w.Count:N0} attempts in {Span(w.Span)} ({w.ProMinute:F0}/min), "
            + $"{w.Names:N0} distinct names, blocked by {w.Rule ?? "?"} from {w.ListName ?? "?"}"),

        "gleichlauf" => new(
            $"{w.Devices} devices discover {f.Subject} at the same time",
            $"{w.Devices} devices asked for {f.Subject} for the first time within "
            + $"{Span(w.Span)} of each other. Individually that would be unremarkable; the "
            + "lockstep points to an app update, a firmware update with a new target — or to "
            + "something spreading through the network right now.",
            $"{w.Devices} devices, first contact {DisplayTime.ToDisplay(w.First):HH:mm:ss}, "
            + $"last {DisplayTime.ToDisplay(w.Last):HH:mm:ss}, never seen before that"),

        "dauersender" => new(
            $"{Wer(f)} asks for {f.Subject} around the clock",
            $"{w.Count:N0} blocked queries in the last hour, and steadily so for days — "
            + $"roughly {w.PerDay:N0} a day extrapolated. This is not a spike but this device's "
            + "normal state: an app asks on a fixed beat and does not take no for an answer. "
            + "The block works; it just costs queries continuously. If you want to know which "
            + "program it is, the name is the best lead.",
            $"{w.Count:N0} queries in {Span(w.Span)} ({w.ProMinute:F1}/min), "
            + $"baseline {w.PerHour:F0}/h over {w.BaselineDays:F1} days, "
            + $"{w.Names} name(s), e.g. {w.Example}"),

        "portfreigabe" => w.ChangeKind switch
        {
            "neu" => new(
                $"New port forward: {w.Protocol} {w.Port} to the outside",
                "A forward has appeared that was not set up here. "
                + (w.ForAll
                    ? "It applies to any remote host — the entire internet can reach this "
                      + "device through it. "
                    : "It applies to one specific remote host only. ")
                + "The usual route is UPnP: a program on the network asks the router for it "
                + "directly. If you know which one, all is well. If not, it should go.",
                w.Now ?? ""),

            "geaendert" => new(
                $"Port forward {w.Protocol} {w.Port} now points elsewhere",
                "The same forward now leads to a different target, or was switched on or "
                + "off. A change of the inner target means the port from outside now reaches "
                + "a different device.",
                $"before: {w.Before} — now: {w.Now}"),

            _ => new(
                $"Port forward {w.Protocol} {w.Port} has disappeared",
                "The forward is gone. Usually harmless — UPnP forwards expire when the "
                + "program does not renew them. It is listed so the other direction does not "
                + "go unnoticed.",
                w.Now ?? ""),
        },

        "neues-geraet" => new(
            $"New device on the network: {Wer(f)}",
            $"Seen for the first time, joined over {w.Connection}. "
            + (w.ZufallMac
                ? "The MAC address is randomised — phones do that per network. The same "
                  + "device can show up as new again after forgetting the network. "
                : "")
            + "If you cannot place it, it is worth checking who signed on.",
            $"MAC {f.Subject}, address {w.Address}, "
            + (w.Online ? "connected" : "offline right now")),

        _ => new(f.Title, f.Explanation, f.Evidence),
    };
}
