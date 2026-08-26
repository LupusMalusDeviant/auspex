namespace Auspex.Control.Services.Localization;

// IPv4 on the home network — the page where you can take name resolution
// away from the whole house. There is a correspondingly large amount of
// prose here: every warning says what happens afterwards, not merely that
// something happens.

public abstract partial class Strings
{
    public abstract string TitleIpv4 { get; }
    public abstract string Ipv4Intro { get; }
    public abstract string Ipv4Warning { get; }

    public abstract string CurrentValues { get; }
    public abstract string NotReadable { get; }
    public abstract string LocalDns { get; }
    public abstract string TheBoxItself { get; }
    public abstract string BoxAddress { get; }
    public abstract string SubnetMask { get; }
    public abstract string DhcpServer { get; }
    public abstract string DhcpRange { get; }
    public abstract string LeaseTime { get; }
    /// <param name="tage">Wie die Box es liefert: als Zeichenkette.</param>
    public abstract string Days(string days);
    public abstract string FieldsUnchanged(int count);

    public abstract string ChangeDns { get; }
    public abstract string NewAddress { get; }
    public abstract string UseAuspex(string address);
    public abstract string BackToTheBox(string address);
    public abstract string UnreachableWarning(string address);
    public abstract string SomeoneAnswersThere(string address);
    public abstract string LastWarning(string address);
    public abstract string YesSetItNow { get; }
    public abstract string CheckReachability { get; }
    public abstract string Change { get; }
    public abstract string AlreadySet { get; }
    public abstract string ReadOnlyNoDns { get; }
    public abstract string Ipv4NotRead { get; }
    public abstract string Unreachable(string reason);
}

public sealed partial class StringsDe
{
    public override string TitleIpv4 => "IPv4 im Heimnetz";
    public override string Ipv4Intro =>
        "Der lokale DNS-Server ist der, den die Fritz!Box per DHCP an alle Geräte "
        + "verteilt. Steht er auf Auspex, laufen die Anfragen des ganzen Hauses "
        + "durch den Filter — und kommen einzeln an, nicht gesammelt unter der "
        + "Adresse der Box.";
    public override string Ipv4Warning =>
        "Diese Seite spricht nicht über TR-064 mit dem Router, sondern über seine "
        + "Weboberfläche. TR-064 kann den lokalen DNS-Server nicht setzen — über "
        + "beide Gerätebeschreibungen hinweg gibt es dafür keine einzige "
        + "schreibende Aktion. Die Weboberfläche ist eine undokumentierte "
        + "Schnittstelle: sie kann sich mit einer Firmware ändern. Passiert das, "
        + "sagt Auspex es und rührt nichts an.";

    public override string CurrentValues => "Aktuelle Werte";
    public override string NotReadable => "Nicht lesbar.";
    public override string LocalDns => "Lokaler DNS-Server";
    public override string TheBoxItself => " · die Box selbst";
    public override string BoxAddress => "Adresse der Box";
    public override string SubnetMask => "Subnetzmaske";
    public override string DhcpServer => "DHCP-Server";
    public override string DhcpRange => "Vergebener Bereich";
    public override string LeaseTime => "Gültigkeit";
    public override string Days(string days) => days == "1" ? "1 Tag" : $"{days} Tage";
    public override string FieldsUnchanged(int count) =>
        $"Alle {count} Felder des Formulars werden beim Ändern unverändert "
        + "zurückgeschickt — nur die vier des DNS-Servers nicht. Was nicht "
        + "mitkommt, könnte die Box auf Vorgabe zurücksetzen, und ein "
        + "zurückgesetzter DHCP-Bereich fällt erst auf, wenn Geräte keine "
        + "Adresse mehr bekommen.";

    public override string ChangeDns => "Lokalen DNS-Server ändern";
    public override string NewAddress => "Neue Adresse";
    public override string UseAuspex(string address) => $"Auspex eintragen ({address})";
    public override string BackToTheBox(string address) => $"Zurück auf die Box ({address})";
    public override string UnreachableWarning(string address) =>
        $"Unter {address} antwortet auf Port 53 kein DNS-Server. Diese Adresse zu "
        + "verteilen nähme dem ganzen Heimnetz die Namensauflösung — auch dem "
        + "Rechner, mit dem du es zurücknehmen wolltest. Solange Auspex noch auf "
        + "einem Testport steht, ist das genau der Fall.";
    public override string SomeoneAnswersThere(string address) =>
        $"Unter {address} antwortet ein DNS-Server.";
    public override string LastWarning(string address) =>
        $"Die Fritz!Box verteilt danach {address} als DNS-Server an alle Geräte. "
        + "Antwortet dort nichts, ist im ganzen Haus keine Namensauflösung mehr "
        + "möglich, bis die Geräte ihre DHCP-Adresse erneuern oder du es hier "
        + "zurücksetzt.";
    public override string YesSetItNow => "Ja, jetzt setzen";
    public override string CheckReachability => "Erreichbarkeit prüfen";
    public override string Change => "Ändern";
    public override string AlreadySet => " · steht bereits so";
    public override string ReadOnlyNoDns =>
        "Nur-Lesen ist eingeschaltet — der DNS-Server lässt sich nicht ändern.";
    public override string Ipv4NotRead =>
        "Die IPv4-Einstellungen ließen sich nicht lesen. Entweder weist die "
        + "Weboberfläche die Anmeldung ab, oder die Seite sieht anders aus als "
        + "erwartet.";
    public override string Unreachable(string reason) => $"Nicht erreichbar: {reason}";
}

public sealed partial class StringsEn
{
    public override string TitleIpv4 => "IPv4 on the home network";
    public override string Ipv4Intro =>
        "The local DNS server is the one the Fritz!Box hands out to every device "
        + "over DHCP. Point it at Auspex and the whole house's queries run through "
        + "the filter — and arrive one by one, not lumped together under the box's "
        + "own address.";
    public override string Ipv4Warning =>
        "This page does not talk to the router over TR-064 but through its web "
        + "interface. TR-064 cannot set the local DNS server — across both device "
        + "descriptions there is not a single writing action for it. The web "
        + "interface is an undocumented one: it can change with a firmware update. "
        + "If that happens, Auspex says so and touches nothing.";

    public override string CurrentValues => "Current values";
    public override string NotReadable => "Cannot read.";
    public override string LocalDns => "Local DNS server";
    public override string TheBoxItself => " · the box itself";
    public override string BoxAddress => "Address of the box";
    public override string SubnetMask => "Subnet mask";
    public override string DhcpServer => "DHCP server";
    public override string DhcpRange => "Handed-out range";
    public override string LeaseTime => "Lease time";
    public override string Days(string days) => days == "1" ? "1 day" : $"{days} days";
    public override string FieldsUnchanged(int count) =>
        $"All {count} fields of the form are sent back unchanged on a write — all "
        + "except the four for the DNS server. Anything left out could be reset to "
        + "the box's default, and a reset DHCP range only shows up once devices "
        + "stop getting addresses.";

    public override string ChangeDns => "Change the local DNS server";
    public override string NewAddress => "New address";
    public override string UseAuspex(string address) => $"Point at Auspex ({address})";
    public override string BackToTheBox(string address) => $"Back to the box ({address})";
    public override string UnreachableWarning(string address) =>
        $"Nothing answers on port 53 at {address}. Handing that address out would "
        + "take name resolution away from the entire home network — including the "
        + "machine you would want to undo it from. While Auspex still sits on a "
        + "test port, that is exactly the situation.";
    public override string SomeoneAnswersThere(string address) =>
        $"A DNS server answers at {address}.";
    public override string LastWarning(string address) =>
        $"The Fritz!Box will then hand out {address} as the DNS server to every "
        + "device. If nothing answers there, no name resolution is possible "
        + "anywhere in the house until devices renew their DHCP lease or you set "
        + "it back here.";
    public override string YesSetItNow => "Yes, set it now";
    public override string CheckReachability => "Check it answers";
    public override string Change => "Change";
    public override string AlreadySet => " · already set to that";
    public override string ReadOnlyNoDns =>
        "Read-only is on — the DNS server cannot be changed.";
    public override string Ipv4NotRead =>
        "The IPv4 settings could not be read. Either the web interface is refusing "
        + "the sign-in, or the page looks different from what was expected.";
    public override string Unreachable(string reason) => $"Unreachable: {reason}";
}
