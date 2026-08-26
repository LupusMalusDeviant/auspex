namespace Auspex.Control.Services.Localization;

// Settings: router account and browser extension.
//
// The Fritz!Box menu paths stay German, in the English version too: a box
// with German firmware has no menu item called "System → FRITZ!Box users".
// Whoever translates the instructions sends somebody hunting for something
// that is called differently on their screen. So the sentence around the
// path gets translated, not the path inside it.

public abstract partial class Strings
{
    public abstract string TitleSettings { get; }
    public abstract string SettingsIntro { get; }

    // ── Router-Konto ──────────────────────────────────────────────────────
    public abstract string Connected { get; }
    public abstract string NoAccount { get; }
    public abstract string AccessFromEnvironment { get; }
    public abstract string RouterAccountHowTo { get; }
    public abstract string RouterAddress { get; }
    public abstract string UserName { get; }
    public abstract string ReadOnlyToggle { get; }
    public abstract string Checking { get; }
    public abstract string SaveAndConnect { get; }
    public abstract string ReallyRemove { get; }
    public abstract string RemoveAccount { get; }
    public abstract string SignInRejected(string router);
    public abstract string ConnectedTo(string router, int services, int actions);
    public abstract string Failed(string reason);
    public abstract string AccountRemovedMessage { get; }
    public abstract string WhatThisAccountMeansTitle { get; }
    public abstract string WhatThisAccountMeans { get; }

    // ── Browser-Erweiterung ───────────────────────────────────────────────
    public abstract string BrowserExtension { get; }
    public abstract string InstalledHere { get; }
    public abstract string NotInstalledHere { get; }
    public abstract string TokenPresent { get; }
    public abstract string NoToken { get; }
    public abstract string ExtensionWhatFor { get; }
    public abstract string CopyOnce { get; }
    public abstract string CreateNewToken { get; }
    public abstract string CreateToken { get; }
    public abstract string ReallyRevoke { get; }
    public abstract string Revoke { get; }
    public abstract string TokenCreatedOn(string wann);
    public abstract string NotInstalledTitle { get; }
    public abstract string TokenAppliesEverywhere { get; }
    public abstract string GetPackage { get; }
    public abstract string VersionLabel(string version);
    public abstract string ThenColon { get; }
    public abstract string StepUnpack { get; }
    public abstract string StepBuildBundle { get; }
    public abstract string StepEnterToken { get; }
    public abstract string AddressCopied { get; }
    public abstract string CopyAddress { get; }
    public abstract string NoDirectLink { get; }
    public abstract string ExtensionRunning(string version);

    // ── Sensor ────────────────────────────────────────────────────────────
    public abstract string SensorTitle { get; }

    // ── Voraussetzungen ───────────────────────────────────────────────────
    public abstract string PartsTitle { get; }
    public abstract string PartsIntro { get; }
    public abstract string PartActive { get; }
    public abstract string PartIdle { get; }
    public abstract string PartMissing { get; }
    public abstract string PartName(string key);
    public abstract string PartAdds(string key);
    public abstract string PartHowTo(string key);

    // ── Zeitzone ──────────────────────────────────────────────────────────
    public abstract string TimeZoneTitle { get; }
    public abstract string TimeZoneExplained { get; }
    public abstract string TimeZoneDefault(string zone);
    public abstract string TimeZoneInEffect(string zone, string example);
    public abstract string TimeZoneSaved { get; }
    public abstract string SensorWhatFor { get; }
    public abstract string SensorLimit { get; }
    public abstract string GetSensor(string size);
    public abstract string SensorMissing { get; }
    public abstract string SensorSteps { get; }
    public abstract string SensorStep1 { get; }
    public abstract string SensorStep2 { get; }
    public abstract string SensorStep3 { get; }
    public abstract string SensorRights { get; }
    public abstract string SensorRemove { get; }

    /// <summary>How the extension gets into each browser.</summary>
    public abstract string LoadingInstructions(string browser);
}

public sealed partial class StringsDe
{
    public override string TitleSettings => "Einstellungen";
    public override string SettingsIntro =>
        "Was sich zur Laufzeit ändern lässt, ohne den Container anzufassen.";

    public override string Connected => "verbunden";
    public override string NoAccount => "kein Konto";
    public override string AccessFromEnvironment =>
        "Die Zugangsdaten kommen aus der Umgebung — gesetzt über ROUTER_USER und "
        + "ROUTER_PASSWORD in der .env. Hier werden sie bewusst nicht "
        + "überschrieben: wer seine Installation in der compose.yml beschreibt, "
        + "soll nicht erleben, dass ein Klick in der Oberfläche das still "
        + "übersteuert. Zum Ändern die .env anpassen und den Dienst neu starten.";
    public override string RouterAccountHowTo =>
        "Mit einem Router-Konto kann Auspex den Router lesen und einstellen. Bei "
        + "einer Fritz!Box: unter „System → FRITZ!Box-Benutzer\" einen eigenen "
        + "Benutzer anlegen — nicht den eigenen —, ihm das Recht „FRITZ!Box "
        + "Einstellungen\" geben, und unter „Heimnetz → Netzwerk → "
        + "Netzwerkeinstellungen\" sicherstellen, dass „Zugriff für Anwendungen "
        + "zulassen\" eingeschaltet ist.";
    public override string RouterAddress => "Adresse des Routers";
    public override string UserName => "Benutzername";
    public override string ReadOnlyToggle =>
        "Nur lesen — der vollständige Katalog bleibt sichtbar, aber nichts lässt "
        + "sich auslösen";
    public override string Checking => "Wird geprüft …";
    public override string SaveAndConnect => "Speichern und verbinden";
    public override string ReallyRemove => "Wirklich entfernen";
    public override string RemoveAccount => "Konto entfernen";
    public override string SignInRejected(string router) =>
        $"Gespeichert, aber {router} weist die Anmeldung ab. Stimmen Benutzername "
        + "und Kennwort, und hat das Konto das Recht \"FRITZ!Box Einstellungen\"?";
    public override string ConnectedTo(string router, int services, int actions) =>
        $"Verbunden mit {router} — {services} Dienste, {actions} Aktionen. Der "
        + "Router-Bereich steht jetzt in der Navigation.";
    public override string Failed(string reason) => $"Fehlgeschlagen: {reason}";
    public override string AccountRemovedMessage =>
        "Konto entfernt. Der Router-Bereich ist wieder ausgeblendet.";
    public override string WhatThisAccountMeansTitle => "Was dieses Konto bedeutet.";
    public override string WhatThisAccountMeans =>
        " Eine Fritz!Box kennt keine Rechteabstufung auf einzelne Dienste: ein "
        + "Konto mit „FRITZ!Box Einstellungen\" kann alles, was du im Menü kannst. "
        + "Auspex hat damit die Konfiguration deines Netzes in der Hand — ohne "
        + "Konto kann es nur Antworten verweigern.";

    public override string BrowserExtension => "Browser-Erweiterung";
    public override string InstalledHere => "in diesem Browser installiert";
    public override string NotInstalledHere => "hier nicht installiert";
    public override string TokenPresent => "Zeichen vorhanden";
    public override string NoToken => "kein Zeichen";
    public override string ExtensionWhatFor =>
        "Die Erweiterung sieht, welche Anfragen auf der gerade geöffneten Seite an "
        + "der Namensauflösung gescheitert sind, und gibt sie auf Klick frei — für "
        + "dieses eine Gerät, befristet oder dauerhaft. Welches Gerät gemeint ist, "
        + "sagt nicht die Erweiterung, sondern die Adresse, von der sie anfragt; "
        + "über sie lässt sich also kein fremdes Gerät verändern.";
    public override string CopyOnce =>
        "Einmal kopieren — danach wird es nicht wieder angezeigt.";
    public override string CreateNewToken => "Neues Zeichen erzeugen";
    public override string CreateToken => "Zeichen erzeugen";
    public override string ReallyRevoke => "Wirklich zurückziehen";
    public override string Revoke => "Zurückziehen";
    public override string TokenCreatedOn(string wann) =>
        $"Erzeugt am {wann}. Ein neues Zeichen macht das alte sofort ungültig — "
        + "die Erweiterung muss es dann erneut eintragen.";
    public override string NotInstalledTitle =>
        "In diesem Browser ist die Erweiterung nicht installiert.";
    public override string TokenAppliesEverywhere =>
        "Ein vorhandenes Zeichen sagt darüber nichts — es gilt für alle "
        + "Browser, die es eintragen.";
    public override string GetPackage =>
        "Paket holen — es wird beim Klick aus den Quellen geschnürt und ist damit "
        + "immer so aktuell wie diese Anwendung.";
    public override string VersionLabel(string version) => $"Fassung {version}";
    public override string ThenColon => "Dann:";
    public override string StepUnpack =>
        "Archiv entpacken — der entstandene Ordner ist das, was der Browser lädt.";
    public override string StepBuildBundle =>
        "Bundle bauen: ./extension/build.sh im Projektverzeichnis.";
    public override string StepEnterToken =>
        "In der Erweiterung unter „Einstellungen\" diese Adresse und ein Zeichen "
        + "eintragen.";
    public override string AddressCopied => "Adresse kopiert";
    public override string CopyAddress => "Adresse kopieren";
    public override string NoDirectLink =>
        "Einen Knopf, der die Erweiterungsseite direkt öffnet, kann es nicht "
        + "geben: Browser lassen sich von einer Webseite aus nicht nach chrome:// "
        + "oder about: schicken. Die Adresse oben muss von Hand in die Adresszeile.";
    public override string ExtensionRunning(string version) =>
        $"Erweiterung {version} läuft in diesem Browser.";

    public override string SensorTitle => "Sensor für den Rechner";

    public override string PartsTitle => "Voraussetzungen";

    public override string PartsIntro =>
        "Auspex benutzt Teile, die es nicht mitbringt. Keines davon schaltet "
        + "sich von allein ein: ein Werkzeug, das ungefragt einen Router "
        + "abklopft oder hunderte Megabyte über eine Heimleitung zieht, nimmt "
        + "sich etwas heraus. Dafür steht hier, was fehlt — eine leere Spalte "
        + "sieht sonst aus wie „hier gibt es nichts\" statt wie „hier fehlt "
        + "etwas\".";

    public override string PartActive => "aktiv";
    public override string PartIdle => "aus";
    public override string PartMissing => "nicht eingerichtet";

    public override string PartName(string key) => key switch
    {
        "analytics" => "Auswertung",
        "router" => "Router-Konto",
        "extension" => "Browser-Erweiterung",
        "sensor" => "Sensor",
        "origin" => "Herkunft der Adressen",
        _ => key,
    };

    public override string PartAdds(string key) => key switch
    {
        "analytics" => "Zeitreihen, Auffälligkeiten und den Verlauf im Dossier. "
                       + "Ohne sie gibt es nur den laufenden Strom.",
        "router" => "Geräteliste, WLAN, Portfreigaben, IPv4 und das "
                    + "Ereignisprotokoll der Box.",
        "extension" => "Freigeben und Blocken aus dem Browser heraus, ohne "
                       + "das Dashboard zu öffnen.",
        "sensor" => "Welches Programm gefunkt hat, und wie viele Bytes. Das "
                    + "sieht ein DNS-Filter grundsätzlich nicht.",
        "origin" => "Betreiber, Land und Stadt hinter einer Adresse — "
                    + "nachgeschlagen in örtlichen Dateien, nie über eine API.",
        _ => "",
    };

    public override string PartHowTo(string key) => key switch
    {
        "analytics" => "Analytics__Enabled in der Umgebung.",
        "router" => "Oben auf dieser Seite ein Konto hinterlegen.",
        "extension" => "Ein Zeichen erzeugen und das Paket in den Browser laden.",
        "sensor" => "Paket herunterladen und setup.ps1 ausführen.",
        "origin" => "Geo__Enabled in der Umgebung. Der erste Lauf holt rund "
                    + "90 MB, mit Stadt weitere 90 MB.",
        _ => "",
    };

    public override string TimeZoneTitle => "Zeitzone";

    public override string TimeZoneExplained =>
        "In welcher Zeitzone alle Uhrzeiten gezeigt werden — im Query-Log, in "
        + "den Auffälligkeiten, überall. Sie gilt für das ganze Dashboard und "
        + "nicht je Betrachter: Auspex beobachtet ein Netz an einem Ort, und eine "
        + "Anfrage um drei Uhr nachts bleibt ein nächtliches Ereignis, auch wenn "
        + "gerade jemand von anderswo draufschaut. Aus demselben Grund hängt sie "
        + "nicht an der Sprache. Die Erkennung nächtlicher Auffälligkeiten "
        + "rechnet mit derselben Zone.";

    public override string TimeZoneDefault(string zone) =>
        $"Vorgabe des Servers ({zone})";

    public override string TimeZoneInEffect(string zone, string example) =>
        $"Es gilt {zone}. Jetzt ist es dort {example}.";

    public override string TimeZoneSaved => "Zeitzone übernommen.";
    public override string SensorWhatFor =>
        "Welches Programm eine Anfrage gestellt hat, kann ein DNS-Filter nicht "
        + "wissen — zwischen ihm und dem Programm liegt das Betriebssystem. Der "
        + "Sensor läuft auf dem Rechner selbst, liest dessen "
        + "TCP-Verbindungstabelle und meldet, wer mit wem spricht. Das Ergebnis "
        + "steht unter „Wohin?\" neben den Empfängern.";
    public override string SensorLimit =>
        "Er liest die Verbindungstabelle, nicht den Verkehr: keine Inhalte, kein "
        + "GET oder POST. Und kein UDP — Windows führt dafür keine Gegenstelle, "
        + "womit QUIC unsichtbar bleibt. Gemeldet werden Programmname, Ziel und "
        + "Port; kein Pfad, kein Fenstertitel, keine Kommandozeile.";
    public override string GetSensor(string size) => $"Sensor holen ({size})";
    public override string SensorMissing =>
        "In diesem Abbild liegt kein Sensor. Er entsteht beim Bauen des "
        + "Containers; wurde das Abbild ohne die Quellen unter sensor/ gebaut, "
        + "fehlt er.";
    public override string SensorSteps => "Dann:";
    public override string SensorStep1 =>
        "Archiv entpacken. Die Adresse dieses Dashboards liegt schon darin.";
    public override string SensorStep2 =>
        "setup.ps1 mit der rechten Maustaste, „Mit PowerShell ausführen\". "
        + "Es fragt nach dem Zeichen und holt sich selbst die nötigen Rechte.";
    public override string SensorStep3 =>
        "Fertig. Der Sensor startet ab jetzt bei jeder Anmeldung, ohne Fenster.";
    public override string SensorRights =>
        "Die Aufgabe läuft mit höchsten Rechten. Nicht aus Bequemlichkeit: "
        + "Windows gibt Byte-Zähler je Verbindung nur über TCP-ESTATS heraus, "
        + "und die verlangen sie. Ohne die Rechte läuft der Sensor auch — dann "
        + "bleibt die Spalte leer, was ehrlicher ist als eine Null.";
    public override string SensorRemove =>
        "Wieder loswerden: setup.ps1 -Remove";

    public override string LoadingInstructions(string browser) => browser switch
    {
        "firefox" =>
            "In <code>about:debugging</code> &rarr; <em>Dieses Firefox</em> &rarr; "
            + "<em>Tempor&auml;res Add-on laden</em> &rarr; die <code>manifest.json</code> "
            + "im entpackten Ordner. Tempor&auml;r geladene Add-ons verschwinden beim "
            + "Neustart des Browsers.",
        "chrome" or "edge" or "opera" =>
            "In <code>chrome://extensions</code> den <em>Entwicklermodus</em> "
            + "einschalten, dann <em>Entpackt laden</em> &rarr; den entpackten Ordner.",
        "safari" =>
            "Safari nimmt keine entpackten Erweiterungen an &mdash; es braucht den "
            + "Umweg &uuml;ber Xcode. Der Rest von Auspex funktioniert ohne die "
            + "Erweiterung unver&auml;ndert.",
        _ =>
            "Chromium-Browser: <code>chrome://extensions</code> &rarr; Entwicklermodus "
            + "&rarr; <em>Entpackt laden</em> &rarr; <code>dist/chrome</code>. "
            + "Firefox: <code>about:debugging</code> &rarr; <em>Tempor&auml;res Add-on "
            + "laden</em> &rarr; <code>dist/firefox/manifest.json</code>.",
    };
}

public sealed partial class StringsEn
{
    public override string TitleSettings => "Settings";
    public override string SettingsIntro =>
        "What can be changed at runtime, without touching the container.";

    public override string Connected => "connected";
    public override string NoAccount => "no account";
    public override string AccessFromEnvironment =>
        "The credentials come from the environment — set through ROUTER_USER and "
        + "ROUTER_PASSWORD in the .env file. They are deliberately not overwritten "
        + "here: if you describe your installation in compose.yml, a click in the "
        + "interface should not quietly override it. To change them, edit .env and "
        + "restart the service.";
    // The menu paths stay German: a box with German firmware has no item
    // called "FRITZ!Box users".
    public override string RouterAccountHowTo =>
        "With a router account, Auspex can read and configure the router. On a "
        + "Fritz!Box: under „System → FRITZ!Box-Benutzer\" create a separate user "
        + "— not your own —, grant it the „FRITZ!Box Einstellungen\" permission, "
        + "and under „Heimnetz → Netzwerk → Netzwerkeinstellungen\" make sure "
        + "„Zugriff für Anwendungen zulassen\" is switched on. (Those menu names "
        + "stay in German here because that is what the box itself shows.)";
    public override string RouterAddress => "Router address";
    public override string UserName => "User name";
    public override string ReadOnlyToggle =>
        "Read only — the full catalogue stays visible, but nothing can be "
        + "triggered";
    public override string Checking => "Checking …";
    public override string SaveAndConnect => "Save and connect";
    public override string ReallyRemove => "Yes, remove it";
    public override string RemoveAccount => "Remove account";
    public override string SignInRejected(string router) =>
        $"Saved, but {router} is refusing the sign-in. Are the user name and "
        + "password right, and does the account carry the \"FRITZ!Box "
        + "Einstellungen\" permission?";
    public override string ConnectedTo(string router, int services, int actions) =>
        $"Connected to {router} — {services} services, {actions} actions. The "
        + "router section is now in the navigation.";
    public override string Failed(string reason) => $"Failed: {reason}";
    public override string AccountRemovedMessage =>
        "Account removed. The router section is hidden again.";
    public override string WhatThisAccountMeansTitle => "What this account means.";
    public override string WhatThisAccountMeans =>
        " A Fritz!Box has no per-service permissions: an account with „FRITZ!Box "
        + "Einstellungen\" can do everything you can do in the menu. Auspex then "
        + "holds your network's configuration in its hands — without an account it "
        + "can only refuse answers.";

    public override string BrowserExtension => "Browser extension";
    public override string InstalledHere => "installed in this browser";
    public override string NotInstalledHere => "not installed here";
    public override string TokenPresent => "token present";
    public override string NoToken => "no token";
    public override string ExtensionWhatFor =>
        "The extension sees which requests on the page you have open failed at "
        + "name resolution, and releases them with a click — for this one device, "
        + "temporarily or for good. Which device is meant is decided not by the "
        + "extension but by the address it asks from; it cannot be used to change "
        + "somebody else's device.";
    public override string CopyOnce =>
        "Copy it now — it is never shown again.";
    public override string CreateNewToken => "Issue a new token";
    public override string CreateToken => "Issue a token";
    public override string ReallyRevoke => "Yes, revoke it";
    public override string Revoke => "Revoke";
    public override string TokenCreatedOn(string wann) =>
        $"Issued {wann}. A new token invalidates the old one immediately — the "
        + "extension then has to be given the new one.";
    public override string NotInstalledTitle =>
        "The extension is not installed in this browser.";
    public override string TokenAppliesEverywhere =>
        "An existing token says nothing about that — it is valid for every "
        + "browser that enters it.";
    public override string GetPackage =>
        "Get the package — it is packed from source on click, so it is always as "
        + "current as this application.";
    public override string VersionLabel(string version) => $"Version {version}";
    public override string ThenColon => "Then:";
    public override string StepUnpack =>
        "Unpack the archive — the resulting folder is what the browser loads.";
    public override string StepBuildBundle =>
        "Build the bundle: ./extension/build.sh in the project directory.";
    public override string StepEnterToken =>
        "In the extension, under „Settings\", enter this address and a token.";
    public override string AddressCopied => "Address copied";
    public override string CopyAddress => "Copy address";
    public override string NoDirectLink =>
        "A button that opens the extensions page directly cannot exist: browsers "
        + "refuse to be sent to chrome:// or about: from a web page. The address "
        + "above has to go into the address bar by hand.";
    public override string ExtensionRunning(string version) =>
        $"Extension {version} is running in this browser.";

    public override string SensorTitle => "Sensor for the machine";

    public override string PartsTitle => "Prerequisites";

    public override string PartsIntro =>
        "Auspex uses parts it does not ship. None of them switch themselves "
        + "on: a tool that probes a router unasked, or pulls hundreds of "
        + "megabytes over a home line, is helping itself. So this page says "
        + "what is missing — an empty column otherwise reads as \"there is "
        + "nothing here\" rather than \"something is missing here\".";

    public override string PartActive => "active";
    public override string PartIdle => "off";
    public override string PartMissing => "not set up";

    public override string PartName(string key) => key switch
    {
        "analytics" => "Analysis",
        "router" => "Router account",
        "extension" => "Browser extension",
        "sensor" => "Sensor",
        "origin" => "Address origins",
        _ => key,
    };

    public override string PartAdds(string key) => key switch
    {
        "analytics" => "Time series, findings and the dossier history. "
                       + "Without it there is only the live stream.",
        "router" => "Device list, wireless, port mappings, IPv4 and the "
                    + "router's event log.",
        "extension" => "Allowing and blocking from the browser without "
                       + "opening the dashboard.",
        "sensor" => "Which program did the talking, and how many bytes. A DNS "
                    + "filter fundamentally cannot see that.",
        "origin" => "Operator, country and city behind an address — looked up "
                    + "in local files, never through an API.",
        _ => "",
    };

    public override string PartHowTo(string key) => key switch
    {
        "analytics" => "Analytics__Enabled in the environment.",
        "router" => "Store an account at the top of this page.",
        "extension" => "Create a token and load the package into the browser.",
        "sensor" => "Download the package and run setup.ps1.",
        "origin" => "Geo__Enabled in the environment. The first run fetches "
                    + "about 90 MB, with cities another 90 MB.",
        _ => "",
    };

    public override string TimeZoneTitle => "Time zone";

    public override string TimeZoneExplained =>
        "Which time zone every clock time is shown in — in the query log, in "
        + "the findings, everywhere. It applies to the whole dashboard rather "
        + "than to each viewer: Auspex watches one network in one place, and a "
        + "request at three in the morning stays a night-time event even if "
        + "someone is looking at it from elsewhere. For the same reason it does "
        + "not follow the language. Night-time findings are judged against the "
        + "same zone.";

    public override string TimeZoneDefault(string zone) =>
        $"Server default ({zone})";

    public override string TimeZoneInEffect(string zone, string example) =>
        $"Currently {zone}. The time there is now {example}.";

    public override string TimeZoneSaved => "Time zone applied.";
    public override string SensorWhatFor =>
        "Which program made a query is something a DNS filter cannot know — the "
        + "operating system sits between the two. The sensor runs on the machine "
        + "itself, reads its TCP connection table and reports who talks to whom. "
        + "The result appears under „Where to?\" next to the recipients.";
    public override string SensorLimit =>
        "It reads the connection table, not the traffic: no content, no GET or "
        + "POST. And no UDP — Windows keeps no remote address for it, which "
        + "leaves QUIC invisible. What is reported is the program name, the "
        + "destination and the port; no path, no window title, no command line.";
    public override string GetSensor(string size) => $"Get the sensor ({size})";
    public override string SensorMissing =>
        "There is no sensor in this image. It is produced when the container is "
        + "built; if the image was built without the sources under sensor/, it "
        + "is missing.";
    public override string SensorSteps => "Then:";
    public override string SensorStep1 =>
        "Unpack the archive. This dashboard's address is already in it.";
    public override string SensorStep2 =>
        "Right-click setup.ps1, “Run with PowerShell”. It asks for the "
        + "token and elevates itself.";
    public override string SensorStep3 =>
        "Done. From now on the sensor starts at every logon, without a window.";
    public override string SensorRights =>
        "The task runs with highest privileges. Not for convenience: Windows "
        + "only hands out per-connection byte counters through TCP ESTATS, and "
        + "those require them. Without the rights the sensor still runs — the "
        + "column then stays empty, which is more honest than a zero.";
    public override string SensorRemove =>
        "To undo: setup.ps1 -Remove";

    public override string LoadingInstructions(string browser) => browser switch
    {
        "firefox" =>
            "In <code>about:debugging</code> &rarr; <em>This Firefox</em> &rarr; "
            + "<em>Load Temporary Add-on</em> &rarr; the <code>manifest.json</code> in "
            + "the unpacked folder. Temporarily loaded add-ons disappear when the "
            + "browser restarts.",
        "chrome" or "edge" or "opera" =>
            "In <code>chrome://extensions</code> turn on <em>Developer mode</em>, then "
            + "<em>Load unpacked</em> &rarr; the unpacked folder.",
        "safari" =>
            "Safari does not accept unpacked extensions &mdash; it needs the detour "
            + "through Xcode. The rest of Auspex works unchanged without the "
            + "extension.",
        _ =>
            "Chromium browsers: <code>chrome://extensions</code> &rarr; Developer mode "
            + "&rarr; <em>Load unpacked</em> &rarr; <code>dist/chrome</code>. "
            + "Firefox: <code>about:debugging</code> &rarr; <em>Load Temporary Add-on</em> "
            + "&rarr; <code>dist/firefox/manifest.json</code>.",
    };
}
