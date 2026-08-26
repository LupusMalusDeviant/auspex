using Microsoft.EntityFrameworkCore;
using Auspex.Control.Data;
using Auspex.Control.Services.Geo;
using Auspex.Control.Services.Localization;

namespace Auspex.Control.Services;

/// <summary>Time window and baseline of one detection run.</summary>
public record DetectionContext(
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    DateTime BaselineStartUtc,
    bool HasBaseline)
{
    public double BaselineHours => Math.Max(1, (WindowStartUtc - BaselineStartUtc).TotalHours);

    /// <summary>
    /// The night window, in the display zone. The same query count means
    /// something different at 3am than at 3pm.
    ///
    /// <para>
    /// Deliberately the same zone as the display and not the container's:
    /// otherwise Auspex calls a finding "at night" while 05:00 stands next to
    /// it. While the container still ran on UTC, night here actually was
    /// 02:00 to 08:00 local time.
    /// </para>
    /// </summary>
    public bool IsNight => Localization.DisplayTime.ToDisplay(WindowStartUtc).Hour is >= 0 and < 6;
}

/// <summary>
/// Heuristics, not truth. Every detector states its thresholds openly and
/// supplies the numbers it rests on - a finding you cannot recompute is
/// worthless.
/// </summary>
public static class Detectors
{
    // Title, explanation and supporting figures used to be here, as finished
    // German sentences. They now live in Services/Localization/FindingTexts.cs,
    // and only the measurements stay here.
    //
    // The occasion was the translation, the reason is older: this code runs
    // every five minutes in the background, without anybody having opened a
    // page. There is no reader at that moment - and therefore no language to
    // write to them in. What stood here was the server's language.
    //
    // Label() went with the sentences: naming a device is the display's job
    // now.

    /// <summary>
    /// A domain this device has never asked for. Harmless in itself (new app,
    /// new CDN) - it becomes notable at night and on devices with an
    /// otherwise stable repertoire.
    /// </summary>
    public static async Task<List<Finding>> NewDomainAsync(
        AnalyticsDbContext db, DetectionContext ctx, CancellationToken ct)
    {
        var findings = new List<Finding>();
        if (!ctx.HasBaseline) return findings;

        var current = await db.Queries
            .Where(q => q.TimeUtc >= ctx.WindowStartUtc && q.TimeUtc < ctx.WindowEndUtc && q.Action != "error")
            .GroupBy(q => new { q.Client, q.Domain })
            .Select(g => new
            {
                g.Key.Client,
                g.Key.Domain,
                Name = g.Max(x => x.ClientName),
                Count = g.LongCount(),
            })
            .Where(x => x.Count >= 5)
            .ToListAsync(ct);

        if (current.Count == 0) return findings;

        var clients = current.Select(x => x.Client).Distinct().ToList();
        var domains = current.Select(x => x.Domain).Distinct().ToList();

        // Query only the candidate domains, not everything ever seen. Without
        // that restriction the complete list of every client-domain
        // combination in the retention period moves into memory on every run
        // — irrelevant today, a memory hog after months of real traffic that
        // starts up every five minutes. This way it stays an index lookup on
        // a handful of values.
        var known = (await db.Queries
                .Where(q => q.TimeUtc >= ctx.BaselineStartUtc && q.TimeUtc < ctx.WindowStartUtc
                            && clients.Contains(q.Client) && domains.Contains(q.Domain))
                .Select(q => new { q.Client, q.Domain })
                .Distinct()
                .ToListAsync(ct))
            .Select(x => x.Client + "|" + x.Domain)
            .ToHashSet();

        foreach (var row in current.OrderByDescending(x => x.Count).Take(10))
        {
            if (known.Contains(row.Client + "|" + row.Domain)) continue;

            findings.Add(new Finding
            {
                Detector = "neue-domain",
                Severity = ctx.IsNight ? "warn" : "info",
                Client = row.Client,
                ClientName = row.Name,
                Subject = row.Domain,
                Values = new FindingValues
                {
                    Count = row.Count,
                    BaselineDays = (ctx.WindowStartUtc - ctx.BaselineStartUtc).TotalDays,
                    Nachts = ctx.IsNight,
                }.AsJson(),
                Score = row.Count,
            });
        }
        return findings;
    }

    /// <summary>
    /// A connection to an address that no lookup ever produced — traffic that
    /// went around the resolver.
    /// </summary>
    /// <remarks>
    /// This is the one question Pi-hole and AdGuard Home cannot ask. They see
    /// what reaches them; the *absence* of a query is invisible from there.
    /// Auspex has a second, independent observer on the endpoint, and the gap
    /// between the two is the finding: the sensor saw a connection, and no
    /// resolution in the whole database explains the address.
    ///
    /// Usual causes, in descending order of how boring they are: a browser
    /// with DNS-over-HTTPS of its own, a program carrying hardcoded addresses,
    /// an app with its own resolver. All three mean the same thing for the
    /// filter — it did not get asked.
    ///
    /// Deliberately matched against *every* resolution rather than against
    /// this device's own. "No lookup anywhere produced this address" is the
    /// stronger statement and the one with fewer false alarms: a device that
    /// reuses an address a second device looked up is not going around
    /// anything.
    ///
    /// Known limits, stated here because the finding would otherwise be read
    /// as more than it is: the sensor is Windows-only and TCP-only, so this
    /// says nothing about phones and nothing about QUIC. Private destinations
    /// are excluded — traffic inside the network does not use public DNS and
    /// never did.
    /// </remarks>
    public static async Task<List<Finding>> UnexplainedConnectionAsync(
        AnalyticsDbContext db, DetectionContext ctx, CancellationToken ct)
    {
        // Both sides are written through AddressSpace.Normalise, so the join
        // is on a canonical spelling. Without that guarantee this would
        // compare "2606:4700::1111" against its expanded form and report the
        // whole internet.
        var unexplained = await db.Connections
            .Where(c => c.LastUtc >= ctx.WindowStartUtc && c.LastUtc < ctx.WindowEndUtc)
            .Where(c => !db.Resolutions.Any(r => r.Ip == c.Destination))
            .Select(c => new
            {
                c.Client,
                c.Device,
                c.Process,
                c.Destination,
                c.Count,
            })
            .ToListAsync(ct);

        if (unexplained.Count == 0) return [];

        var findings = new List<Finding>();
        var perProcess = unexplained
            // Traffic inside the network never involved public DNS, so its
            // absence says nothing.
            .Where(x => !AddressSpace.IsPrivate(x.Destination))
            .GroupBy(x => new { x.Client, x.Process })
            .Select(g => new
            {
                g.Key.Client,
                g.Key.Process,
                Device = g.Select(x => x.Device).FirstOrDefault(d => !string.IsNullOrEmpty(d)),
                Addresses = g.Select(x => x.Destination).Distinct().ToList(),
                Count = g.Sum(x => x.Count),
            })
            // One address is a hardcoded endpoint and hardly worth waking
            // anybody for. A handful of distinct ones from one program is the
            // shape of a resolver that is not ours.
            .Where(x => x.Addresses.Count >= 3)
            .OrderByDescending(x => x.Addresses.Count)
            .Take(10);

        foreach (var row in perProcess)
        {
            findings.Add(new Finding
            {
                Detector = "unerklaerte-verbindung",
                Severity = row.Addresses.Count >= 10 ? "warn" : "info",
                Client = row.Client,
                ClientName = row.Device,
                Subject = row.Process,
                Values = new FindingValues
                {
                    Count = row.Count,
                    Names = row.Addresses.Count,
                    Example = row.Addresses[0],
                }.AsJson(),
                Score = row.Addresses.Count,
            });
        }
        return findings;
    }

    /// <summary>
    /// A public name that answered with an address inside the network — the
    /// DNS-rebinding pattern.
    /// </summary>
    /// <remarks>
    /// The resolver already blocked it; this turns the block into something
    /// somebody sees. That is the whole difference to the two comparable
    /// projects: they drop the answer and say nothing, so the one event that
    /// would tell you a device is being attacked never reaches a human.
    ///
    /// Not automatically an attack, and the text says so. Some services
    /// publish internal addresses on purpose, and the ones we know of are on
    /// the resolver's allowlist. Several distinct names on one device within
    /// a window is the shape that is hard to explain innocently — that is
    /// where it goes from warning to hard finding.
    /// </remarks>
    public static async Task<List<Finding>> RebindAsync(
        AnalyticsDbContext db, DetectionContext ctx, CancellationToken ct)
    {
        var blocked = await db.Queries
            .Where(q => q.TimeUtc >= ctx.WindowStartUtc && q.TimeUtc < ctx.WindowEndUtc
                        && q.Source == "rebind")
            .GroupBy(q => new { q.Client, q.Name })
            .Select(g => new
            {
                g.Key.Client,
                g.Key.Name,
                ClientName = g.Max(x => x.ClientName),
                Address = g.Max(x => x.Rule),
                Count = g.LongCount(),
            })
            .ToListAsync(ct);

        if (blocked.Count == 0) return [];

        var namesPerClient = blocked
            .GroupBy(x => x.Client)
            .ToDictionary(g => g.Key, g => g.Count());

        var findings = new List<Finding>();
        foreach (var row in blocked.OrderByDescending(x => x.Count).Take(10))
        {
            var names = namesPerClient[row.Client];
            findings.Add(new Finding
            {
                Detector = "rebind",
                Severity = names >= 3 ? "high" : "warn",
                Client = row.Client,
                ClientName = row.ClientName,
                Subject = row.Name,
                Values = new FindingValues
                {
                    Count = row.Count,
                    Address = row.Address,
                    Names = names,
                }.AsJson(),
                Score = row.Count,
            });
        }
        return findings;
    }

    /// <summary>
    /// A high NXDOMAIN share: either a broken configuration or a piece of
    /// malware working through a domain generator.
    /// </summary>
    public static async Task<List<Finding>> NxdomainFloodAsync(
        AnalyticsDbContext db, DetectionContext ctx, CancellationToken ct)
    {
        var rows = await db.Queries
            .Where(q => q.TimeUtc >= ctx.WindowStartUtc && q.TimeUtc < ctx.WindowEndUtc)
            .GroupBy(q => q.Client)
            .Select(g => new
            {
                Client = g.Key,
                Name = g.Max(x => x.ClientName),
                Total = g.LongCount(),
                Nx = g.LongCount(x => x.Rcode == "NXDOMAIN" && x.Action != "blocked"),
                Domains = g.Select(x => x.Domain).Distinct().Count(),
            })
            .Where(x => x.Total >= 50)
            .ToListAsync(ct);

        var findings = new List<Finding>();
        foreach (var row in rows)
        {
            var ratio = (double)row.Nx / row.Total;
            if (ratio < 0.4) continue;

            findings.Add(new Finding
            {
                Detector = "nxdomain-flut",
                Severity = ratio >= 0.7 && row.Total >= 200 ? "high" : "warn",
                Client = row.Client,
                ClientName = row.Name,
                Subject = null,
                Values = new FindingValues
                {
                    Anteil = ratio,
                    Nx = row.Nx,
                    Total = row.Total,
                    Domains = row.Domains,
                }.AsJson(),
                Score = ratio * row.Total,
            });
        }
        return findings;
    }

    /// <summary>
    /// The same domain markedly more often than usual - the classic "device X
    /// asks the same telemetry domain 400 times at night".
    /// </summary>
    public static async Task<List<Finding>> RepetitionBurstAsync(
        AnalyticsDbContext db, DetectionContext ctx, CancellationToken ct)
    {
        var findings = new List<Finding>();
        if (!ctx.HasBaseline) return findings;

        var current = await db.Queries
            .Where(q => q.TimeUtc >= ctx.WindowStartUtc && q.TimeUtc < ctx.WindowEndUtc)
            .GroupBy(q => new { q.Client, q.Domain })
            .Select(g => new
            {
                g.Key.Client,
                g.Key.Domain,
                Name = g.Max(x => x.ClientName),
                Count = g.LongCount(),
            })
            .Where(x => x.Count >= 100)
            .ToListAsync(ct);

        if (current.Count == 0) return findings;

        var clients = current.Select(x => x.Client).Distinct().ToList();
        var domains = current.Select(x => x.Domain).Distinct().ToList();

        var baseline = (await db.Queries
                .Where(q => q.TimeUtc >= ctx.BaselineStartUtc && q.TimeUtc < ctx.WindowStartUtc
                            && clients.Contains(q.Client) && domains.Contains(q.Domain))
                .GroupBy(q => new { q.Client, q.Domain })
                .Select(g => new { g.Key.Client, g.Key.Domain, Count = g.LongCount() })
                .ToListAsync(ct))
            .ToDictionary(x => x.Client + "|" + x.Domain, x => x.Count / ctx.BaselineHours);

        foreach (var row in current)
        {
            var perHour = baseline.GetValueOrDefault(row.Client + "|" + row.Domain, 0);
            // Without history a spike cannot be judged; that is what the "new
            // domain" detector is for.
            if (perHour < 1) continue;

            var factor = row.Count / perHour;
            if (factor < 5) continue;

            findings.Add(new Finding
            {
                Detector = "wiederholungssturm",
                Severity = factor >= 20 ? "high" : "warn",
                Client = row.Client,
                ClientName = row.Name,
                Subject = row.Domain,
                Values = new FindingValues
                {
                    Count = row.Count,
                    PerHour = perHour,
                    Faktor = factor,
                    BaselineDays = ctx.BaselineHours / 24,
                    Nachts = ctx.IsNight,
                }.AsJson(),
                Score = factor,
            });
        }
        return findings;
    }

    /// <summary>
    /// Viele verschiedene, sehr lange Namen unter einer Domain. So sieht
    /// DNS-Tunneling aus: die Nutzdaten stecken im Namen selbst.
    /// </summary>
    public static async Task<List<Finding>> TunnelingAsync(
        AnalyticsDbContext db, DetectionContext ctx, CancellationToken ct)
    {
        var candidates = await db.Queries
            .Where(q => q.TimeUtc >= ctx.WindowStartUtc && q.TimeUtc < ctx.WindowEndUtc)
            .GroupBy(q => new { q.Client, q.Domain })
            .Select(g => new
            {
                g.Key.Client,
                g.Key.Domain,
                Name = g.Max(x => x.ClientName),
                MaxLabel = g.Max(x => x.LongestLabel),
                Total = g.LongCount(),
            })
            .Where(x => x.MaxLabel >= 30 && x.Total >= 50)
            .ToListAsync(ct);

        var findings = new List<Finding>();
        foreach (var row in candidates)
        {
            // Only now ask the more expensive question about distinct names, and
            // only for the few suspects.
            var distinctNames = await db.Queries
                .Where(q => q.TimeUtc >= ctx.WindowStartUtc && q.TimeUtc < ctx.WindowEndUtc
                            && q.Client == row.Client && q.Domain == row.Domain)
                .Select(q => q.Name)
                .Distinct()
                .CountAsync(ct);

            if (distinctNames < 50) continue;

            findings.Add(new Finding
            {
                Detector = "tunneling-verdacht",
                Severity = "high",
                Client = row.Client,
                ClientName = row.Name,
                Subject = row.Domain,
                Values = new FindingValues
                {
                    Names = distinctNames,
                    Total = row.Total,
                    MaxLabel = row.MaxLabel,
                }.AsJson(),
                Score = distinctNames,
            });
        }
        return findings;
    }

    /// <summary>
    /// A device asks the same blocked domain over and over in quick
    /// succession. That is not an advert being fetched but a retry loop —
    /// the signature of an app that is not working right now.
    ///
    /// The most common reason people switch DNS filters off again is exactly
    /// this: something breaks and nobody knows why. Being able to name the
    /// cause and supply the exception with it turns that around.
    /// </summary>
    public static async Task<List<Finding>> FalsePositiveAsync(
        AnalyticsDbContext db, DetectionContext ctx, CancellationToken ct)
    {
        var rows = await db.Queries
            .Where(q => q.TimeUtc >= ctx.WindowStartUtc && q.TimeUtc < ctx.WindowEndUtc
                        && q.Action == "blocked")
            .GroupBy(q => new { q.Client, q.Domain })
            .Select(g => new
            {
                g.Key.Client,
                g.Key.Domain,
                Name = g.Max(x => x.ClientName),
                Count = g.LongCount(),
                First = g.Min(x => x.TimeUtc),
                Last = g.Max(x => x.TimeUtc),
                Rule = g.Max(x => x.Rule),
                List = g.Max(x => x.List),
                Names = g.Select(x => x.Name).Distinct().Count(),
                SampleName = g.Min(x => x.Name),
            })
            .Where(x => x.Count >= 8)
            .ToListAsync(ct);

        var findings = new List<Finding>();
        if (rows.Count == 0) return findings;

        // What has been doing the same thing for days is no longer a false
        // alarm.
        //
        // This detector's assumption is: a retry loop means something is
        // broken right now, and an exception fixes it. For telemetry that is
        // not true — it asks on a fixed cadence for all eternity, nobody
        // wants the exception, and the finding comes back every hour. In
        // practice this detector produced 123 of 131 findings and buried the
        // other five.
        //
        // Whatever has already been reported on several days is a standing
        // state. "steady talker" covers that, reporting once a day.
        var limit = ctx.WindowEndUtc.AddDays(-7);
        var history = await db.Findings
            .Where(f => f.Detector == "fehlalarm-verdacht"
                        && f.DetectedUtc >= limit
                        && f.DetectedUtc < ctx.WindowStartUtc)
            .Select(f => new { f.Client, f.Subject, f.DetectedUtc })
            .ToListAsync(ct);

        var daysPerPair = history
            .GroupBy(f => f.Client + "|" + f.Subject)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DetectedUtc.Date).Distinct().Count());

        foreach (var row in rows)
        {
            if (daysPerPair.GetValueOrDefault(row.Client + "|" + row.Domain, 0) >= 2)
            {
                continue;
            }

            var span = row.Last - row.First;

            // The difference between "adverts while browsing" and "app is
            // stuck": the density. Eight requests spread over an hour are
            // normal, eight in five minutes are a retry.
            if (span > TimeSpan.FromMinutes(5)) continue;

            var perMinute = row.Count / Math.Max(1, span.TotalMinutes);
            var distinct = row.Count >= 20 && span <= TimeSpan.FromMinutes(2);

            findings.Add(new Finding
            {
                Detector = "fehlalarm-verdacht",
                Severity = distinct ? "warn" : "info",
                Client = row.Client,
                ClientName = row.Name,
                Subject = row.Domain,
                Values = new FindingValues
                {
                    Count = row.Count,
                    SpanneSek = span.TotalSeconds,
                    ProMinute = perMinute,
                    Names = row.Names,
                    Rule = row.Rule,
                    ListName = row.List,
                }.AsJson(),
                // As narrow as possible: if only one name was affected, only
                // that one gets allowed. An exception on the whole
                // registrable domain would open the entire provider because
                // of a single telemetry host.
                Suggestion = $"@@||{(row.Names == 1 ? row.SampleName : row.Domain)}^",
                Score = perMinute,
            });
        }
        return findings;
    }

    /// <summary>
    /// Several devices ask for the same domain, new to all of them, within a
    /// short interval. Taken individually each of those queries is
    /// unremarkable — it is the synchrony that makes them interesting: a
    /// rollout wave, a firmware update with a new destination, or something
    /// spreading.
    ///
    /// This is exactly the view tools that analyse per device do not have.
    /// </summary>
    public static async Task<List<Finding>> CorrelationAsync(
        AnalyticsDbContext db, DetectionContext ctx, CancellationToken ct)
    {
        var findings = new List<Finding>();
        if (!ctx.HasBaseline) return findings;

        // First the domains in the window, then the devices for them
        // separately: "distinct clients per domain" inside a grouping is
        // something EF cannot translate to SQL.
        var inWindow = await db.Queries
            .Where(q => q.TimeUtc >= ctx.WindowStartUtc && q.TimeUtc < ctx.WindowEndUtc
                        && q.Action != "error")
            .GroupBy(q => q.Domain)
            .Select(g => new { Domain = g.Key, Count = g.LongCount() })
            .ToListAsync(ct);

        if (inWindow.Count == 0) return findings;

        var candidateDomains = inWindow.Select(x => x.Domain).ToList();
        var perDomainAndClient = await db.Queries
            .Where(q => q.TimeUtc >= ctx.WindowStartUtc && q.TimeUtc < ctx.WindowEndUtc
                        && q.Action != "error" && candidateDomains.Contains(q.Domain))
            .GroupBy(q => new { q.Domain, q.Client })
            .Select(g => new
            {
                g.Key.Domain,
                g.Key.Client,
                Name = g.Max(x => x.ClientName),
                Erstkontakt = g.Min(x => x.TimeUtc),
            })
            .ToListAsync(ct);

        var withSeveralDevices = perDomainAndClient
            .GroupBy(x => x.Domain)
            .Where(g => g.Count() >= 3)
            .ToList();

        if (withSeveralDevices.Count == 0) return findings;

        // Only domains that are new to ALL of them. One a device already knew
        // is not synchrony, it is everyday traffic.
        var toCheck = withSeveralDevices.Select(g => g.Key).ToList();
        var known = (await db.Queries
                .Where(q => q.TimeUtc >= ctx.BaselineStartUtc && q.TimeUtc < ctx.WindowStartUtc
                            && toCheck.Contains(q.Domain))
                .Select(q => q.Domain)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        foreach (var group in withSeveralDevices)
        {
            if (known.Contains(group.Key)) continue;

            var first = group.Min(x => x.Erstkontakt);
            var last = group.Max(x => x.Erstkontakt);
            var span = last - first;

            // Spread over an hour it is no longer synchrony.
            if (span > TimeSpan.FromMinutes(15)) continue;

            var devices = group.OrderBy(x => x.Erstkontakt).ToList();
            var count = devices.Count;

            findings.Add(new Finding
            {
                Detector = "gleichlauf",
                Severity = count >= 5 || span <= TimeSpan.FromMinutes(2) ? "warn" : "info",
                Client = Kurzliste(devices.Select(x => x.Client)),
                ClientName = Kurzliste(devices.Select(x => x.Name).Where(n => !string.IsNullOrEmpty(n)!)!),
                Subject = group.Key,
                Values = new FindingValues
                {
                    Devices = count,
                    SpanneSek = span.TotalSeconds,
                    First = first,
                    Last = last,
                }.AsJson(),
                Score = count,
            });
        }
        return findings;
    }

    /// <summary>
    /// A device asking a blocked address evenly and continuously — not in a
    /// burst but around the clock.
    ///
    /// The detector exists because of a gap: "repetition burst" compares
    /// against a device's own history and only fires on a spike. What has
    /// been equally loud for days has no spike — factor one — and stays
    /// invisible, even though it causes most of the load. A storm that was
    /// always there is still a storm.
    ///
    /// Deliberately without a suggestion: an exception would be the wrong
    /// answer here. Whoever blocks telemetry wants it blocked; the report is
    /// meant to show that it costs queries, not to open it.
    /// </summary>
    public static async Task<List<Finding>> SteadyTalkerAsync(
        AnalyticsDbContext db, DetectionContext ctx, CancellationToken ct)
    {
        var findings = new List<Finding>();
        if (!ctx.HasBaseline) return findings;

        var current = await db.Queries
            .Where(q => q.TimeUtc >= ctx.WindowStartUtc && q.TimeUtc < ctx.WindowEndUtc
                        && q.Action == "blocked")
            .GroupBy(q => new { q.Client, q.Domain })
            .Select(g => new
            {
                g.Key.Client,
                g.Key.Domain,
                Name = g.Max(x => x.ClientName),
                Count = g.LongCount(),
                First = g.Min(x => x.TimeUtc),
                Last = g.Max(x => x.TimeUtc),
                Names = g.Select(x => x.Name).Distinct().Count(),
                SampleName = g.Min(x => x.Name),
            })
            // Two a minute across the hour. Below that it is the noise browsing
            // produces.
            .Where(x => x.Count >= 120)
            .ToListAsync(ct);

        if (current.Count == 0) return findings;

        var clients = current.Select(x => x.Client).Distinct().ToList();
        var domains = current.Select(x => x.Domain).Distinct().ToList();

        var baseline = (await db.Queries
                .Where(q => q.TimeUtc >= ctx.BaselineStartUtc && q.TimeUtc < ctx.WindowStartUtc
                            && q.Action == "blocked"
                            && clients.Contains(q.Client) && domains.Contains(q.Domain))
                .GroupBy(q => new { q.Client, q.Domain })
                .Select(g => new { g.Key.Client, g.Key.Domain, Count = g.LongCount() })
                .ToListAsync(ct))
            .ToDictionary(x => x.Client + "|" + x.Domain, x => x.Count / ctx.BaselineHours);

        var dayKey = ctx.WindowEndUtc.ToString("yyyyMMdd");

        foreach (var row in current)
        {
            var perHour = baseline.GetValueOrDefault(row.Client + "|" + row.Domain, 0);

            // Without history it is not "continuous" but new — "new domain" and
            // "repetition burst" are responsible for that.
            if (perHour < 60) continue;

            // A genuine spike belongs to the other detector. This is about the
            // baseline level, not the peak.
            if (row.Count / perHour >= 5) continue;

            // Evenly means: spread across the window, not in one block. A
            // five-minute burst looks exactly like a continuous run in the
            // hourly figure.
            var span = row.Last - row.First;
            if (span < TimeSpan.FromMinutes(45)) continue;

            var perMinute = row.Count / Math.Max(1, span.TotalMinutes);
            var perDay = (long)(perHour * 24);

            findings.Add(new Finding
            {
                Detector = "dauersender",
                // Not an alarm: it is not an incident but a state. But one you
                // should have seen once.
                Severity = "info",
                Client = row.Client,
                ClientName = row.Name,
                Subject = row.Domain,
                Values = new FindingValues
                {
                    Count = row.Count,
                    SpanneSek = span.TotalSeconds,
                    ProMinute = perMinute,
                    PerHour = perHour,
                    PerDay = perDay,
                    BaselineDays = ctx.BaselineHours / 24,
                    Names = row.Names,
                    Example = row.SampleName,
                }.AsJson(),
                Suggestion = null,
                Score = perHour,
                // Once a day is enough. A state that reports itself hourly turns
                // into wallpaper.
                Fingerprint = $"dauersender|{row.Client}|{row.Domain}|{dayKey}",
            });
        }

        return findings;
    }

    /// <summary>
    /// A short enumeration that fits into a database field and an alert line.
    /// </summary>
    private static string Kurzliste(IEnumerable<string?> values)
    {
        var list = values.Where(w => !string.IsNullOrEmpty(w)).Distinct().ToList();
        if (list.Count == 0) return "";
        if (list.Count <= 2) return string.Join(", ", list);
        return $"{list[0]}, {list[1]} +{list.Count - 2}";
    }
}
