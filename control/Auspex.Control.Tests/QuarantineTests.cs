using Microsoft.Extensions.Logging.Abstractions;
using Auspex.Control.Services;

namespace Auspex.Control.Tests;

/// <summary>
/// Taking a device off the network is the most consequential thing Auspex
/// does on its own. These tests are about the two ways it could go wrong
/// quietly: forgetting what the device was before, and forgetting to let go.
/// </summary>
public class QuarantineTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"quarantine-{Guid.NewGuid():N}.json");

    private (QuarantineService Service, FakeAuspex Auspex, QuarantineStore Store) Build(
        params ManagedClient[] clients)
    {
        var auspex = new FakeAuspex(clients);
        var store = new QuarantineStore(_file);
        return (new QuarantineService(auspex, store, NullLogger<QuarantineService>.Instance), auspex, store);
    }

    public void Dispose()
    {
        if (File.Exists(_file)) File.Delete(_file);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Quarantine_switches_the_profile_and_records_it()
    {
        var (service, auspex, _) = Build(new ManagedClient { Name = "iot", Policy = "open" });

        var error = await service.StartAsync("iot", TimeSpan.FromHours(1), "test");

        Assert.Null(error);
        Assert.Equal("quarantine", auspex.Saved.Single().Policy);
        Assert.Equal("iot", service.Active.Single().Profile);
    }

    // The one that would hurt and never show: a device in learn mode gets
    // quarantined, and lifting it sets the device to "open" — throwing away
    // two weeks of learning without a word.
    [Fact]
    public async Task Lifting_restores_the_policy_the_device_had_before()
    {
        var (service, auspex, _) = Build(new ManagedClient { Name = "iot", Policy = "learn" });

        await service.StartAsync("iot", TimeSpan.FromHours(1), "test");
        auspex.Apply();
        await service.LiftAsync("iot");

        Assert.Equal("learn", auspex.Saved.Last().Policy);
        Assert.Empty(service.Active);
    }

    // A lock whose key lives in a process that might die is not a lock, it is
    // a trap. It has to let go on its own.
    [Fact]
    public async Task An_expired_quarantine_lifts_itself()
    {
        var (service, auspex, _) = Build(new ManagedClient { Name = "iot", Policy = "enforce" });

        await service.StartAsync("iot", TimeSpan.FromSeconds(-1), "test");
        auspex.Apply();

        var lifted = await service.LiftExpiredAsync();

        Assert.Equal(1, lifted);
        Assert.Equal("enforce", auspex.Saved.Last().Policy);
        Assert.Empty(service.Active);
    }

    [Fact]
    public async Task One_that_has_not_run_out_stays()
    {
        var (service, _, _) = Build(new ManagedClient { Name = "iot", Policy = "open" });

        await service.StartAsync("iot", TimeSpan.FromHours(1), "test");
        var lifted = await service.LiftExpiredAsync();

        Assert.Equal(0, lifted);
        Assert.Single(service.Active);
    }

    // If the resolver refuses, the record must not be written — otherwise the
    // expiry would later "restore" a policy nobody ever changed.
    [Fact]
    public async Task A_refused_change_is_not_recorded()
    {
        var (service, auspex, _) = Build(new ManagedClient { Name = "iot", Policy = "open" });
        auspex.Refuse = "resolver unreachable";

        var error = await service.StartAsync("iot", TimeSpan.FromHours(1), "test");

        Assert.NotNull(error);
        Assert.Empty(service.Active);
    }

    // And the mirror image: if lifting fails, the record has to stay, or the
    // device is quarantined with nothing left to remember that it should not
    // be.
    [Fact]
    public async Task A_failed_lift_keeps_the_record_for_the_next_attempt()
    {
        var (service, auspex, _) = Build(new ManagedClient { Name = "iot", Policy = "open" });
        await service.StartAsync("iot", TimeSpan.FromSeconds(-1), "test");
        auspex.Apply();
        auspex.Refuse = "resolver unreachable";

        var lifted = await service.LiftExpiredAsync();

        Assert.Equal(0, lifted);
        Assert.Single(service.Active);
    }

    // A restart is exactly when a forgotten quarantine would turn into a
    // device that is off the network with no record of why.
    [Fact]
    public async Task The_list_survives_a_restart()
    {
        var (service, _, _) = Build(new ManagedClient { Name = "iot", Policy = "learn" });
        await service.StartAsync("iot", TimeSpan.FromHours(1), "test");

        var second = new QuarantineStore(_file);

        var held = Assert.Single(second.All);
        Assert.Equal("iot", held.Profile);
        Assert.Equal("learn", held.PreviousPolicy);
    }

    [Fact]
    public async Task Quarantining_twice_does_not_produce_two_entries()
    {
        var (service, auspex, _) = Build(new ManagedClient { Name = "iot", Policy = "open" });

        await service.StartAsync("iot", TimeSpan.FromHours(1), "first");
        auspex.Apply();
        await service.StartAsync("iot", TimeSpan.FromHours(1), "second");

        Assert.Single(service.Active);
        // And the recorded previous policy is still the real one, not
        // "quarantine" — otherwise lifting would leave the device locked.
        Assert.Equal("open", service.Active[0].PreviousPolicy);
    }
}

/// <summary>A resolver that remembers what it was told.</summary>
internal sealed class FakeAuspex(params ManagedClient[] clients) : IClientProfiles
{
    private List<ManagedClient> _clients = [.. clients];

    public List<ManagedClient> Saved { get; } = [];
    public string? Refuse { get; set; }

    /// <summary>Makes the last saved profile the current one.</summary>
    public void Apply()
    {
        if (Saved.Count == 0) return;
        var last = Saved[^1];
        _clients = [.. _clients.Where(c => c.Name != last.Name), last];
    }

    public Task<IReadOnlyList<ManagedClient>> GetClientsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ManagedClient>>(_clients);

    public Task<string?> PutClientAsync(ManagedClient client, CancellationToken ct = default)
    {
        if (Refuse is not null) return Task.FromResult<string?>(Refuse);
        Saved.Add(client);
        return Task.FromResult<string?>(null);
    }
}

/// <summary>
/// The container has to be able to build what the hosted services ask for.
///
/// This exists because it did not, and nothing caught it: the quarantine
/// service takes <see cref="IClientProfiles"/>, the container only knew
/// <see cref="IAuspexClient"/>, and a container does not infer the base
/// interface from the derived one. Every unit test passed, because they all
/// build the service by hand with a double. The running system logged
/// "Unable to resolve service" once a minute, and the feature simply did
/// nothing.
/// </summary>
public class QuarantineWiringTests
{
    /// <summary>
    /// And the statement the registration above relies on: the wide interface
    /// really does carry the narrow one. If that ever stops being true, the
    /// registration compiles and fails at run time.
    /// </summary>
    [Fact]
    public void The_wide_interface_carries_the_narrow_one()
    {
        Assert.True(typeof(IClientProfiles).IsAssignableFrom(typeof(IAuspexClient)));
    }
}
