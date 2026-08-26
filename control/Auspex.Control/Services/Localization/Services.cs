namespace Auspex.Control.Services.Localization;

// Messages from the services.
//
// What stands here is only seen when something does not work — and then it
// is the only thing you see. An error message that changes language halfway
// through is worse than a monolingual interface: whoever reads it is
// already searching.
//
// These texts are reached through Strings.Current, not through injection —
// the reasons are written there.

public abstract partial class Strings
{
    // ── Router-Aktionen ───────────────────────────────────────────────────
    public abstract string ServiceMissing(string service);
    public abstract string RouterNotAnswering { get; }
    public abstract string WlanNotSwitchable { get; }
    public abstract string NoCount { get; }
    public abstract string MappingsNotChangeable { get; }
    public abstract string ActionDoesNotExist(string service, string action);
    public abstract string RouterDoesNotKnow(string was) ;
    public abstract string ReadOnlyBlocked { get; }
    public abstract string SignInRefused { get; }
    public abstract string ActionNeedsAccount { get; }
    public abstract string RouterReportsError(string code, string plainText);
    public abstract string RouterAnswersHttp(int status);
    public abstract string WithoutFurtherDetail { get; }

    // Names of the services, as the interface calls them when one is missing.
    public abstract string ServiceEventLog { get; }
    public abstract string ServicePortMappings { get; }

    // ── Fritz!Box web interface ───────────────────────────────────────────
    public abstract string WebSignInRefused { get; }
    public abstract string NoIpv4Address(string input);
    public abstract string DnsNotAnswering(string address);
    public abstract string FieldNamesChanged { get; }
    public abstract string SentButUnreadable { get; }
    public abstract string DnsSet(string address);
    public abstract string DnsNotApplied(string steht);
    public abstract string ConfirmationRefused(string reason);
    public abstract string ConfirmationNeeded(string kind);
    public abstract string ConfirmationButton { get; }

    /// <summary>The second route: a code from an authenticator app.</summary>
    public abstract string ConfirmationApp { get; }

    /// <summary>
    /// Joins the offered routes. A separator and not a list: the sentence
    /// around it is built in <c>FritzWebClient</c>, and "or" is the one word
    /// in it that changes with the language.
    /// </summary>
    public abstract string ConfirmationOr { get; }
    public abstract string ConfirmationPhone { get; }
    public abstract string ConfirmationGeneric { get; }

    // ── Wireless encryption ───────────────────────────────────────────────
    public abstract string WlanOpen { get; }
    /// <summary>"2.4 GHz" — the separator hangs off the language.</summary>
    public abstract string WlanBand24 { get; }
    public abstract string WlanWep { get; }
    public abstract string Unknown { get; }
    /// <summary>Two schemes side by side — "WPA2 and WPA3".</summary>
    public abstract string And(string a, string b);

    // ── Sicherung ─────────────────────────────────────────────────────────
    public abstract string NotAnAuspexBackup { get; }
    public abstract string SchemaMismatch(string fromBackup, string here);
    public abstract string BackupMerged { get; }
    public abstract string NotAReadableZip { get; }
    public abstract string SuspiciousPath(string path);
    public abstract string NoDatabaseInside { get; }

    // ── Regeln ────────────────────────────────────────────────────────────
    public abstract string RuleWritingOff { get; }
}

public sealed partial class StringsDe
{
    public override string ServiceMissing(string service) =>
        $"Der Router bietet {service} nicht an — Internetzugang lässt sich nicht schalten.";
    public override string RouterNotAnswering => "Der Router hat nicht geantwortet.";
    public override string WlanNotSwitchable => "Dieses Funknetz lässt sich nicht schalten.";
    public override string NoCount =>
        "Der Router nennt keine Anzahl — die Antwort ist nicht auswertbar.";
    public override string MappingsNotChangeable =>
        "Portfreigaben lassen sich auf diesem Router nicht ändern.";
    public override string ActionDoesNotExist(string service, string action) =>
        $"Aktion {service}#{action} gibt es auf diesem Router nicht.";
    public override string RouterDoesNotKnow(string was) => $"Dieser Router kennt {was} nicht.";
    public override string ReadOnlyBlocked =>
        "Nur-Lesen ist eingeschaltet: verändernde Aktionen sind gesperrt.";
    public override string SignInRefused =>
        "Der Router weist die Anmeldung zurück. Benutzername oder Kennwort stimmen "
        + "nicht, oder dem Konto fehlt das Recht \"FRITZ!Box Einstellungen\".";
    public override string ActionNeedsAccount =>
        "Diese Aktion verlangt ein Router-Konto. Offen sind nur einzelne Leseaktionen.";
    public override string RouterReportsError(string code, string plainText) =>
        $"Router meldet Fehler {code}: {plainText}";
    public override string RouterAnswersHttp(int status) =>
        $"Router antwortet mit HTTP {status}.";
    public override string WithoutFurtherDetail => "ohne nähere Angabe";

    public override string ServiceEventLog => "ein Ereignisprotokoll";
    public override string ServicePortMappings => "Portfreigaben";

    public override string WebSignInRefused =>
        "Die Weboberfläche der Fritz!Box weist die Anmeldung zurück.";
    public override string NoIpv4Address(string input) =>
        $"{input} ist keine IPv4-Adresse.";
    public override string DnsNotAnswering(string address) =>
        $"Unter {address} antwortet auf Port 53 kein DNS-Server. Diese Adresse zu "
        + "verteilen würde dem ganzen Heimnetz die Namensauflösung nehmen — auch dem "
        + "Rechner, mit dem du es zurücknehmen wolltest.";
    public override string FieldNamesChanged =>
        "Die erwarteten Felder stehen nicht auf der Seite; vermutlich hat eine "
        + "Firmware die Feldnamen geändert.";
    public override string SentButUnreadable =>
        "Gesendet, aber das Ergebnis war nicht mehr lesbar.";
    public override string DnsSet(string address) =>
        $"Lokaler DNS-Server steht jetzt auf {address}. Geräte übernehmen ihn, "
        + "sobald sie ihre DHCP-Adresse erneuern.";
    public override string DnsNotApplied(string steht) =>
        $"Die Box hat den Wert nicht übernommen — sie steht weiterhin auf {steht}.";
    public override string ConfirmationRefused(string reason) =>
        $"Die Fritz!Box hat die Bestätigungsanfrage abgewiesen{reason}. Meist steht "
        + "dort noch eine frühere Anfrage offen oder es wurde zu oft hintereinander "
        + "versucht. Warte einige Minuten, oder nimm die Änderung direkt im Menü der "
        + "Box vor.";
    public override string ConfirmationNeeded(string kind) =>
        $"Die Fritz!Box verlangt für diese Änderung eine Bestätigung am Gerät: {kind}. "
        + "Die Anfrage liegt dort jetzt bereit und läuft nach wenigen Minuten ab. "
        + "Auspex kann diesen Schritt nicht übernehmen — das ist der Sinn eines "
        + "solchen Schutzes.";
    public override string ConfirmationButton => "Taste an der Box drücken";
    public override string ConfirmationApp => "Code aus der Authenticator-App eingeben";
    public override string ConfirmationOr => ", oder ";
    public override string ConfirmationPhone => "Tastenfolge am angeschlossenen Telefon wählen";
    public override string ConfirmationGeneric => "die Bestätigung am Gerät durchführen";

    public override string NotAnAuspexBackup =>
        "Das ist keine Auspex-Sicherung: manifest.json fehlt.";
    public override string SchemaMismatch(string fromBackup, string here) =>
        $"Die Sicherung stammt aus Schema-Stand {fromBackup}, hier läuft {here}. "
        + "Zurückspielen würde Daten verbiegen.";
    public override string BackupMerged => "Sicherung zusammengeführt.";
    public override string NotAReadableZip => "Die Datei ist kein lesbares ZIP-Archiv.";
    public override string SuspiciousPath(string path) =>
        $"Verdächtiger Pfad in der Sicherung: {path}";
    public override string NoDatabaseInside => "keine Datenbank in der Sicherung";

    public override string RuleWritingOff => "Regel-Schreiben ist abgeschaltet";

    public override string WlanOpen => "offen";
    public override string WlanBand24 => "2,4 GHz";
    public override string WlanWep => "WEP (unsicher)";
    public override string Unknown => "unbekannt";
    public override string And(string a, string b) => $"{a} und {b}";
}

public sealed partial class StringsEn
{
    public override string ServiceMissing(string service) =>
        $"The router does not offer {service} — internet access cannot be switched.";
    public override string RouterNotAnswering => "The router did not answer.";
    public override string WlanNotSwitchable => "This wireless network cannot be switched.";
    public override string NoCount =>
        "The router gives no count — the answer cannot be interpreted.";
    public override string MappingsNotChangeable =>
        "Port forwards cannot be changed on this router.";
    public override string ActionDoesNotExist(string service, string action) =>
        $"There is no action {service}#{action} on this router.";
    public override string RouterDoesNotKnow(string was) =>
        $"This router does not know about {was}.";
    public override string ReadOnlyBlocked =>
        "Read-only is on: changing actions are locked.";
    public override string SignInRefused =>
        "The router rejects the sign-in. Either the user name or password is wrong, "
        + "or the account lacks the \"FRITZ!Box Einstellungen\" permission.";
    public override string ActionNeedsAccount =>
        "This action needs a router account. Only a few read actions are open.";
    public override string RouterReportsError(string code, string plainText) =>
        $"Router reports error {code}: {plainText}";
    public override string RouterAnswersHttp(int status) =>
        $"Router answers with HTTP {status}.";
    public override string WithoutFurtherDetail => "no further detail";

    public override string ServiceEventLog => "an event log";
    public override string ServicePortMappings => "port forwards";

    public override string WebSignInRefused =>
        "The Fritz!Box web interface rejects the sign-in.";
    public override string NoIpv4Address(string input) =>
        $"{input} is not an IPv4 address.";
    public override string DnsNotAnswering(string address) =>
        $"Nothing answers on port 53 at {address}. Handing that address out would take "
        + "name resolution away from the whole home network — including the machine "
        + "you would want to undo it from.";
    public override string FieldNamesChanged =>
        "The expected fields are not on the page; a firmware update has probably "
        + "renamed them.";
    public override string SentButUnreadable =>
        "Sent, but the result was no longer readable.";
    public override string DnsSet(string address) =>
        $"The local DNS server is now {address}. Devices pick it up as soon as they "
        + "renew their DHCP lease.";
    public override string DnsNotApplied(string steht) =>
        $"The box did not take the value — it still reads {steht}.";
    public override string ConfirmationRefused(string reason) =>
        $"The Fritz!Box refused the confirmation request{reason}. Usually an earlier "
        + "request is still open there, or it was tried too many times in a row. Wait "
        + "a few minutes, or make the change directly in the box's own menu.";
    public override string ConfirmationNeeded(string kind) =>
        $"The Fritz!Box wants this change confirmed on the device itself: {kind}. The "
        + "request is waiting there now and expires after a few minutes. Auspex cannot "
        + "take that step for you — which is the whole point of such a safeguard.";
    public override string ConfirmationButton => "press a button on the box";
    public override string ConfirmationApp => "enter a code from the authenticator app";
    public override string ConfirmationOr => ", or ";
    public override string ConfirmationPhone => "dial a key sequence on a connected phone";
    public override string ConfirmationGeneric => "confirm on the device";

    public override string NotAnAuspexBackup =>
        "This is not an Auspex backup: manifest.json is missing.";
    public override string SchemaMismatch(string fromBackup, string here) =>
        $"The backup comes from schema version {fromBackup}, this installation runs "
        + $"{here}. Restoring it would bend the data out of shape.";
    public override string BackupMerged => "Backup merged in.";
    public override string NotAReadableZip => "The file is not a readable ZIP archive.";
    public override string SuspiciousPath(string path) =>
        $"Suspicious path inside the backup: {path}";
    public override string NoDatabaseInside => "no database in the backup";

    public override string RuleWritingOff => "Writing rules is switched off";

    public override string WlanOpen => "open";
    public override string WlanBand24 => "2.4 GHz";
    public override string WlanWep => "WEP (insecure)";
    public override string Unknown => "unknown";
    public override string And(string a, string b) => $"{a} and {b}";
}
