using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Auspex.Control.Data;
using Auspex.Control.Services;
using Auspex.Control.Services.Extension;
using Auspex.Control.Services.Geo;
using Auspex.Control.Services.Router;

namespace Auspex.Control.Tests;

/// <summary>
/// The overview of prerequisites.
///
/// <para>
/// It is the counterpart to the restraint: nothing switches itself on, and
/// so that this does not become a silent gap, what is missing has to stand
/// here. The distinction that matters is the one between "not set up at all"
/// and "set up but not reporting" - when hunting a fault that is the whole
/// difference.
/// </para>
/// </summary>
public class PrerequisitesTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private Prerequisites Build(bool geoOn = false, long geoRows = 0)
    {
        // One folder per run: otherwise the two stores put their files where
        // a real installation would have them - and might find an account
        // stored there.
        var folder = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"auspex-test-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(folder);
        var guard = new ServiceCollection().AddDataProtection()
            .Services.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();

        var routerStore = new RouterSettingsStore(
            Options.Create(new RouterOptions
            {
                SettingsPath = System.IO.Path.Combine(folder, "router.json"),
            }),
            guard,
            NullLogger<RouterSettingsStore>.Instance);

        var ranges = new NetworkRanges(
            System.IO.Path.Combine(folder, "ranges.db"),
            NullLogger<NetworkRanges>.Instance);
        if (geoRows > 0)
        {
            ranges.Prepare();
            ranges.Import([(0, UInt128.MaxValue, 64512, "ZZ", "Test")]);
        }

        return new Prerequisites(
            routerStore,
            new ExtensionTokenStore(
                new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Extension:TokenPath"] = System.IO.Path.Combine(folder, "token.json"),
                    }).Build(),
                guard,
                NullLogger<ExtensionTokenStore>.Instance),
            new SensorPackage(NullLogger<SensorPackage>.Instance),
            ranges,
            Options.Create(new GeoOptions { Enabled = geoOn }),
            Options.Create(new AnalyticsOptions()),
            _db.Db);
    }

    private static Part Find(IReadOnlyList<Part> parts, string key) =>
        parts.Single(p => p.Key == key);

    [Fact]
    public async Task With_nothing_everything_is_missing_except_the_analysis()
    {
        var parts = await Build().AllAsync();

        // The analysis needs nothing from outside - it is on as long as
        // nobody switches it off.
        Assert.Equal(PartState.Active, Find(parts, "analytics").State);
        Assert.Equal(PartState.Missing, Find(parts, "router").State);
        Assert.Equal(PartState.Missing, Find(parts, "extension").State);
        Assert.Equal(PartState.Missing, Find(parts, "sensor").State);
        Assert.Equal(PartState.Missing, Find(parts, "origin").State);
    }

    [Fact]
    public async Task A_sensor_that_has_just_reported_is_active()
    {
        _db.Db.Connections.Add(new Connection
        {
            Client = "192.168.1.10",
            Process = "claude",
            Destination = "160.79.104.10",
            Port = 443,
            FirstUtc = DateTime.UtcNow.AddMinutes(-5),
            LastUtc = DateTime.UtcNow.AddMinutes(-1),
            Count = 3,
        });
        await _db.Db.SaveChangesAsync();

        Assert.Equal(PartState.Active, Find(await Build().AllAsync(), "sensor").State);
    }

    /// <summary>
    /// The case that decides the fault hunt: there was data once, but
    /// nothing for a day. That is something other than "never had a sensor"
    /// and has to be called something else.
    /// </summary>
    [Fact]
    public async Task A_sensor_gone_quiet_is_not_called_missing()
    {
        _db.Db.Connections.Add(new Connection
        {
            Client = "192.168.1.10",
            Process = "claude",
            Destination = "160.79.104.10",
            Port = 443,
            FirstUtc = DateTime.UtcNow.AddDays(-3),
            LastUtc = DateTime.UtcNow.AddDays(-2),
            Count = 3,
        });
        await _db.Db.SaveChangesAsync();

        Assert.Equal(PartState.Idle, Find(await Build().AllAsync(), "sensor").State);
    }

    /// <summary>
    /// Origin: the switch says whether refreshing happens, the row count
    /// says whether anything is there. Switched on without data is not yet
    /// an answer - and data without the switch is not a missing source.
    /// </summary>
    [Fact]
    public async Task Origin_distinguishes_switch_from_data()
    {
        Assert.Equal(PartState.Missing, Find(await Build(geoOn: true).AllAsync(), "origin").State);
        Assert.Equal(PartState.Idle, Find(await Build(geoOn: false, geoRows: 1).AllAsync(), "origin").State);
        Assert.Equal(PartState.Active, Find(await Build(geoOn: true, geoRows: 1).AllAsync(), "origin").State);
    }
}

/// <summary>
/// Every path the control plane writes to has to point at the volume, not
/// into the container.
///
/// This exists because the quarantine store did not: its default is a
/// relative <c>var/quarantine.json</c>, which inside the container lands in
/// the working directory and vanishes with the next recreate. The unit test
/// "the list survives a restart" would have kept passing while the running
/// system forgot every quarantine — and a forgotten quarantine is a device
/// off the network with nothing left to say why.
/// </summary>
public class WritablePathTests
{
    private static string Dockerfile()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !File.Exists(Path.Combine(here.FullName, "Auspex.Control.slnx")))
        {
            here = here.Parent;
        }
        Assert.NotNull(here);
        return File.ReadAllText(Path.Combine(here!.FullName, "Auspex.Control", "Dockerfile"));
    }

    /// <summary>compose.yml, which lives one level above the solution.</summary>
    private static string Compose()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !File.Exists(Path.Combine(here.FullName, "compose.yml")))
        {
            here = here.Parent;
        }
        Assert.NotNull(here);
        return File.ReadAllText(Path.Combine(here!.FullName, "compose.yml"));
    }

    [Theory]
    [InlineData("Extension__TokenPath")]
    [InlineData("Appearance__Path")]
    [InlineData("Router__SettingsPath")]
    [InlineData("Quarantine__Path")]
    [InlineData("Auth__KeyPath")]
    public void The_path_points_at_a_mounted_volume(string variable)
    {
        // Wherever it is declared. Auth__KeyPath moved to compose.yml because
        // the Docker linter reads "Auth" plus "Key" in a Dockerfile ENV as a
        // leaked secret; the others stay in the image so it is usable on its
        // own. Both places count — what matters is that the value is absolute
        // and on a volume, not which file says so.
        var dockerfile = Dockerfile() + Compose();

        // The value, not the line. Two variables landed on one physical line
        // once, and a line-wide check then read the neighbour's value and
        // reported green while the path was wrong.
        // Two spellings, because the two files disagree: a Dockerfile ENV
        // writes NAME=value, YAML writes NAME: "value".
        var at = -1;
        var marker = "";
        foreach (var candidate in new[] { variable + "=", variable + ":" })
        {
            at = dockerfile.IndexOf(candidate, StringComparison.Ordinal);
            if (at >= 0)
            {
                marker = candidate;
                break;
            }
        }
        Assert.True(at >= 0, $"{variable} is set in neither the Dockerfile nor compose.yml");

        var rest = dockerfile[(at + marker.Length)..].TrimStart().Trim('"');
        var value = new string([.. rest.TakeWhile(c => !char.IsWhiteSpace(c) && c != '\\' && c != '"')]);

        Assert.StartsWith("/var/lib/auspex-", value, StringComparison.Ordinal);
    }
}
