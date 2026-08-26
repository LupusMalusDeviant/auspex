using Auspex.Control.Services.Router;

namespace Auspex.Control.Tests;

/// <summary>
/// The catalogue comes out of the device's own description, not out of a
/// curated list. So everything hangs on the classification being right:
/// marking an action as "only reads" when it does not would render the
/// read-only lock useless.
/// </summary>
public class RouterCatalogTests
{
    private static RouterAction Action(string name, params RouterArgument[] args) =>
        new(name, args);

    private static RouterArgument Arg(
        string name, string direction = "in", string type = "string",
        string[]? allowed = null, string? min = null, string? max = null) =>
        new(name, direction, name + "Var", type, allowed ?? [], min, max, null);

    [Theory]
    [InlineData("GetInfo", true)]
    [InlineData("GetGenericHostEntry", true)]
    [InlineData("X_AVM-DE_GetHostListPath", true)]
    [InlineData("SetEnable", false)]
    [InlineData("DisallowWANAccessByIP", false)]
    [InlineData("X_AVM-DE_SetFriendlyNameByMAC", false)]
    public void Reading_and_changing_are_told_apart(string name, bool erwartet)
    {
        Assert.Equal(erwartet, Action(name).IsReadOnly);
    }

    [Theory]
    [InlineData("SetEnable")]
    [InlineData("Reboot")]
    [InlineData("X_AVM-DE_SetConfigFile")]
    [InlineData("SetDHCPServerEnable")]
    [InlineData("SetIPInterface")]
    public void Locking_actions_count_as_dangerous(string name)
    {
        Assert.True(Action(name).IsDangerous);
    }

    [Fact]
    public void A_read_action_is_never_dangerous()
    {
        // Otherwise a warning would stand in front of every harmless read,
        // and warnings that stand everywhere nobody reads any more.
        Assert.False(Action("GetSecurityPort").IsDangerous);
    }

    [Fact]
    public void Parameters_lose_their_New_for_display()
    {
        Assert.Equal("MACAddress", Arg("NewMACAddress").Label);
        Assert.Equal("Index", Arg("NewIndex").Label);
    }

    [Fact]
    public void A_parameter_really_called_New_keeps_it()
    {
        Assert.Equal("New", Arg("New").Label);
    }

    [Fact]
    public void Permitted_values_and_limits_are_taken_over()
    {
        var a = Arg("NewChannel", type: "ui1", allowed: ["1", "6", "11"], min: "1", max: "13");
        Assert.True(a.HasChoices);
        Assert.Equal(3, a.AllowedValues.Count);
        Assert.Equal("1", a.Minimum);
        Assert.Equal("13", a.Maximum);
        Assert.True(a.IsNumeric);
    }

    [Fact]
    public void Booleans_are_recognised_as_such()
    {
        Assert.True(Arg("NewEnable", type: "boolean").IsBoolean);
        Assert.False(Arg("NewName", type: "string").IsBoolean);
    }

    [Fact]
    public void Input_and_output_are_kept_apart()
    {
        var a = Action("GetInfo", Arg("NewIndex"), Arg("NewMACAddress", "out"));
        Assert.Single(a.In);
        Assert.Single(a.Out);
        Assert.Equal("NewIndex", a.In.First().Name);
    }

    [Fact]
    public void Services_of_the_same_name_stay_distinguishable()
    {
        // A Fritz!Box has four WLANConfiguration. Without telling them apart
        // they would stand in the list four times identically and nobody
        // would know which one is the guest network.
        var a = new RouterServiceInfo(
            "WLANConfiguration", "urn:x:service:WLANConfiguration:1",
            "/upnp/control/wlanconfig1", "/x.xml", []);
        var b = a with { ControlUrl = "/upnp/control/wlanconfig3" };

        Assert.Equal("WLANConfiguration (wlanconfig1)", a.DisplayName);
        Assert.Equal("WLANConfiguration (wlanconfig3)", b.DisplayName);
        Assert.NotEqual(a.DisplayName, b.DisplayName);
    }

    [Fact]
    public void A_service_without_an_instance_suffix_stays_plain()
    {
        var s = new RouterServiceInfo("Hosts", "urn:x:service:Hosts:1", "/upnp/control/hosts", "/x.xml", []);
        Assert.Equal("Hosts", s.DisplayName);
    }

    [Theory]
    // Bit 1 of the first byte set = locally assigned, so rolled at random.
    [InlineData("02:00:5E:00:53:0E", true)]
    [InlineData("06:00:5E:00:53:C1", true)]
    [InlineData("00:00:5E:00:53:0E", false)]
    [InlineData("00:00:5E:00:53:C1", false)]
    public void Random_MACs_are_recognised(string mac, bool erwartet)
    {
        var g = new RouterDevice(mac, "192.168.1.2", "Test", true, "802.11", "DHCP");
        Assert.Equal(erwartet, g.HasRandomMac);
    }

    [Fact]
    public void A_device_without_a_name_shows_its_MAC()
    {
        var g = new RouterDevice("00:00:5E:00:53:0E", "192.168.1.2", "", true, "", "DHCP");
        Assert.Equal("00:00:5E:00:53:0E", g.DisplayName);
    }

    [Fact]
    public void Without_an_account_the_router_counts_as_not_set_up()
    {
        Assert.False(new RouterOptions().Configured);
        Assert.False(new RouterOptions { User = "auspex" }.Configured);
        Assert.False(new RouterOptions { Password = "geheim" }.Configured);
        Assert.True(new RouterOptions { User = "auspex", Password = "geheim" }.Configured);
    }

    [Fact]
    public void An_empty_host_does_not_count_as_set_up()
    {
        Assert.False(new RouterOptions { Host = "", User = "a", Password = "b" }.Configured);
    }

    [Fact]
    public void A_catalogue_without_gaps_counts_as_complete()
    {
        var k = new RouterCatalog("FRITZ!Box", "Box", null, [], []);
        Assert.True(k.IsComplete);
    }

    [Fact]
    public void Missing_services_make_the_catalogue_incomplete()
    {
        // The case from the field: the box throttles, Hosts of all things
        // drops out, and the catalogue still looks complete with 28 services
        // instead of 39. That is exactly what must not pass.
        var k = new RouterCatalog("FRITZ!Box", "Box", null, [],
            ["Hosts", "LANHostConfigManagement"]);

        Assert.False(k.IsComplete);
        Assert.Equal(2, k.Incomplete.Count);
        Assert.Contains("Hosts", k.Incomplete);
    }
}

/// <summary>
/// The credentials are replaced at run time, everything else has to stay put
/// while that happens. Previously a new options object was assembled field by
/// field on loading — one of them had been forgotten in the process, and the
/// device list ended up in the wrong directory.
/// </summary>
public class RouterOptionsCopyTests
{
    private static RouterOptions BaseUrl() => new()
    {
        Host = "192.168.1.1",
        Port = 49000,
        TlsPort = 49443,
        SettingsPath = "/var/lib/auspex-control/router.json",
        DeviceNamePath = "/var/lib/auspex-shared/devices.json",
        CatalogTtl = TimeSpan.FromHours(6),
        Timeout = TimeSpan.FromSeconds(7),
        AcceptSelfSignedCertificate = false,
        Kind = "fritzbox",
    };

    [Fact]
    public void The_credentials_are_replaced()
    {
        var k = BaseUrl().WithAccess("10.0.0.1", "auspex", "geheim", readOnly: true);

        Assert.Equal("10.0.0.1", k.Host);
        Assert.Equal("auspex", k.User);
        Assert.Equal("geheim", k.Password);
        Assert.True(k.ReadOnly);
        Assert.True(k.Configured);
    }

    [Fact]
    public void Everything_else_stays()
    {
        var k = BaseUrl().WithAccess("10.0.0.1", "auspex", "geheim", readOnly: false);

        Assert.Equal("/var/lib/auspex-shared/devices.json", k.DeviceNamePath);
        Assert.Equal("/var/lib/auspex-control/router.json", k.SettingsPath);
        Assert.Equal(49443, k.TlsPort);
        Assert.Equal(49000, k.Port);
        Assert.Equal(TimeSpan.FromHours(6), k.CatalogTtl);
        Assert.Equal(TimeSpan.FromSeconds(7), k.Timeout);
        Assert.False(k.AcceptSelfSignedCertificate);
        Assert.Equal("fritzbox", k.Kind);
    }

    [Fact]
    public void The_original_stays_untouched()
    {
        var original = BaseUrl();
        original.WithAccess("10.0.0.1", "auspex", "geheim", readOnly: true);

        Assert.Equal("192.168.1.1", original.Host);
        Assert.Equal("", original.User);
        Assert.False(original.ReadOnly);
    }
}
