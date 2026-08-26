using Auspex.Control.Data;
using Auspex.Control.Services.Localization;
using Microsoft.EntityFrameworkCore;

namespace Auspex.Control.Services.Router;

/// <summary>
/// Watches the router for changes nobody set in motion.
///
/// The occasion is port mappings. Over UPnP any device on the network may
/// open a port to the outside for itself — a console, a camera, or something
/// malicious too. The router asks nobody, and in the Fritz!Box interface you
/// only see it if you go looking. A quiet door to the outside weighs more
/// than a tracker that slipped through, so this is reported rather than
/// merely displayed.
///
/// Second point: new devices. Their arrival is in the router log, but there
/// among a hundred other lines.
///
/// The service writes into the same findings as the detectors — a second
/// interface for the same thing would be one interface too many.
/// </summary>
public sealed class RouterWatchService(
    IServiceScopeFactory scopes,
    IRouterSettingsStore store,
    ILogger<RouterWatchService> log) : BackgroundService
{
    /// <summary>
    /// Five minutes. A mapping that stands open unnoticed for ten minutes is
    /// no different a harm from one open for five — asking more often only
    /// costs load on the router, which throttles rapid polling anyway.
    /// </summary>
    private static readonly TimeSpan Beat = TimeSpan.FromMinutes(5);

    /// <summary>
    /// State of the Fritz!Box's change counter for the device list. Querying
    /// the list individually costs one SOAP call per device; with thirty
    /// devices every five minutes that would be close to nine thousand calls
    /// a day for a question that almost always answers "nothing new". The
    /// counter costs one.
    /// </summary>
    private string _deviceCounter = "";

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // The catalogue is read in the background at startup; before that every
        // call runs into discovery and therefore into the throttling.
        if (!await Wait(TimeSpan.FromMinutes(1), stop)) return;

        while (!stop.IsCancellationRequested)
        {
            if (store.Current.Configured)
            {
                try
                {
                    await EinmalAsync(stop);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // An unreachable router is a state, not a crash. Again on the
                    // next tick.
                    log.LogWarning(ex, "Router-Beobachtung fehlgeschlagen");
                }
            }

            if (!await Wait(Beat, stop)) return;
        }
    }

    private static async Task<bool> Wait(TimeSpan duration, CancellationToken ct)
    {
        try
        {
            await Task.Delay(duration, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>One pass. Returns how many findings came out of it.</summary>
    public async Task<int> EinmalAsync(CancellationToken ct = default)
    {
        using var range = scopes.CreateScope();
        var admin = range.ServiceProvider.GetRequiredService<RouterAdmin>();
        var db = range.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        var findings = new List<Finding>();
        findings.AddRange(await PortsAsync(admin, db, ct));
        findings.AddRange(await DevicesAsync(admin, db, ct));

        var fresh = 0;
        if (findings.Count > 0)
        {
            var fingerprints = findings.Select(f => f.Fingerprint).ToList();
            var known = await db.Findings
                .Where(f => fingerprints.Contains(f.Fingerprint))
                .Select(f => f.Fingerprint)
                .ToListAsync(ct);

            foreach (var f in findings.Where(f => !known.Contains(f.Fingerprint)))
            {
                db.Findings.Add(f);
                fresh++;
            }
        }

        // State and findings go out together: otherwise an error in between
        // could leave a change counted as remembered without it ever having
        // been reported.
        await db.SaveChangesAsync(ct);

        if (fresh > 0)
        {
            log.LogInformation("{Count} changes on the router found", fresh);
            var reporter = range.ServiceProvider.GetRequiredService<FindingNotifier>();
            await reporter.FlushAsync(ct);
        }

        return fresh;
    }

    private async Task<List<Finding>> PortsAsync(
        IRouterAdmin admin, AnalyticsDbContext db, CancellationToken ct)
    {
        var reply = await admin.GetPortMappingsAsync(ct);

        // Could not be asked: then the state is unknown, not empty. "Every
        // mapping has disappeared" would be the worst false report here -
        // exactly the opposite of the truth.
        if (!reply.Ok)
        {
            log.LogWarning("Port mappings not read: {Reason}", reply.Error);
            return [];
        }

        var mappings = reply.Entries;
        if (mappings.Count == 0)
        {
            return [];
        }

        var now = new Dictionary<string, string>();
        foreach (var f in mappings)
        {
            now[Key(f)] =
                $"{f.InternalClient}:{f.InternalPort} · {(f.Enabled ? "aktiv" : "aus")} · {f.Description}";
        }

        var change = await CompareAsync(db, "port", now, reportGone: true, ct);
        var findings = new List<Finding>();

        foreach (var w in change)
        {
            var parts = w.Key.Split('/');
            var log = parts.Length > 0 ? parts[0] : "?";
            var port = parts.Length > 1 ? parts[1] : "?";
            var openToAll = parts.Length > 2 && parts[2] == "*";
            var destination = mappings.FirstOrDefault(f => Key(f) == w.Key);

            // Weight and measurements only. What the finding is called and what
            // it explains is the display's decision - this service runs every
            // five minutes in the background and has no reader whose language
            // it could know.
            var gewicht = w.ChangeKind switch
            {
                ChangeKind.After => openToAll ? "high" : "warn",
                ChangeKind.Changed => "warn",
                _ => "info",
            };

            findings.Add(Finding(
                detektor: "portfreigabe",
                key: w.Key,
                content: w.Detail,
                severity: gewicht,
                client: destination?.InternalClient ?? "",
                subjekt: w.Key,
                values: new FindingValues
                {
                    ChangeKind = w.ChangeKind switch
                    {
                        ChangeKind.After => "neu",
                        ChangeKind.Changed => "geaendert",
                        _ => "weg",
                    },
                    Protocol = log,
                    Port = port,
                    ForAll = openToAll,
                    Before = w.Before,
                    Now = w.Detail,
                }));
        }

        return findings;
    }

    /// <summary>
    /// Protocol, external port and remote end form a mapping's identity —
    /// exactly the fields the router treats as unique as well.
    /// </summary>
    internal static string Key(RouterPortMapping f) =>
        $"{f.Protocol}/{f.ExternalPort}/{(ForAll(f.RemoteHost) ? "*" : f.RemoteHost)}";

    /// <summary>
    /// Whether the mapping applies to any remote end, that is, whether the
    /// entire internet reaches this device.
    ///
    /// The Fritz!Box does not write nothing for that but <c>0.0.0.0</c> —
    /// measured on the two existing mappings. Checking only for the empty
    /// value classifies precisely the open case as the more harmless one.
    /// </summary>
    internal static bool ForAll(string remote)
    {
        var g = remote.Trim();
        return g.Length == 0 || g == "0.0.0.0" || g == "::" || g == "*";
    }

    private async Task<List<Finding>> DevicesAsync(
        IRouterAdmin admin, AnalyticsDbContext db, CancellationToken ct)
    {
        var counter = await admin.GetHostChangeCounterAsync(ct);
        if (counter is not null && counter == _deviceCounter)
        {
            return [];
        }

        var devices = await admin.GetDevicesAsync(ct);
        if (devices.Count == 0)
        {
            return [];
        }

        // Remember only after a successful fetch: otherwise the next pass
        // skips the list even though it was never read.
        _deviceCounter = counter ?? "";

        var now = new Dictionary<string, string>();
        foreach (var g in devices)
        {
            now[g.Mac.ToLowerInvariant()] =
                $"{(g.Name.Length == 0 ? "ohne Namen" : g.Name)} · {g.Ip} · {g.Interface}";
        }

        // Devices do not disappear: the Fritz!Box remembers them, switched off
        // as well. Whatever does fall out of the list was deleted there by
        // hand — that needs no report.
        var change = await CompareAsync(db, "geraet", now, reportGone: false, ct);
        var findings = new List<Finding>();

        foreach (var w in change.Where(w => w.ChangeKind == ChangeKind.After))
        {
            var g = devices.First(x => x.Mac.ToLowerInvariant() == w.Key);

            findings.Add(Finding(
                detektor: "neues-geraet",
                key: w.Key,
                content: w.Detail,
                severity: "warn",
                client: g.Ip,
                clientName: g.Name.Length == 0 ? null : g.Name,
                subjekt: w.Key,
                values: new FindingValues
                {
                    ChangeKind = "neu",
                    Connection = g.Interface,
                    Address = g.Ip,
                    ZufallMac = g.HasRandomMac,
                    Online = g.Online,
                }));
        }

        return findings;
    }

    internal enum ChangeKind { After, Changed, Gone }

    internal record Change(ChangeKind ChangeKind, string Key, string Detail, string? Before);

    /// <summary>
    /// Compares the current state with the remembered one and carries the
    /// remembered one forward. On the very first run no report is produced —
    /// otherwise every existing mapping and every present device would be
    /// "new" once, and the first page of findings would be pure inventory.
    /// </summary>
    internal static async Task<List<Change>> CompareAsync(
        AnalyticsDbContext db,
        string kind,
        IReadOnlyDictionary<string, string> now,
        bool reportGone,
        CancellationToken ct)
    {
        var gemerkt = await db.RouterObservations
            .Where(o => o.Kind == kind)
            .ToDictionaryAsync(o => o.Key, ct);

        var firstRun = gemerkt.Count == 0;
        var change = new List<Change>();
        var clock = DateTime.UtcNow;

        foreach (var (key, detail) in now)
        {
            if (gemerkt.TryGetValue(key, out var old))
            {
                var wiederDa = old.GoneUtc is not null;
                if (old.Detail != detail || wiederDa)
                {
                    if (!firstRun)
                    {
                        change.Add(new Change(
                            wiederDa ? ChangeKind.After : ChangeKind.Changed, key, detail, old.Detail));
                    }
                    old.Detail = detail;
                }

                old.GoneUtc = null;
                old.LastSeenUtc = clock;
                continue;
            }

            if (!firstRun)
            {
                change.Add(new Change(ChangeKind.After, key, detail, null));
            }

            db.RouterObservations.Add(new RouterObservation
            {
                Kind = kind,
                Key = key,
                Detail = detail,
                FirstSeenUtc = clock,
                LastSeenUtc = clock,
            });
        }

        foreach (var old in gemerkt.Values)
        {
            if (now.ContainsKey(old.Key) || old.GoneUtc is not null)
            {
                continue;
            }

            old.GoneUtc = clock;
            if (reportGone)
            {
                change.Add(new Change(ChangeKind.Gone, old.Key, old.Detail, null));
            }
        }

        return change;
    }

    private static Finding Finding(
        string detektor,
        string key,
        string content,
        string severity,
        string client,
        string subjekt,
        FindingValues values,
        string? clientName = null)
    {
        var clock = DateTime.UtcNow;

        return new Finding
        {
            Detector = detektor,
            Severity = severity,
            Client = Shorten(client, 64),
            ClientName = clientName is null ? null : Shorten(clientName, 128),
            Subject = Shorten(subjekt, 253),
            Values = Shorten(values.AsJson(), 1000),
            // No rule: the interface creates an exception in the rule set from
            // Suggestion. A port mapping is fixed at the router, not with a
            // DNS rule.
            Suggestion = null,
            DetectedUtc = clock,
            WindowStartUtc = clock,
            WindowEndUtc = clock,
            Score = severity == "high" ? 100 : severity == "warn" ? 50 : 10,
            // Content rather than clock time: the same change should turn up
            // only once even after a restart. The day hangs off it so the
            // same mapping is reported again tomorrow if it went away in
            // between and came back.
            Fingerprint = Shorten(
                $"{detektor}|{key}|{Abdruck(content)}|{clock:yyyyMMdd}", 200),
        };
    }

    /// <summary>
    /// A stable imprint of a text. <c>string.GetHashCode</c> would be wrong
    /// here: the value differs between two runs of the program, and a
    /// fingerprint that changes on restart reports every existing change
    /// again.
    /// </summary>
    private static string Abdruck(string value)
    {
        var raw = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(raw)[..8].ToLowerInvariant();
    }

    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
