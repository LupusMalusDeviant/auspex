using Auspex.Control.Services.Router;

namespace Auspex.Control.Tests;

/// <summary>
/// The comparison decides whether a report comes into being. It can be
/// wrong in two ways, and both are worse than no watching at all: report too
/// much and soon nobody reads it; report too little and a door stands open
/// to the outside with nobody the wiser.
/// </summary>
public class RouterReconcileTests
{
    private static Dictionary<string, string> State(params (string Key, string Detail)[] paare) =>
        paare.ToDictionary(p => p.Key, p => p.Detail);

    /// <summary>
    /// On the first run everything is new — but none of it is a change.
    /// Without this rule the first page of findings would be an inventory
    /// with thirty warnings, and the one real one would drown in it.
    /// </summary>
    [Fact]
    public async Task The_first_run_reports_nothing_but_remembers_everything()
    {
        using var fixture = new TestDb();

        var change = await RouterWatchService.CompareAsync(
            fixture.Db, "port",
            State(("TCP/8080/*", "192.168.1.29:8080 · aktiv · Konsole")),
            reportGone: true, default);

        Assert.Empty(change);

        await fixture.Db.SaveChangesAsync();
        Assert.Single(fixture.Db.RouterObservations);
    }

    [Fact]
    public async Task A_new_port_mapping_after_the_first_run_is_reported()
    {
        using var fixture = new TestDb();
        var old = State(("TCP/8080/*", "192.168.1.29:8080 · aktiv · Konsole"));

        await RouterWatchService.CompareAsync(fixture.Db, "port", old, true, default);
        await fixture.Db.SaveChangesAsync();

        var fresh = State(
            ("TCP/8080/*", "192.168.1.29:8080 · aktiv · Konsole"),
            ("UDP/9999/*", "192.168.1.51:9999 · aktiv · irgendwas"));

        var change = await RouterWatchService.CompareAsync(fixture.Db, "port", fresh, true, default);

        var w = Assert.Single(change);
        Assert.Equal(RouterWatchService.ChangeKind.After, w.ChangeKind);
        Assert.Equal("UDP/9999/*", w.Key);
    }

    /// <summary>
    /// Unchanged means silent. A service reporting the same thing every five
    /// minutes is not a watch but a fault.
    /// </summary>
    [Fact]
    public async Task An_unchanged_state_produces_nothing()
    {
        using var fixture = new TestDb();
        var snapshot = State(("TCP/8080/*", "192.168.1.29:8080 · aktiv · Konsole"));

        await RouterWatchService.CompareAsync(fixture.Db, "port", snapshot, true, default);
        await fixture.Db.SaveChangesAsync();

        for (var i = 0; i < 3; i++)
        {
            Assert.Empty(await RouterWatchService.CompareAsync(
                fixture.Db, "port", snapshot, true, default));
            await fixture.Db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// The same outer port, a different inner target: from outside you now
    /// reach a different device. That is the case you notice least and want
    /// to know about most.
    /// </summary>
    [Fact]
    public async Task A_redirected_target_is_reported_as_a_change()
    {
        using var fixture = new TestDb();

        await RouterWatchService.CompareAsync(fixture.Db, "port",
            State(("TCP/8080/*", "192.168.1.29:8080 · aktiv · Konsole")), true, default);
        await fixture.Db.SaveChangesAsync();

        var change = await RouterWatchService.CompareAsync(fixture.Db, "port",
            State(("TCP/8080/*", "192.168.1.99:8080 · aktiv · Konsole")), true, default);

        var w = Assert.Single(change);
        Assert.Equal(RouterWatchService.ChangeKind.Changed, w.ChangeKind);
        Assert.Contains("192.168.1.29", w.Before);
        Assert.Contains("192.168.1.99", w.Detail);
    }

    [Fact]
    public async Task A_vanished_port_mapping_is_reported_when_wanted()
    {
        using var fixture = new TestDb();

        await RouterWatchService.CompareAsync(fixture.Db, "port",
            State(("TCP/8080/*", "a"), ("UDP/9999/*", "b")), true, default);
        await fixture.Db.SaveChangesAsync();

        var change = await RouterWatchService.CompareAsync(fixture.Db, "port",
            State(("TCP/8080/*", "a")), true, default);

        var w = Assert.Single(change);
        Assert.Equal(RouterWatchService.ChangeKind.Gone, w.ChangeKind);
        Assert.Equal("UDP/9999/*", w.Key);
    }

    /// <summary>
    /// Devices do not really disappear — the Fritz!Box remembers them
    /// switched off as well. Reporting here would report every phone that
    /// was turned off.
    /// </summary>
    [Fact]
    public async Task For_devices_disappearance_is_not_reported()
    {
        using var fixture = new TestDb();

        await RouterWatchService.CompareAsync(fixture.Db, "geraet",
            State(("aa:bb:cc:dd:ee:01", "Handy · 192.168.1.20 · WLAN"),
                  ("aa:bb:cc:dd:ee:02", "Laptop · 192.168.1.21 · LAN")), false, default);
        await fixture.Db.SaveChangesAsync();

        var change = await RouterWatchService.CompareAsync(fixture.Db, "geraet",
            State(("aa:bb:cc:dd:ee:01", "Handy · 192.168.1.20 · WLAN")), false, default);

        Assert.Empty(change);
    }

    /// <summary>
    /// A port mapping that was gone and comes back is new again. Without
    /// that the second opening of the same port would be silent — and that
    /// is precisely the interesting one.
    /// </summary>
    [Fact]
    public async Task What_was_gone_and_comes_back_is_new_again()
    {
        using var fixture = new TestDb();
        var having = State(("TCP/8080/*", "192.168.1.29:8080 · aktiv · Konsole"));
        var without = State(("TCP/1/*", "platzhalter"));

        await RouterWatchService.CompareAsync(fixture.Db, "port", having, true, default);
        await fixture.Db.SaveChangesAsync();

        await RouterWatchService.CompareAsync(fixture.Db, "port", without, true, default);
        await fixture.Db.SaveChangesAsync();

        var change = await RouterWatchService.CompareAsync(fixture.Db, "port", having, true, default);

        var w = Assert.Single(change, x => x.Key == "TCP/8080/*");
        Assert.Equal(RouterWatchService.ChangeKind.After, w.ChangeKind);
    }

    /// <summary>
    /// Ports and devices share one table. A key occurring in both kinds must
    /// not overwrite itself across them.
    /// </summary>
    [Fact]
    public async Task The_kinds_do_not_disturb_each_other()
    {
        using var fixture = new TestDb();

        await RouterWatchService.CompareAsync(fixture.Db, "port", State(("x", "eins")), true, default);
        await fixture.Db.SaveChangesAsync();

        // Second kind, same key: for it this is the first run.
        var change = await RouterWatchService.CompareAsync(
            fixture.Db, "geraet", State(("x", "zwei")), false, default);
        await fixture.Db.SaveChangesAsync();

        Assert.Empty(change);
        Assert.Equal(2, fixture.Db.RouterObservations.Count());
    }
}

/// <summary>
/// The identity of a port mapping. Take in too little and two different
/// mappings count as the same one, and one of them goes unnoticed.
/// </summary>
public class PortKeyTests
{
    private static RouterPortMapping Mapping(
        string log = "TCP", string outside = "8080", string remote = "") =>
        new("Beschreibung", log, outside, "8080", "192.168.1.29", true, remote);

    [Fact]
    public void Protocol_and_port_form_the_identity()
    {
        Assert.Equal("TCP/8080/*", RouterWatchService.Key(Mapping()));
    }

    [Fact]
    public void Same_port_different_protocol_is_something_else()
    {
        Assert.NotEqual(
            RouterWatchService.Key(Mapping(log: "TCP")),
            RouterWatchService.Key(Mapping(log: "UDP")));
    }

    /// <summary>
    /// "From anywhere" is the more dangerous case and has to stay
    /// distinguishable from "only from this one address".
    /// </summary>
    [Fact]
    public void Open_to_all_is_not_the_same_as_a_fixed_remote_end()
    {
        Assert.Equal("TCP/8080/*", RouterWatchService.Key(Mapping(remote: "")));
        Assert.Equal("TCP/8080/203.0.113.7",
            RouterWatchService.Key(Mapping(remote: "203.0.113.7")));
    }

    /// <summary>
    /// For "from anywhere" the Fritz!Box does not write nothing but
    /// <c>0.0.0.0</c> — measured against the two mappings that really stood
    /// on the box. Whoever knows only the empty value classifies the mapping
    /// open to the whole world as the more harmless one of all things.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("*")]
    public void The_router_writes_from_anywhere_in_several_ways(string remote)
    {
        Assert.True(RouterWatchService.ForAll(remote));
        Assert.Equal("TCP/8080/*", RouterWatchService.Key(Mapping(remote: remote)));
    }

    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("198.51.100.0")]
    public void A_real_remote_end_is_not_from_anywhere(string remote)
    {
        Assert.False(RouterWatchService.ForAll(remote));
    }
}
