using System.Text.Json;

namespace Auspex.Control.Services;

/// <summary>One device held in quarantine, and what to put back afterwards.</summary>
public sealed record Quarantine(
    string Profile,
    string PreviousPolicy,
    DateTime UntilUtc,
    string Reason,
    DateTime StartedUtc);

/// <summary>
/// Answers a finding by taking a device off the network — for a while.
///
/// <para>
/// The chain the other two projects cannot build: spot it, act on it, and be
/// able to show afterwards what it cost. Auspex is the only one of the three
/// that holds all three parts.
/// </para>
/// <para>
/// <b>Three decisions, all of them deliberate.</b> It is triggered by a click
/// and never on its own: a false positive would otherwise take a device off
/// the network at night with nobody watching, and false positives exist — the
/// project has a detector devoted to spotting them. It acts on DNS rather
/// than on the router, so Auspex stays the one holding the switch and can let
/// go again. And it expires by itself, because a lock whose key lives in a
/// process that might die is not a lock, it is a trap.
/// </para>
/// <para>
/// The previous policy is written down before the profile is changed.
/// Otherwise lifting a quarantine would set the device to "open" and quietly
/// throw away a learn mode somebody had spent two weeks on.
/// </para>
/// </summary>
public sealed class QuarantineService(
    IClientProfiles auspex,
    QuarantineStore store,
    ILogger<QuarantineService> log)
{
    /// <summary>The default the interface offers. Long enough to look, short
    /// enough that forgetting it is not a disaster.</summary>
    public static readonly TimeSpan DefaultSpan = TimeSpan.FromHours(1);

    public IReadOnlyList<Quarantine> Active => store.All;

    public Quarantine? For(string profile) =>
        store.All.FirstOrDefault(q => string.Equals(q.Profile, profile, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Puts a profile into quarantine. Returns the error text, or null.
    /// </summary>
    public async Task<string?> StartAsync(
        string profile, TimeSpan span, string reason, CancellationToken ct = default)
    {
        var clients = await auspex.GetClientsAsync(ct);
        var client = clients.FirstOrDefault(c =>
            string.Equals(c.Name, profile, StringComparison.OrdinalIgnoreCase));
        if (client is null)
        {
            return $"no profile named {profile}";
        }
        if (client.Policy == "quarantine")
        {
            return null; // Already there. Not an error, and not a second entry.
        }

        var previous = string.IsNullOrEmpty(client.Policy) ? "open" : client.Policy;
        var wanted = client.Copy();
        wanted.Policy = "quarantine";

        var error = await auspex.PutClientAsync(wanted, ct);
        if (error is not null)
        {
            return error;
        }

        // Written down only after the resolver accepted it. The other order
        // would leave a record of a quarantine that never happened, and the
        // expiry would then "restore" a policy nobody had changed.
        store.Put(new Quarantine(client.Name, previous, DateTime.UtcNow + span, reason, DateTime.UtcNow));
        log.LogWarning("device {Profile} quarantined until {Until:u}: {Reason}",
            client.Name, DateTime.UtcNow + span, reason);
        return null;
    }

    /// <summary>Lifts a quarantine and puts the previous policy back.</summary>
    public async Task<string?> LiftAsync(string profile, CancellationToken ct = default)
    {
        var held = For(profile);
        if (held is null)
        {
            return null;
        }

        var clients = await auspex.GetClientsAsync(ct);
        var client = clients.FirstOrDefault(c =>
            string.Equals(c.Name, profile, StringComparison.OrdinalIgnoreCase));
        if (client is null)
        {
            // The profile is gone. Nothing left to restore, and keeping the
            // record would mean retrying for ever.
            store.Remove(profile);
            return null;
        }

        var wanted = client.Copy();
        wanted.Policy = held.PreviousPolicy;
        var error = await auspex.PutClientAsync(wanted, ct);
        if (error is not null)
        {
            return error;
        }

        store.Remove(profile);
        log.LogInformation("quarantine for {Profile} lifted, policy back to {Policy}",
            profile, held.PreviousPolicy);
        return null;
    }

    /// <summary>Lifts everything that has run out. Called on a timer.</summary>
    public async Task<int> LiftExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var due = store.All.Where(q => q.UntilUtc <= now).ToList();
        var lifted = 0;
        foreach (var q in due)
        {
            var error = await LiftAsync(q.Profile, ct);
            if (error is null)
            {
                lifted++;
                continue;
            }
            // Left in place on purpose: the next run tries again. Dropping it
            // would leave the device quarantined with nothing left to
            // remember that it should not be.
            log.LogWarning("quarantine for {Profile} could not be lifted: {Error}", q.Profile, error);
        }
        return lifted;
    }
}

/// <summary>
/// The quarantine list on disk. A handful of rows that have to survive a
/// restart — deliberately a file next to the other small state rather than a
/// table, because a restart is exactly when a forgotten quarantine would turn
/// into a device that is off the network with no record of why.
/// </summary>
public sealed class QuarantineStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly Lock _lock = new();
    private List<Quarantine> _current;

    public QuarantineStore(string path)
    {
        _path = path;
        _current = Load();
    }

    public IReadOnlyList<Quarantine> All
    {
        get { lock (_lock) { return [.. _current]; } }
    }

    public void Put(Quarantine q)
    {
        lock (_lock)
        {
            _current.RemoveAll(x => string.Equals(x.Profile, q.Profile, StringComparison.OrdinalIgnoreCase));
            _current.Add(q);
            Save();
        }
    }

    public void Remove(string profile)
    {
        lock (_lock)
        {
            _current.RemoveAll(x => string.Equals(x.Profile, profile, StringComparison.OrdinalIgnoreCase));
            Save();
        }
    }

    private List<Quarantine> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            return JsonSerializer.Deserialize<List<Quarantine>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // An unreadable list must not stop the control plane from
            // starting. The quarantines are then gone, which errs towards
            // "device works" rather than "device is off and nobody knows why".
            return [];
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(_current, Options));
        }
        catch (IOException)
        {
            // Kept in memory. Better than throwing in the middle of a request.
        }
    }
}
