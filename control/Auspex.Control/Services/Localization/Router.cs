namespace Auspex.Control.Services.Localization;

// The router section: guard, overview, devices, wireless networks.

public abstract partial class Strings
{
    // ── Guard in front of the section ─────────────────────────────────────
    public abstract string RouterSubOverview { get; }
    public abstract string RouterSubDevices { get; }
    public abstract string RouterSubWlan { get; }
    public abstract string RouterSubPortMappings { get; }
    public abstract string RouterSubIpv4 { get; }
    public abstract string RouterSubEvents { get; }
    public abstract string RouterSubCatalogue { get; }
    public abstract string ReadOnly { get; }
    public abstract string NoRouterAccount { get; }
    public abstract string RouterAccountWhatFor { get; }
    public abstract string StoreAccount { get; }

    // ── Overview ──────────────────────────────────────────────────────────
    public abstract string RouterIntro { get; }
    public abstract string Loading { get; }
    public abstract string RouterUnreachable(string reason);
    public abstract string Services { get; }
    public abstract string Actions { get; }
    public abstract string OfWhichChanging { get; }
    public abstract string Events { get; }
    public abstract string ViewEntries(int count);
    public abstract string View { get; }
    public abstract string Counting { get; }
    public abstract string CatalogueIncomplete { get; }
    public abstract string CatalogueMissing(int fehlend, int total);
    public abstract string CatalogueWhy { get; }
    public abstract string ReadOnlyOn { get; }
    public abstract string ReadOnlyExplained { get; }
    public abstract string ViewAllActions { get; }
    public abstract string ColumnService { get; }
    public abstract string ColumnChanging { get; }

    // ── Devices on the router ─────────────────────────────────────────────
    public abstract string TitleRouterDevices { get; }
    public abstract string RouterDevicesIntro { get; }
    public abstract string DeviceTally(int total, int online);
    public abstract string OnlyConnected { get; }
    public abstract string NoDevicesReported { get; }
    public abstract string ColumnDevice { get; }
    public abstract string ColumnIp { get; }
    public abstract string ColumnMac { get; }
    public abstract string ColumnLink { get; }
    public abstract string ColumnInternet { get; }
    public abstract string RandomMac { get; }
    public abstract string RandomMacTitle { get; }
    public abstract string Online { get; }
    public abstract string Offline { get; }
    public abstract string BlockDevice { get; }
    public abstract string CheckDevice { get; }
    public abstract string DevicesWithoutAccount { get; }

    // ── Funknetze ─────────────────────────────────────────────────────────
    public abstract string TitleWlan { get; }
    public abstract string WlanIntro { get; }
    public abstract string NotRead { get; }
    public abstract string NoWirelessNetworks { get; }
    public abstract string Unnamed { get; }
    public abstract string Guest { get; }
    public abstract string On { get; }
    public abstract string Off { get; }
    public abstract string Kind { get; }
    public abstract string GuestNetwork { get; }
    public abstract string HomeNetwork { get; }
    public abstract string Band { get; }
    public abstract string Channel { get; }
    public abstract string Encryption { get; }
    public abstract string Instance { get; }
    public abstract string SwitchingBlocked { get; }
    public abstract string GuestsLoseConnection { get; }
    public abstract string CutOwnConnection { get; }
    public abstract string ReallyTurnOff { get; }
    public abstract string ReallyTurnOn { get; }
    public abstract string Cancel { get; }
    public abstract string TurnOff { get; }
    public abstract string SwitchOn { get; }
    public abstract string WlanToggled(string network, bool nowOn);
}

public sealed partial class StringsDe
{
    public override string RouterSubOverview => "Übersicht";
    public override string RouterSubDevices => "Geräte";
    public override string RouterSubWlan => "WLAN";
    public override string RouterSubPortMappings => "Freigaben";
    public override string RouterSubIpv4 => "IPv4";
    public override string RouterSubEvents => "Ereignisse";
    public override string RouterSubCatalogue => "Katalog";
    public override string ReadOnly => "nur lesen";
    public override string NoRouterAccount => "Kein Router-Konto hinterlegt.";
    public override string RouterAccountWhatFor =>
        "Mit einem Router-Konto liest Auspex den Router aus und kann ihn "
        + "einstellen — Geräte, Funknetze, Portfreigaben, Ereignisse.";
    public override string StoreAccount => "Konto hinterlegen";

    public override string RouterIntro =>
        "Auspex liest beim Verbinden die Gerätebeschreibung des Routers und leitet "
        + "daraus ab, was er kann. Kein gepflegter Katalog — was die Firmware "
        + "hergibt, steht hier.";
    public override string Loading => "Wird gelesen …";
    public override string RouterUnreachable(string reason) =>
        $"Router nicht erreichbar: {reason}";
    public override string Services => "Dienste";
    public override string Actions => "Aktionen";
    public override string OfWhichChanging => "davon verändernd";
    public override string Events => "Ereignisse";
    public override string ViewEntries(int count) =>
        $"{count:N0} Einträge ansehen";
    public override string View => "ansehen";
    public override string Counting => " · wird gezählt …";
    public override string CatalogueIncomplete => "Der Katalog ist unvollständig.";
    public override string CatalogueMissing(int fehlend, int total) =>
        $" {fehlend} von {total} Diensten konnten nicht gelesen werden.";
    public override string CatalogueWhy =>
        "Die Fritz!Box drosselt, wenn ihre knapp 40 Beschreibungsdateien schnell "
        + "hintereinander abgerufen werden. Auspex versucht es dreimal je Datei; "
        + "was dann fehlt, steht hier. Der Katalog wird in zwei Minuten erneut "
        + "gelesen — meist ist er beim nächsten Aufruf vollständig.";
    public override string ReadOnlyOn => "Nur-Lesen ist eingeschaltet.";
    public override string ReadOnlyExplained =>
        " Der vollständige Katalog ist sichtbar, aber verändernde Aktionen sind "
        + "gesperrt. Abschalten über Router:ReadOnly.";
    public override string ViewAllActions => "Alle Aktionen ansehen";
    public override string ColumnService => "Dienst";
    public override string ColumnChanging => "verändernd";

    public override string TitleRouterDevices => "Geräte am Router";
    public override string RouterDevicesIntro =>
        "Die Inventur des Routers, mit der MAC als stabiler Kennung. Anders als die "
        + "Quell-IP im Query-Log überlebt sie eine neue DHCP-Vergabe — das ist die "
        + "Grundlage, auf der ein Rollenmodell später aufsetzen kann.";
    public override string DeviceTally(int total, int online) =>
        $"{total} Geräte, {online} online";
    public override string OnlyConnected => "nur verbundene";
    public override string NoDevicesReported => "Keine Geräte gemeldet.";
    public override string ColumnDevice => "Gerät";
    public override string ColumnIp => "IP";
    public override string ColumnMac => "MAC";
    public override string ColumnLink => "Anschluss";
    public override string ColumnInternet => "Internet";
    public override string RandomMac => "zufällige MAC";
    public override string RandomMacTitle =>
        "Das Gerät würfelt seine MAC pro WLAN. Nach 'Netzwerk vergessen' "
        + "erscheint es als neues Gerät.";
    public override string Online => "online";
    public override string Offline => "offline";
    public override string BlockDevice => "sperren";
    public override string CheckDevice => "prüfen";
    public override string DevicesWithoutAccount =>
        "Diese Liste kommt ohne Router-Konto — eine Fritz!Box gibt sie offen heraus. "
        + "Um den Internetzugang eines Geräts zu schalten, braucht es ein Konto unter "
        + "Router:User und Router:Password.";

    public override string TitleWlan => "Funknetze";
    public override string WlanIntro =>
        "Die Fritz!Box führt mehrere Funknetze nebeneinander — Frequenzbänder und "
        + "Gastnetz. Welches welches ist, steht nirgends geschrieben; Auspex liest "
        + "es aus, statt es zu raten.";
    public override string NotRead => "Nicht gelesen.";
    public override string NoWirelessNetworks => "Der Router meldet keine Funknetze.";
    public override string Unnamed => "(ohne Namen)";
    public override string Guest => "Gast";
    public override string On => "an";
    public override string Off => "aus";
    public override string Kind => "Art";
    public override string GuestNetwork => "Gastnetz";
    public override string HomeNetwork => "Heimnetz";
    public override string Band => "Band";
    public override string Channel => "Kanal";
    public override string Encryption => "Verschlüsselung";
    public override string Instance => "Instanz";
    public override string SwitchingBlocked =>
        "Schalten ist gesperrt, solange nur gelesen wird.";
    public override string GuestsLoseConnection =>
        "Gäste verlieren damit sofort ihre Verbindung.";
    public override string CutOwnConnection =>
        "Wenn dein Rechner in genau diesem Netz hängt, kappst du damit deine eigene "
        + "Verbindung zu Auspex — und kommst an diesen Knopf nicht mehr heran.";
    public override string ReallyTurnOff => "Wirklich abschalten";
    public override string ReallyTurnOn => "Wirklich einschalten";
    public override string Cancel => "Abbrechen";
    public override string TurnOff => "Abschalten";
    public override string SwitchOn => "Einschalten";
    public override string WlanToggled(string network, bool nowOn) =>
        $"{network} ist jetzt {(nowOn ? "an" : "aus")}.";
}

public sealed partial class StringsEn
{
    public override string RouterSubOverview => "Overview";
    public override string RouterSubDevices => "Devices";
    public override string RouterSubWlan => "Wi-Fi";
    public override string RouterSubPortMappings => "Port forwards";
    public override string RouterSubIpv4 => "IPv4";
    public override string RouterSubEvents => "Events";
    public override string RouterSubCatalogue => "Catalogue";
    public override string ReadOnly => "read only";
    public override string NoRouterAccount => "No router account on file.";
    public override string RouterAccountWhatFor =>
        "With a router account, Auspex reads the router and can configure it — "
        + "devices, wireless networks, port forwards, events.";
    public override string StoreAccount => "Add an account";

    public override string RouterIntro =>
        "On connecting, Auspex reads the router's device description and works out "
        + "what it can do. No curated catalogue — what the firmware offers is what "
        + "stands here.";
    public override string Loading => "Reading …";
    public override string RouterUnreachable(string reason) =>
        $"Router unreachable: {reason}";
    public override string Services => "Services";
    public override string Actions => "Actions";
    public override string OfWhichChanging => "of those, changing";
    public override string Events => "Events";
    public override string ViewEntries(int count) => $"View {count:N0} entries";
    public override string View => "view";
    public override string Counting => " · counting …";
    public override string CatalogueIncomplete => "The catalogue is incomplete.";
    public override string CatalogueMissing(int fehlend, int total) =>
        $" {fehlend} of {total} services could not be read.";
    public override string CatalogueWhy =>
        "The Fritz!Box throttles when its roughly 40 description files are fetched "
        + "in quick succession. Auspex retries each file three times; whatever is "
        + "still missing is listed here. The catalogue is read again in two minutes "
        + "— usually it is complete on the next visit.";
    public override string ReadOnlyOn => "Read-only is on.";
    public override string ReadOnlyExplained =>
        " The full catalogue is visible, but changing actions are locked. Turn it "
        + "off with Router:ReadOnly.";
    public override string ViewAllActions => "View all actions";
    public override string ColumnService => "Service";
    public override string ColumnChanging => "changing";

    public override string TitleRouterDevices => "Devices on the router";
    public override string RouterDevicesIntro =>
        "The router's own inventory, keyed on the MAC. Unlike the source IP in the "
        + "query log, it survives a fresh DHCP lease — which is what a role model "
        + "can later build on.";
    public override string DeviceTally(int total, int online) =>
        $"{total} devices, {online} online";
    public override string OnlyConnected => "connected only";
    public override string NoDevicesReported => "No devices reported.";
    public override string ColumnDevice => "Device";
    public override string ColumnIp => "IP";
    public override string ColumnMac => "MAC";
    public override string ColumnLink => "Link";
    public override string ColumnInternet => "Internet";
    public override string RandomMac => "random MAC";
    public override string RandomMacTitle =>
        "This device randomises its MAC per network. After 'forget network' it "
        + "shows up as a new device.";
    public override string Online => "online";
    public override string Offline => "offline";
    public override string BlockDevice => "block";
    public override string CheckDevice => "check";
    public override string DevicesWithoutAccount =>
        "This list needs no router account — a Fritz!Box hands it out openly. To "
        + "switch a device's internet access, an account under Router:User and "
        + "Router:Password is required.";

    public override string TitleWlan => "Wireless networks";
    public override string WlanIntro =>
        "A Fritz!Box runs several wireless networks side by side — frequency bands "
        + "and a guest network. Which is which is written down nowhere; Auspex reads "
        + "it out rather than guessing.";
    public override string NotRead => "Not read.";
    public override string NoWirelessNetworks => "The router reports no wireless networks.";
    public override string Unnamed => "(unnamed)";
    public override string Guest => "Guest";
    public override string On => "on";
    public override string Off => "off";
    public override string Kind => "Kind";
    public override string GuestNetwork => "Guest network";
    public override string HomeNetwork => "Home network";
    public override string Band => "Band";
    public override string Channel => "Channel";
    public override string Encryption => "Encryption";
    public override string Instance => "Instance";
    public override string SwitchingBlocked =>
        "Switching is locked while read-only is on.";
    public override string GuestsLoseConnection =>
        "Guests lose their connection immediately.";
    public override string CutOwnConnection =>
        "If your machine is on this very network, you are cutting your own "
        + "connection to Auspex — and this button goes out of reach with it.";
    public override string ReallyTurnOff => "Yes, switch off";
    public override string ReallyTurnOn => "Yes, switch on";
    public override string Cancel => "Cancel";
    public override string TurnOff => "Switch off";
    public override string SwitchOn => "Switch on";
    public override string WlanToggled(string network, bool nowOn) =>
        $"{network} is now {(nowOn ? "on" : "off")}.";
}
