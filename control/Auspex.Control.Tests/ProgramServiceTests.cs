using Auspex.Control.Services.Geo;

namespace Auspex.Control.Tests;

/// <summary>
/// The join that answers "which program asks for which domain". Auspex holds
/// both halves — the resolver's name-to-address record and the sensor's
/// connection table — and neither half answers it alone.
/// </summary>
public class ProgramServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 14, 0, 0, DateTimeKind.Utc);
    private static DateTime Since => Now.AddHours(-2);

    [Fact]
    public async Task The_domains_behind_a_program_are_named()
    {
        using var fixture = new TestDb();
        fixture.SeedResolution("tracker.example", "104.18.1.1");
        fixture.SeedResolution("news.example", "93.184.216.34");
        fixture.SeedConnection("10.0.5.20", "chrome", "104.18.1.1", Now.AddMinutes(-30), count: 12);
        fixture.SeedConnection("10.0.5.20", "chrome", "93.184.216.34", Now.AddMinutes(-20), count: 3);

        var service = new ProgramService(fixture.Db);
        var profiles = await service.ForDeviceAsync("10.0.5.20", Since);

        var chrome = Assert.Single(profiles);
        Assert.Equal("chrome", chrome.Process);
        Assert.Equal(15, chrome.Connections);
        // Ordered by weight: the tracker was talked to four times as much.
        Assert.Equal("tracker.example", chrome.Domains[0].Domain);
        Assert.Equal(12, chrome.Domains[0].Connections);
        Assert.Equal("news.example", chrome.Domains[1].Domain);
    }

    // Several names can sit on one address. The connection table records where
    // the program went, not what it asked for — so it is credited with both,
    // and the documentation says so rather than pretending to a precision the
    // data does not have.
    [Fact]
    public async Task An_address_with_several_names_credits_all_of_them()
    {
        using var fixture = new TestDb();
        fixture.SeedResolution("a.example", "104.18.1.1");
        fixture.SeedResolution("b.example", "104.18.1.1");
        fixture.SeedConnection("10.0.5.20", "chrome", "104.18.1.1", Now.AddMinutes(-30), count: 5);

        var service = new ProgramService(fixture.Db);
        var profiles = await service.ForDeviceAsync("10.0.5.20", Since);

        var chrome = Assert.Single(profiles);
        Assert.Equal(2, chrome.Domains.Count);
        Assert.All(chrome.Domains, d => Assert.Equal(5, d.Connections));
    }

    // Addresses no lookup explains are counted rather than dropped — that
    // number is the interesting one, because it is traffic that went around
    // the resolver.
    [Fact]
    public async Task Addresses_without_a_lookup_are_counted_separately()
    {
        using var fixture = new TestDb();
        fixture.SeedResolution("news.example", "93.184.216.34");
        fixture.SeedConnection("10.0.5.20", "chrome", "93.184.216.34", Now.AddMinutes(-30), count: 4);
        fixture.SeedConnection("10.0.5.20", "chrome", "104.18.9.9", Now.AddMinutes(-30), count: 7);
        fixture.SeedConnection("10.0.5.20", "chrome", "104.18.8.8", Now.AddMinutes(-30), count: 2);

        var service = new ProgramService(fixture.Db);
        var profiles = await service.ForDeviceAsync("10.0.5.20", Since);

        var chrome = Assert.Single(profiles);
        Assert.Single(chrome.Domains);
        Assert.Equal(2, chrome.UnexplainedAddresses);
        // The total still counts everything: 4 + 7 + 2.
        Assert.Equal(13, chrome.Connections);
    }

    [Fact]
    public async Task Programs_are_kept_apart_and_ordered_by_weight()
    {
        using var fixture = new TestDb();
        fixture.SeedResolution("a.example", "93.184.216.34");
        fixture.SeedConnection("10.0.5.20", "quiet", "93.184.216.34", Now.AddMinutes(-30), count: 2);
        fixture.SeedConnection("10.0.5.20", "busy", "93.184.216.34", Now.AddMinutes(-30), count: 40);

        var service = new ProgramService(fixture.Db);
        var profiles = await service.ForDeviceAsync("10.0.5.20", Since);

        Assert.Equal(2, profiles.Count);
        Assert.Equal("busy", profiles[0].Process);
        Assert.Equal("quiet", profiles[1].Process);
    }

    // Another device's traffic must not appear under this one. The sensor
    // reports per sender address, and mixing them would make every statement
    // about a device worthless.
    [Fact]
    public async Task Another_devices_traffic_stays_out()
    {
        using var fixture = new TestDb();
        fixture.SeedResolution("a.example", "93.184.216.34");
        fixture.SeedConnection("10.0.5.20", "chrome", "93.184.216.34", Now.AddMinutes(-30), count: 5);
        fixture.SeedConnection("10.0.5.99", "teams", "93.184.216.34", Now.AddMinutes(-30), count: 5);

        var service = new ProgramService(fixture.Db);
        var profiles = await service.ForDeviceAsync("10.0.5.20", Since);

        Assert.Single(profiles);
        Assert.Equal("chrome", profiles[0].Process);
    }

    [Fact]
    public async Task Older_traffic_is_outside_the_window()
    {
        using var fixture = new TestDb();
        fixture.SeedResolution("a.example", "93.184.216.34");
        fixture.SeedConnection("10.0.5.20", "chrome", "93.184.216.34", Now.AddDays(-3), count: 9);

        var service = new ProgramService(fixture.Db);
        var profiles = await service.ForDeviceAsync("10.0.5.20", Since);

        Assert.Empty(profiles);
    }

    [Fact]
    public async Task The_device_list_names_what_the_sensor_reported_for()
    {
        using var fixture = new TestDb();
        fixture.SeedConnection("10.0.5.20", "chrome", "93.184.216.34", Now.AddMinutes(-30),
            device: "Arbeitsrechner");
        fixture.SeedConnection("10.0.5.99", "teams", "93.184.216.34", Now.AddMinutes(-10));

        var service = new ProgramService(fixture.Db);
        var devices = await service.DevicesAsync(Since);

        Assert.Equal(2, devices.Count);
        // Most recent first.
        Assert.Equal("10.0.5.99", devices[0].Client);
        Assert.Equal("Arbeitsrechner", devices[1].Device);
    }

    // Traffic inside the network never used public DNS, so counting it as
    // "unexplained" would put a red mark next to a browser for doing nothing
    // wrong. Found by reading a live installation: two of five such addresses
    // were the machine's own ULA, talking to Auspex itself.
    [Fact]
    public async Task Addresses_inside_the_network_are_not_counted_as_unexplained()
    {
        using var fixture = new TestDb();
        fixture.SeedResolution("news.example", "93.184.216.34");
        fixture.SeedConnection("10.0.5.20", "vivaldi", "93.184.216.34", Now.AddMinutes(-30), count: 4);
        // The machine's own network: a ULA and a private IPv4.
        fixture.SeedConnection("10.0.5.20", "vivaldi", "fd71:7881:a5f2::1", Now.AddMinutes(-30), count: 9);
        fixture.SeedConnection("10.0.5.20", "vivaldi", "192.168.1.61", Now.AddMinutes(-30), count: 6);
        // And one genuinely out on the internet with no lookup behind it.
        fixture.SeedConnection("10.0.5.20", "vivaldi", "2a00:1450:400c:c0a::bc", Now.AddMinutes(-30), count: 2);

        var service = new ProgramService(fixture.Db);
        var profiles = await service.ForDeviceAsync("10.0.5.20", Since);

        var vivaldi = Assert.Single(profiles);
        Assert.Equal(1, vivaldi.UnexplainedAddresses);
        // The connections themselves still all count — only the red mark is
        // reserved for what deserves it.
        Assert.Equal(21, vivaldi.Connections);
    }
}
