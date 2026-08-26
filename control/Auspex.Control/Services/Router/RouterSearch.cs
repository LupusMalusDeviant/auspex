namespace Auspex.Control.Services.Router;

/// <summary>
/// Finds actions by the way people ask for them in German.
///
/// The catalogue's real problem is not its size but its language: the
/// actions are called <c>X_AVM-DE_SetFriendlyNameByMAC</c> and
/// <c>DisallowWANAccessByIP</c>, while people search for "Gerätename" and
/// "sperren". A full-text search over the English names then finds nothing,
/// however clever it is.
///
/// Hence a maintained mapping of German terms rather than a vector search.
/// An embedding would have to place 468 short, cryptic identifiers into a
/// semantic space where they carry hardly any context — that would need a
/// model in the house or a service outside it, and both would be more
/// expensive and worse for this problem than thirty lines of word list. If
/// the catalogue one day carried descriptive sentences rather than just
/// identifiers, the arithmetic would look different.
/// </summary>
public static class RouterSearch
{
    /// <summary>
    /// German term → parts that have to appear in the identifier.
    /// Deliberately trimmed to word stems, so "sperren", "gesperrt" and
    /// "Sperre" all hit the same row.
    /// </summary>
    private static readonly Dictionary<string, string[]> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wlan"] = ["WLANConfiguration"],
        ["funk"] = ["WLANConfiguration"],
        ["gast"] = ["WLANConfiguration", "Guest"],
        ["kanal"] = ["Channel"],
        ["schlüssel"] = ["KeyPassphrase", "Security"],
        ["passwort"] = ["Password", "KeyPassphrase"],
        ["kennwort"] = ["Password", "KeyPassphrase"],

        ["gerät"] = ["Host"],
        ["geräte"] = ["Host"],
        ["rechner"] = ["Host"],
        ["name"] = ["FriendlyName", "HostName"],
        ["umbenennen"] = ["SetFriendlyName", "SetHostName"],
        ["mac"] = ["MACAddress"],

        ["sperren"] = ["Disallow", "HostFilter"],
        ["sperre"] = ["Disallow", "HostFilter"],
        ["blockieren"] = ["Disallow", "HostFilter"],
        ["freigeben"] = ["Disallow", "HostFilter"],
        ["kindersicherung"] = ["HostFilter", "Ticket"],
        ["internetzugang"] = ["WANAccess", "Disallow"],

        ["portfreigabe"] = ["PortMapping"],
        ["freigabe"] = ["PortMapping"],
        ["port"] = ["PortMapping", "Port"],
        ["weiterleitung"] = ["PortMapping"],

        ["internet"] = ["WANIPConnection", "WANPPPConnection", "ExternalIPAddress"],
        ["verbindung"] = ["Connection"],
        ["neu verbinden"] = ["ForceTermination", "RequestConnection"],
        ["ip"] = ["IPAddress"],
        ["dns"] = ["DNSServer"],

        ["dhcp"] = ["DHCP"],
        ["adressbereich"] = ["AddressRange"],
        ["netzwerk"] = ["LANHostConfig", "IPInterface"],

        ["neustart"] = ["Reboot"],
        ["sicherung"] = ["ConfigFile"],
        ["protokoll"] = ["DeviceLog"],
        ["log"] = ["DeviceLog"],
        ["seriennummer"] = ["SerialNumber", "DeviceInfo"],
        ["firmware"] = ["SoftwareVersion", "DeviceInfo"],
        ["uhrzeit"] = ["Time", "NTP"],

        ["telefon"] = ["VoIP", "OnTel", "Dect"],
        ["anruf"] = ["CallList", "OnTel"],
        ["anrufbeantworter"] = ["TAM"],
        ["telefonbuch"] = ["Phonebook"],

        ["steckdose"] = ["Homeauto"],
        ["smarthome"] = ["Homeauto"],
        ["schalten"] = ["Homeauto", "SetSwitch"],

        ["aufwecken"] = ["WakeOnLAN"],
        ["wol"] = ["WakeOnLAN"],
        ["speicher"] = ["Storage"],
        ["nas"] = ["Storage"],

        // English terms are in the SAME list, not in a second one. The search
        // is not in a language but for a thing: whoever types "block" means
        // the same as whoever types "sperren", and the catalogue behind it is
        // labelled in English anyway. Two separate lists would only have
        // meant the search getting worse as soon as somebody switched the
        // interface language.
        ["wifi"] = ["WLANConfiguration"],
        ["wireless"] = ["WLANConfiguration"],
        ["guest"] = ["WLANConfiguration", "Guest"],
        ["channel"] = ["Channel"],
        ["key"] = ["KeyPassphrase", "Security"],
        ["device"] = ["Host"],
        ["host"] = ["Host"],
        ["rename"] = ["SetFriendlyName", "SetHostName"],
        ["block"] = ["Disallow", "HostFilter"],
        ["unblock"] = ["Disallow", "HostFilter"],
        ["allow"] = ["Disallow", "HostFilter"],
        ["parental"] = ["HostFilter", "Ticket"],
        ["forward"] = ["PortMapping"],
        ["forwarding"] = ["PortMapping"],
        ["mapping"] = ["PortMapping"],
        ["connection"] = ["Connection"],
        ["reconnect"] = ["ForceTermination", "RequestConnection"],
        ["reboot"] = ["Reboot"],
        ["restart"] = ["Reboot"],
        ["backup"] = ["ConfigFile"],
        ["serial"] = ["SerialNumber", "DeviceInfo"],
        ["time"] = ["Time", "NTP"],
        ["clock"] = ["Time", "NTP"],
        ["phone"] = ["VoIP", "OnTel", "Dect"],
        ["call"] = ["CallList", "OnTel"],
        ["answering"] = ["TAM"],
        ["phonebook"] = ["Phonebook"],
        ["socket"] = ["Homeauto"],
        ["switch"] = ["Homeauto", "SetSwitch"],
        ["wake"] = ["WakeOnLAN"],
        ["storage"] = ["Storage"],
        ["network"] = ["LANHostConfig", "IPInterface"],
        ["range"] = ["AddressRange"],
    };

    /// <summary>
    /// Whether service and action match the query. It hits when the text
    /// appears directly in the identifier or when a German term points at it
    /// — both, so that whoever types <c>SetEnable</c> finds it too.
    /// </summary>
    public static bool Matches(RouterServiceInfo service, RouterAction action, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        foreach (var word in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TrifftEinzeln(service, action, word.Trim()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TrifftEinzeln(RouterServiceInfo service, RouterAction action, string word)
    {
        if (action.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
            || service.Name.Contains(word, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Stem match: "sperr" finds "sperren" and "gesperrt".
        foreach (var (begriff, parts) in Words)
        {
            var passt = begriff.StartsWith(word, StringComparison.OrdinalIgnoreCase)
                || word.StartsWith(begriff, StringComparison.OrdinalIgnoreCase);
            if (!passt)
            {
                continue;
            }

            foreach (var chunk in parts)
            {
                if (action.Name.Contains(chunk, StringComparison.OrdinalIgnoreCase)
                    || service.Name.Contains(chunk, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A rough classification of a service into a subject area, so 39
    /// services do not sit there as a list of 39 things.
    /// </summary>
    /// <remarks>
    /// What comes back is a KEY, not display text. This used to return
    /// "Heimnetz" - and the same string was at once the value of the filter
    /// dropdown, the grouping key and the input to
    /// <see cref="AreaRank"/>. While the interface was monolingual that did
    /// not show; with the translation the grouping would have become
    /// language-dependent and the rank would have grasped at nothing. What
    /// the area is called is now said by Strings.RouterArea.
    /// </remarks>
    public static string Area(RouterServiceInfo service) => service.Name switch
    {
        "WLANConfiguration" => "funknetz",
        "Hosts" or "X_AVM-DE_HostFilter" or "LANHostConfigManagement"
            or "LANEthernetInterfaceConfig" or "LANConfigSecurity" or "Layer3Forwarding" => "heimnetz",
        var n when n.StartsWith("WAN") || n.Contains("WANFiber") || n.Contains("WANMobile") => "internet",
        "X_VoIP" or "X_AVM-DE_OnTel" or "X_AVM-DE_TAM" or "X_AVM-DE_Dect" => "telefonie",
        "X_AVM-DE_Homeauto" or "X_AVM-DE_Homeplug" => "smarthome",
        "X_AVM-DE_Storage" or "X_AVM-DE_WebDAVClient" or "X_AVM-DE_Filelinks" => "speicher",
        "DeviceInfo" or "DeviceConfig" or "Time" or "UserInterface"
            or "ManagementServer" or "X_AVM-DE_Auth" => "system",
        _ => "weiteres",
    };

    /// <summary>Order of the areas: the most common first.</summary>
    public static int BereichsRang(string range) => range switch
    {
        "heimnetz" => 0,
        "funknetz" => 1,
        "internet" => 2,
        "system" => 3,
        "smarthome" => 4,
        "telefonie" => 5,
        "speicher" => 6,
        _ => 7,
    };
}
