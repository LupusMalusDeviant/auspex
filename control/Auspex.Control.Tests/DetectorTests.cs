using Auspex.Control.Services;
using Auspex.Control.Services.Localization;

namespace Auspex.Control.Tests;

public class DetectorTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 14, 0, 0, DateTimeKind.Utc);

    private static DetectionContext Context(bool hasBaseline = true, int baselineHours = 336) => new(
        WindowStartUtc: Now.AddHours(-1),
        WindowEndUtc: Now,
        BaselineStartUtc: Now.AddHours(-1 - baselineHours),
        HasBaseline: hasBaseline);

    [Fact]
    public async Task NewDomain_reports_only_genuinely_new_domains()
    {
        using var fixture = new TestDb();
        // History: the client has known bekannt.example for days.
        fixture.Seed("10.0.5.20", "bekannt.example", Now.AddDays(-5), count: 20);
        // In the window: the known one and a new one.
        fixture.Seed("10.0.5.20", "bekannt.example", Now.AddMinutes(-30), count: 10);
        fixture.Seed("10.0.5.20", "neu.example", Now.AddMinutes(-20), count: 8);

        var findings = await Detectors.NewDomainAsync(fixture.Db, Context(), default);

        var finding = Assert.Single(findings);
        Assert.Equal("neu.example", finding.Subject);
        Assert.Equal("neue-domain", finding.Detector);
    }

    [Fact]
    public async Task NewDomain_stays_silent_without_a_baseline()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.5.20", "neu.example", Now.AddMinutes(-20), count: 50);

        var findings = await Detectors.NewDomainAsync(fixture.Db, Context(hasBaseline: false), default);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task NewDomain_ignores_one_off_queries()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.5.20", "bekannt.example", Now.AddDays(-5), count: 20);
        // Only two queries - below the threshold of five.
        fixture.Seed("10.0.5.20", "streifschuss.example", Now.AddMinutes(-10), count: 2);

        var findings = await Detectors.NewDomainAsync(fixture.Db, Context(), default);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task NxdomainFlood_only_fires_above_the_threshold()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.5.30", "ok.example", Now.AddMinutes(-30), count: 40);
        fixture.Seed("10.0.5.30", "weg.example", Now.AddMinutes(-30), count: 60, rcode: "NXDOMAIN");

        var findings = await Detectors.NxdomainFloodAsync(fixture.Db, Context(), default);

        var finding = Assert.Single(findings);
        Assert.Equal("nxdomain-flut", finding.Detector);
        Assert.Contains("60 von 100", new StringsDe().Finding(finding).Numbers);
        Assert.Contains("60 of 100", new StringsEn().Finding(finding).Numbers);
    }

    [Fact]
    public async Task NxdomainFlood_does_not_count_our_own_blocks()
    {
        using var fixture = new TestDb();
        // The filter answers with NXDOMAIN itself. If that counted, every
        // well filtered connection would be a false alarm.
        fixture.Seed("10.0.5.30", "ok.example", Now.AddMinutes(-30), count: 40);
        fixture.Seed("10.0.5.30", "tracker.example", Now.AddMinutes(-30), count: 60,
            action: "blocked", rcode: "NXDOMAIN");

        var findings = await Detectors.NxdomainFloodAsync(fixture.Db, Context(), default);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task RepetitionBurst_compares_against_its_own_baseline()
    {
        using var fixture = new TestDb();
        // Grundlinie: rund 2 Anfragen pro Stunde ueber 10 Stunden.
        for (var hour = 2; hour <= 11; hour++)
        {
            fixture.Seed("10.0.5.40", "telemetrie.example", Now.AddHours(-hour), count: 2);
        }
        // In the window: 400 queries.
        fixture.Seed("10.0.5.40", "telemetrie.example", Now.AddMinutes(-30), count: 400);

        var findings = await Detectors.RepetitionBurstAsync(fixture.Db, Context(baselineHours: 10), default);

        var finding = Assert.Single(findings);
        Assert.Equal("wiederholungssturm", finding.Detector);
        Assert.Equal("high", finding.Severity);
    }

    [Fact]
    public async Task RepetitionBurst_stays_silent_on_habitual_traffic()
    {
        using var fixture = new TestDb();
        // This device always asks a lot - around 200/h is normal here.
        for (var hour = 2; hour <= 11; hour++)
        {
            fixture.Seed("10.0.5.41", "laut.example", Now.AddHours(-hour), count: 200);
        }
        fixture.Seed("10.0.5.41", "laut.example", Now.AddMinutes(-30), count: 220);

        var findings = await Detectors.RepetitionBurstAsync(fixture.Db, Context(baselineHours: 10), default);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Tunnelling_recognises_many_long_names()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.5.50", "tunnel.example", Now.AddMinutes(-20), count: 80,
            namePrefix: "aGVsbG8gd29ybGQgdGhpcyBpcyBkYXRh", longestLabel: 34);

        var findings = await Detectors.TunnelingAsync(fixture.Db, Context(), default);

        var finding = Assert.Single(findings);
        Assert.Equal("tunneling-verdacht", finding.Detector);
        Assert.Equal("high", finding.Severity);
    }

    [Fact]
    public async Task Tunnelling_ignores_many_short_names()
    {
        using var fixture = new TestDb();
        // An ordinary CDN: many host names, but short labels.
        fixture.Seed("10.0.5.51", "cdn.example", Now.AddMinutes(-20), count: 200,
            namePrefix: "n", longestLabel: 4);

        var findings = await Detectors.TunnelingAsync(fixture.Db, Context(), default);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Tunnelling_ignores_one_long_name_without_variety()
    {
        using var fixture = new TestDb();
        // One long name, asked a hundred times - that is not a tunnel.
        fixture.Seed("10.0.5.52", "lang.example", Now.AddMinutes(-20), count: 100, longestLabel: 40);

        var findings = await Detectors.TunnelingAsync(fixture.Db, Context(), default);

        Assert.Empty(findings);
    }

    // A single blocked name is a warning: some services publish internal
    // addresses on purpose, and one that nobody has allowed yet looks exactly
    // like this from here.
    [Fact]
    public async Task Rebind_reports_a_single_blocked_name_as_a_warning()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.5.20", "attacker.example", Now.AddMinutes(-20),
            count: 4, action: "blocked", rcode: "NXDOMAIN",
            source: "rebind", rule: "192.168.1.1");

        var findings = await Detectors.RebindAsync(fixture.Db, Context(), default);

        var finding = Assert.Single(findings);
        Assert.Equal("rebind", finding.Detector);
        Assert.Equal("warn", finding.Severity);
        Assert.Equal("attacker.example", finding.Subject);
        // Without the address nobody can judge the finding.
        Assert.Contains("192.168.1.1", finding.Values);
    }

    // Several names on one device inside one window is the shape that is hard
    // to explain innocently.
    [Fact]
    public async Task Rebind_escalates_when_several_names_hit_one_device()
    {
        using var fixture = new TestDb();
        foreach (var name in new[] { "a.attacker.example", "b.attacker.example", "c.attacker.example" })
        {
            fixture.Seed("10.0.5.20", name, Now.AddMinutes(-20),
                count: 2, action: "blocked", rcode: "NXDOMAIN",
                source: "rebind", rule: "192.168.1.1");
        }

        var findings = await Detectors.RebindAsync(fixture.Db, Context(), default);

        Assert.Equal(3, findings.Count);
        Assert.All(findings, f => Assert.Equal("high", f.Severity));
    }

    // Must not fire on ordinary blocks. Otherwise every filtered advert
    // arrives as a security finding, and the whole thing gets switched off
    // within a week.
    [Fact]
    public async Task Rebind_ignores_ordinary_blocks()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.5.20", "werbung.example", Now.AddMinutes(-20),
            count: 50, action: "blocked", rcode: "NXDOMAIN", list: "hagezi");
        fixture.Seed("10.0.5.20", "harmlos.example", Now.AddMinutes(-10), count: 20);

        var findings = await Detectors.RebindAsync(fixture.Db, Context(), default);

        Assert.Empty(findings);
    }

    // The question the other two cannot ask: the sensor saw a connection, and
    // no resolution anywhere explains the address.
    [Fact]
    public async Task Unexplained_reports_a_program_that_resolves_elsewhere()
    {
        using var fixture = new TestDb();
        // Resolved properly — must not count.
        fixture.SeedResolution("example.com", "93.184.216.34");
        fixture.SeedConnection("10.0.5.20", "chrome", "93.184.216.34", Now.AddMinutes(-20));
        // Three addresses nothing ever resolved.
        foreach (var ip in new[] { "104.18.1.1", "104.18.2.2", "104.18.3.3" })
        {
            fixture.SeedConnection("10.0.5.20", "chrome", ip, Now.AddMinutes(-20), count: 5);
        }

        var findings = await Detectors.UnexplainedConnectionAsync(fixture.Db, Context(), default);

        var finding = Assert.Single(findings);
        Assert.Equal("unerklaerte-verbindung", finding.Detector);
        Assert.Equal("chrome", finding.Subject);
        // Three unexplained, not four: the resolved one belongs to Auspex.
        Assert.Contains("\"namen\":3", finding.Values);
    }

    // Traffic inside the network never used public DNS, so its absence says
    // nothing at all. Reporting it would bury the real signal in noise.
    [Fact]
    public async Task Unexplained_ignores_traffic_inside_the_network()
    {
        using var fixture = new TestDb();
        foreach (var ip in new[] { "192.168.1.10", "192.168.1.11", "10.0.0.9", "127.0.0.1" })
        {
            fixture.SeedConnection("10.0.5.20", "explorer", ip, Now.AddMinutes(-20), count: 9);
        }

        var findings = await Detectors.UnexplainedConnectionAsync(fixture.Db, Context(), default);

        Assert.Empty(findings);
    }

    // A single hardcoded address is an NTP client or a licence check, not a
    // resolver of its own. Below the threshold it stays quiet.
    [Fact]
    public async Task Unexplained_stays_quiet_below_the_threshold()
    {
        using var fixture = new TestDb();
        fixture.SeedConnection("10.0.5.20", "ntp", "5.9.1.1", Now.AddMinutes(-20), count: 40);
        fixture.SeedConnection("10.0.5.20", "ntp", "5.9.2.2", Now.AddMinutes(-20), count: 40);

        var findings = await Detectors.UnexplainedConnectionAsync(fixture.Db, Context(), default);

        Assert.Empty(findings);
    }

    // Everything resolved properly is the normal case and must produce
    // nothing — otherwise the detector fires on every quiet household.
    [Fact]
    public async Task Unexplained_is_silent_when_everything_was_resolved()
    {
        using var fixture = new TestDb();
        foreach (var (name, ip) in new[]
                 {
                     ("a.example", "93.184.216.34"),
                     ("b.example", "93.184.216.35"),
                     ("c.example", "93.184.216.36"),
                 })
        {
            fixture.SeedResolution(name, ip);
            fixture.SeedConnection("10.0.5.20", "chrome", ip, Now.AddMinutes(-20), count: 7);
        }

        var findings = await Detectors.UnexplainedConnectionAsync(fixture.Db, Context(), default);

        Assert.Empty(findings);
    }

    // An address one device looked up and another one reuses is not a bypass.
    // The join is deliberately against every resolution, not per device.
    [Fact]
    public async Task Unexplained_accepts_an_address_another_device_looked_up()
    {
        using var fixture = new TestDb();
        foreach (var ip in new[] { "93.184.216.34", "93.184.216.35", "93.184.216.36" })
        {
            fixture.SeedResolution("shared.example", ip);
            fixture.SeedConnection("10.0.5.99", "teams", ip, Now.AddMinutes(-20), count: 6);
        }

        var findings = await Detectors.UnexplainedConnectionAsync(fixture.Db, Context(), default);

        Assert.Empty(findings);
    }
}

public class DetectionContextTests
{
    [Fact]
    public void The_baseline_cannot_lie_behind_the_window()
    {
        var now = DateTime.UtcNow;
        var ctx = new DetectionContext(
            WindowStartUtc: now.AddHours(-1),
            WindowEndUtc: now,
            BaselineStartUtc: now.AddHours(-1),
            HasBaseline: false);

        // A tie is the edge case with a fresh database: at least one hour,
        // never zero or negative.
        Assert.True(ctx.BaselineHours >= 1);
        Assert.True((ctx.WindowStartUtc - ctx.BaselineStartUtc).TotalDays >= 0);
    }
}

public class DetectorNameTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_finding_takes_the_device_name_from_the_queries()
    {
        using var fixture = new TestDb();
        fixture.Seed("192.168.1.43", "tunnel.example", Now.AddMinutes(-20), count: 80,
            namePrefix: "aGVsbG8gd29ybGQgdGhpcyBpcyBkYXRh", longestLabel: 34,
            clientName: "Fernseher Wohnzimmer");

        var ctx = new DetectionContext(Now.AddHours(-1), Now, Now.AddDays(-14), true);
        var findings = await Detectors.TunnelingAsync(fixture.Db, ctx, default);

        var finding = Assert.Single(findings);
        Assert.Equal("Fernseher Wohnzimmer", finding.ClientName);
        Assert.Equal("Fernseher Wohnzimmer (192.168.1.43)", finding.ClientLabel);
    }

    [Fact]
    public async Task The_title_names_the_name_rather_than_the_address()
    {
        using var fixture = new TestDb();
        fixture.Seed("192.168.1.50", "ok.example", Now.AddMinutes(-30), count: 40,
            clientName: "Kinder-Tablet");
        fixture.Seed("192.168.1.50", "weg.example", Now.AddMinutes(-30), count: 60,
            rcode: "NXDOMAIN", clientName: "Kinder-Tablet");

        var ctx = new DetectionContext(Now.AddHours(-1), Now, Now.AddDays(-14), true);
        var findings = await Detectors.NxdomainFloodAsync(fixture.Db, ctx, default);

        var finding = Assert.Single(findings);
        // In both languages: the name is the reason the report is usable -
        // "192.168.1.50 runs into nothing" tells nobody anything.
        foreach (Strings t in new Strings[] { new StringsDe(), new StringsEn() })
        {
            Assert.Contains("Kinder-Tablet", t.Finding(finding).Titel);
            Assert.DoesNotContain("192.168.1.50", t.Finding(finding).Titel);
        }
    }
}

public class DetectorScalingTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 14, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The history must not be loaded in full, only the part concerning the
    /// candidates. The test checks the result with a lot of unrelated
    /// history — if the restriction runs into nothing, this would still be
    /// correct here but unbearable in production eventually.
    /// </summary>
    [Fact]
    public async Task NewDomain_stays_correct_with_a_lot_of_foreign_history()
    {
        using var fixture = new TestDb();

        // 400 domains of history that have nothing to do with the window.
        for (var i = 0; i < 400; i++)
        {
            fixture.Seed("10.0.0.1", $"alt{i}.example", Now.AddDays(-5), count: 2);
        }
        // One of them turns up in the window again: not new.
        fixture.Seed("10.0.0.1", "alt7.example", Now.AddMinutes(-20), count: 9);
        // And one genuinely new one.
        fixture.Seed("10.0.0.1", "frisch.example", Now.AddMinutes(-20), count: 9);

        var ctx = new DetectionContext(Now.AddHours(-1), Now, Now.AddDays(-14), true);
        var findings = await Detectors.NewDomainAsync(fixture.Db, ctx, default);

        var finding = Assert.Single(findings);
        Assert.Equal("frisch.example", finding.Subject);
    }
}
