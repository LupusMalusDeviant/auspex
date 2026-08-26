using Microsoft.EntityFrameworkCore;
using Auspex.Control.Data;

namespace Auspex.Control.Services.Geo;

/// <summary>One domain a program talked to, and how often.</summary>
public sealed record ProgramDomain(string Domain, long Connections, int Addresses);

/// <summary>
/// One program on one device, with the domains behind the addresses it
/// reached.
/// </summary>
public sealed record ProgramProfile(
    string Client,
    string? Device,
    string Process,
    long Connections,
    IReadOnlyList<ProgramDomain> Domains,
    int UnexplainedAddresses);

/// <summary>
/// Which program talks to which domain.
///
/// <para>
/// Auspex says "this device asked for X" and, separately, "this program
/// connected to Y". Neither half answers the question people actually have.
/// Joined over the address they do, and the result is a statement otherwise
/// reserved for commercial tooling: Chrome talked to forty trackers, the
/// vacuum talked to three endpoints abroad.
/// </para>
/// <para>
/// The join is the resolver's own record of which name produced which
/// address. Both sides are written through <see cref="AddressSpace.Normalise"/>,
/// so they are comparable — without that guarantee this would silently match
/// nothing for IPv6.
/// </para>
/// <para>
/// <b>What it cannot say.</b> The sensor runs on Windows and reads the TCP
/// table, so phones are absent and QUIC is invisible. An address may carry
/// several names, and then the program is credited with all of them — the
/// connection table records where it went, not what it asked for. And
/// addresses no lookup explains are counted separately rather than dropped:
/// that number is the interesting one, because it is the traffic that went
/// around the resolver.
/// </para>
/// </summary>
public sealed class ProgramService(AnalyticsDbContext db)
{
    public async Task<IReadOnlyList<ProgramProfile>> ForDeviceAsync(
        string client, DateTime sinceUtc, CancellationToken ct = default)
    {
        var connections = await db.Connections
            .Where(c => c.Client == client && c.LastUtc >= sinceUtc)
            .Select(c => new { c.Client, c.Device, c.Process, c.Destination, c.Count })
            .ToListAsync(ct);

        if (connections.Count == 0) return [];

        // One lookup for every address in play, rather than one per
        // connection. At a few thousand connections the second shape is the
        // difference between a page and a timeout.
        var addresses = connections.Select(c => c.Destination).Distinct().ToList();
        var byAddress = (await db.Resolutions
                .Where(r => addresses.Contains(r.Ip))
                .Select(r => new { r.Ip, r.Domain })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(r => r.Ip)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Domain).Distinct().ToList());

        var profiles = new List<ProgramProfile>();
        foreach (var group in connections.GroupBy(c => c.Process))
        {
            var perDomain = new Dictionary<string, (long Connections, HashSet<string> Addresses)>();
            var unexplained = new HashSet<string>();

            foreach (var c in group)
            {
                if (!byAddress.TryGetValue(c.Destination, out var domains) || domains.Count == 0)
                {
                    // Not dropped: this is the number worth looking at — but
                    // only for addresses out on the internet. Traffic inside
                    // the network never used public DNS, so the absence of a
                    // lookup says nothing about it.
                    //
                    // Found by reading real data: two of five "unexplained"
                    // addresses on a live installation were the machine's own
                    // ULA, talking to Auspex itself. Counting those would put
                    // a red mark next to a browser for doing nothing wrong,
                    // and a mark that cries wolf gets ignored.
                    if (!AddressSpace.IsPrivate(c.Destination))
                    {
                        unexplained.Add(c.Destination);
                    }
                    continue;
                }
                foreach (var domain in domains)
                {
                    var seen = perDomain.TryGetValue(domain, out var e)
                        ? e
                        : (0L, new HashSet<string>());
                    seen.Item2.Add(c.Destination);
                    perDomain[domain] = (seen.Item1 + c.Count, seen.Item2);
                }
            }

            profiles.Add(new ProgramProfile(
                Client: client,
                Device: group.Select(x => x.Device).FirstOrDefault(d => !string.IsNullOrEmpty(d)),
                Process: group.Key,
                Connections: group.Sum(x => x.Count),
                Domains: [.. perDomain
                    .Select(kv => new ProgramDomain(kv.Key, kv.Value.Connections, kv.Value.Addresses.Count))
                    .OrderByDescending(d => d.Connections)],
                UnexplainedAddresses: unexplained.Count));
        }

        return [.. profiles.OrderByDescending(p => p.Connections)];
    }

    /// <summary>Devices the sensor has reported for, most recent first.</summary>
    public async Task<IReadOnlyList<(string Client, string? Device)>> DevicesAsync(
        DateTime sinceUtc, CancellationToken ct = default)
    {
        var rows = await db.Connections
            .Where(c => c.LastUtc >= sinceUtc)
            .GroupBy(c => c.Client)
            .Select(g => new
            {
                Client = g.Key,
                Device = g.Max(x => x.Device),
                Last = g.Max(x => x.LastUtc),
            })
            .OrderByDescending(x => x.Last)
            .ToListAsync(ct);

        return [.. rows.Select(r => (r.Client, r.Device))];
    }
}
