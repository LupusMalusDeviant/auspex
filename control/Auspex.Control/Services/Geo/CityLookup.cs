using System.IO.Compression;
using System.Net;

namespace Auspex.Control.Services.Geo;

/// <summary>Country and city for an address.</summary>
public sealed record PlaceInfo(string? Country, string? City);

/// <summary>
/// Works out the city for a batch of addresses.
///
/// <para>
/// The city file is the heavy one: 90 MB compressed, 706 MB uncompressed,
/// 7.9 million ranges. Loading it into a database would mean occupying a
/// home device with half a gigabyte of lookup data — for a value that is
/// uncertain with large providers anyway.
/// </para>
///
/// <para>
/// Instead, one pass for <em>all</em> pending addresses at once. The file is
/// read once from front to back, and for every line it checks whether any of
/// the addresses sought falls into it. With a few thousand pending addresses
/// that is one pass instead of thousands of searches — and afterwards the
/// file goes quiet again until enough has accumulated.
/// </para>
///
/// <para>
/// That is exactly why the city is filled in later rather than supplied
/// straight away: one pass per new address would be unaffordable. The
/// operator, by contrast, is there immediately — that comes from the small
/// file.
/// </para>
/// </summary>
public sealed class CityLookup(ILogger<CityLookup> log)
{
    /// <summary>
    /// Finds the places for the given addresses in one pass.
    /// </summary>
    /// <param name="path">The compressed CSV file.</param>
    /// <param name="wanted">
    /// The addresses as numbers. Sorted here, so one range search per line is
    /// enough instead of a comparison against all of them.
    /// </param>
    public Dictionary<UInt128, PlaceInfo> Lookup(
        string path, IReadOnlyCollection<UInt128> gesucht, CancellationToken ct)
    {
        var hits = new Dictionary<UInt128, PlaceInfo>();
        // How wide the range a hit came from was.
        var width = new Dictionary<UInt128, UInt128>();
        if (gesucht.Count == 0)
        {
            return hits;
        }

        // TWO lists, separated by family - and that is not an optimisation but
        // a correction.
        //
        // IPv4 is embedded as ::ffff:a.b.c.d and therefore lies numerically
        // INSIDE low IPv6 ranges. The city file carries a row from :: to
        // 1fff:ffff:... with country "ZZ" and no city; it stands behind the
        // IPv4 block. In a shared list it therefore hit every embedded IPv4
        // address and overwrote the city found moments earlier with nothing.
        //
        // Measured: of 1026 addresses, 185 kept their city and the rest stood
        // there empty. The fault was invisible because the result did not
        // look wrong - only empty.
        var v4 = gesucht.Where(a => IsEmbeddedV4(a)).Distinct().Order().ToArray();
        var v6 = gesucht.Where(a => !IsEmbeddedV4(a)).Distinct().Order().ToArray();
        var sorted = v4.Length + v6.Length;

        var begonnen = DateTime.UtcNow;
        long lines = 0;

        using var file = File.OpenRead(path);
        using var stream = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            if (++lines % 500_000 == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            var s = line.AsSpan();

            var komma1 = s.IndexOf(',');
            if (komma1 <= 0)
            {
                continue;
            }
            var rest = s[(komma1 + 1)..];
            var komma2 = rest.IndexOf(',');
            if (komma2 <= 0)
            {
                continue;
            }

            var fromRaw = s[..komma1];
            var toRaw = rest[..komma2];

            // The family is in the colon. An IPv6 row is only checked against
            // IPv6 addresses and vice versa.
            var passend = fromRaw.Contains(':') ? v6 : v4;
            if (passend.Length == 0)
            {
                continue;
            }

            if (!IPAddress.TryParse(fromRaw, out var fromIp)
                || !IPAddress.TryParse(toRaw, out var toIp))
            {
                continue;
            }

            var from = AddressSpace.AsNumber(fromIp);
            var until = AddressSpace.AsNumber(toIp);

            // The first address sought at or after the start of the range.
            var i = FirstFrom(passend, from);
            if (i >= passend.Length || passend[i] > until)
            {
                continue;
            }

            // Only now is it worth splitting the remaining fields.
            var place = PlaceFrom(rest[(komma2 + 1)..]);
            for (; i < passend.Length && passend[i] <= until; i++)
            {
                // The NARROWER range wins. The file carries wide catch-all ranges
                // alongside precise ones; whoever writes last would otherwise
                // win purely by their position in the file.
                if (!width.TryGetValue(passend[i], out var bisher) || until - from < bisher)
                {
                    width[passend[i]] = until - from;
                    hits[passend[i]] = place;
                }
            }
        }

        log.LogInformation(
            "City lookup: {Rows} ranges read, {Matched} of {Sought} addresses mapped, {Duration:F1}s",
            lines, hits.Count, sorted, (DateTime.UtcNow - begonnen).TotalSeconds);

        return hits;
    }

    /// <summary>
    /// Whether the number is an embedded IPv4 address — <c>::ffff:a.b.c.d</c>,
    /// that is, anything between 2^32·65535 and 2^48.
    /// </summary>
    internal static bool IsEmbeddedV4(UInt128 value)
    {
        var untenV4 = AddressSpace.AsNumber(IPAddress.Parse("::ffff:0.0.0.0"));
        var obenV4 = AddressSpace.AsNumber(IPAddress.Parse("::ffff:255.255.255.255"));
        return value >= untenV4 && value <= obenV4;
    }

    /// <summary>
    /// The first position holding a value greater than or equal to
    /// <paramref name="bound"/>. A binary search; without it the pass would
    /// be a cross product of eight million lines and every address sought.
    /// </summary>
    internal static int FirstFrom(UInt128[] sorted, UInt128 limit)
    {
        var links = 0;
        var rechts = sorted.Length;
        while (links < rechts)
        {
            var middle = links + ((rechts - links) >> 1);
            if (sorted[middle] < limit)
            {
                links = middle + 1;
            }
            else
            {
                rechts = middle;
            }
        }
        return links;
    }

    /// <summary>
    /// Pulls country and city out of the rest of the line.
    ///
    /// <para>
    /// The fields from here on are: continent, country, region, city,
    /// latitude, longitude. Region and city can be in quotes when they
    /// contain a comma — "South Brisbane" does not, "Washington, D.C." does.
    /// </para>
    /// </summary>
    internal static PlaceInfo PlaceFrom(ReadOnlySpan<char> rest)
    {
        Span<Range> fields = stackalloc Range[4];
        var count = Fields(rest, fields);
        if (count < 4)
        {
            return new PlaceInfo(null, null);
        }

        var country = rest[fields[1]].Trim('"').ToString();
        var city = rest[fields[3]].Trim('"').ToString();

        // "ZZ" is what the source uses for unknown.
        return new PlaceInfo(
            country is "" or "ZZ" ? null : country,
            city is "" or "-" ? null : city);
    }

    /// <summary>Splits as many fields as there is room for — quotes respected.</summary>
    private static int Fields(ReadOnlySpan<char> line, Span<Range> destination)
    {
        var count = 0;
        var start = 0;
        var inQuotes = false;

        for (var i = 0; i < line.Length && count < destination.Length; i++)
        {
            if (line[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (line[i] == ',' && !inQuotes)
            {
                destination[count++] = new Range(start, i);
                start = i + 1;
            }
        }

        if (count < destination.Length)
        {
            destination[count++] = new Range(start, line.Length);
        }
        return count;
    }
}
