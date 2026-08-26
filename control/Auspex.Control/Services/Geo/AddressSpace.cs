using System.Net;
using System.Net.Sockets;

namespace Auspex.Control.Services.Geo;

/// <summary>
/// Arithmetic with IP addresses.
///
/// <para>
/// A range database maps an address to a range — "everything between
/// 8.8.8.0 and 8.8.8.255 belongs to Google". To look that up, an address has
/// to be a <em>number</em> and not a string: "9.9.9.9" &lt; "89.0.0.1" is
/// true as text and false as an address.
/// </para>
///
/// <para>
/// Both families therefore end up in <see cref="UInt128"/>, IPv4 embedded as
/// <c>::ffff:a.b.c.d</c>. That costs eight unused bytes for IPv4 and saves
/// two tables, two searches and two opportunities to get it wrong.
/// </para>
/// </summary>
public static class AddressSpace
{
    /// <summary>The address as a sortable number, big endian.</summary>
    public static UInt128 AsNumber(IPAddress address)
    {
        // IPv4 gets embedded: ::ffff:a.b.c.d. Without that, 1.2.3.4 would sit
        // numerically before every IPv6 range and the search would miss.
        var bytes = (address.AddressFamily == AddressFamily.InterNetwork
            ? address.MapToIPv6()
            : address).GetAddressBytes();

        UInt128 value = 0;
        foreach (var b in bytes)
        {
            value = (value << 8) | b;
        }
        return value;
    }

    /// <summary>Like <see cref="AsNumber(IPAddress)"/>, but from text.</summary>
    public static UInt128? AsNumber(string? address) =>
        IPAddress.TryParse(address, out var ip) ? AsNumber(ip) : null;

    /// <summary>
    /// Whether the address comes from the local network and therefore is not
    /// to be looked up anywhere.
    ///
    /// <para>
    /// Important not just to save effort: looking a private address up with
    /// a geo service returns either nothing or — worse — something.
    /// "192.168.1.1 is in California" would be a false statement about your
    /// own router.
    /// </para>
    /// </summary>
    public static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fc00::/7 are the unique local addresses, fe80::/10 the link-local
            // ones. Embedded IPv4 is checked below.
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal)
            {
                return true;
            }
            if (!ip.IsIPv4MappedToIPv6)
            {
                return false;
            }
            ip = ip.MapToIPv4();
        }

        var b = ip.GetAddressBytes();
        return b[0] switch
        {
            10 => true,                                  // 10.0.0.0/8
            127 => true,                                 // Loopback
            172 => b[1] >= 16 && b[1] <= 31,             // 172.16.0.0/12
            192 => b[1] == 168 || (b[1] == 0 && b[2] == 2), // 192.168/16, TEST-NET-1
            169 => b[1] == 254,                          // Link-local
            100 => b[1] >= 64 && b[1] <= 127,            // CGNAT, 100.64.0.0/10
            0 => true,
            _ => false,
        };
    }

    /// <summary>Like <see cref="IsPrivate(IPAddress)"/>, but from text.</summary>
    public static bool IsPrivate(string? address) =>
        IPAddress.TryParse(address, out var ip) && IsPrivate(ip);

    /// <summary>
    /// Whether the string is an address at all.
    ///
    /// <para>
    /// The resolver puts everything that was in the answer into
    /// <c>answers</c> — for a CNAME chain that means names too, for a TXT
    /// record its text. Whatever is not an address does not belong in the
    /// destinations table.
    /// </para>
    /// </summary>
    public static bool IsAddress(string? value) => IPAddress.TryParse(value, out _);

    /// <summary>
    /// Normalises an address to its canonical spelling.
    ///
    /// <para>
    /// Otherwise <c>2001:0db8::1</c> and <c>2001:db8::1</c> would stand in
    /// the table as two different destinations, and every count would be
    /// wrong by exactly that difference.
    /// </para>
    /// </summary>
    public static string? Normalise(string? address) =>
        IPAddress.TryParse(address, out var ip) ? ip.ToString() : null;
}
