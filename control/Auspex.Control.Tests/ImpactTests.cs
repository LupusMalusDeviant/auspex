using Auspex.Control.Services;
using Auspex.Control.Services.Localization;

namespace Auspex.Control.Tests;

public class RuleParserTests
{
    [Theory]
    // The same cases as in the Go parser: the two must not drift apart.
    [InlineData("||tracker.example^", "tracker.example", RuleKind.Suffix, false)]
    [InlineData("@@||shop.example^", "shop.example", RuleKind.Suffix, true)]
    [InlineData("*.ads.example", "ads.example", RuleKind.SubOnly, false)]
    [InlineData("0.0.0.0 exakt.example", "exakt.example", RuleKind.Exact, false)]
    [InlineData("nackt.example", "nackt.example", RuleKind.Suffix, false)]
    [InlineData("TRACKER.Example", "tracker.example", RuleKind.Suffix, false)]
    public void Formats_are_read_the_way_the_data_layer_reads_them(
        string raw, string pattern, RuleKind kind, bool isAllow)
    {
        var rule = RuleParser.Parse(raw);

        Assert.NotNull(rule);
        Assert.Equal(pattern, rule.Pattern);
        Assert.Equal(kind, rule.Kind);
        Assert.Equal(isAllow, rule.IsAllow);
    }

    [Theory]
    [InlineData("! Kommentar")]
    [InlineData("# Kommentar")]
    [InlineData("")]
    [InlineData("||example.com^$third-party")]  // modifier: cannot be expressed in DNS
    [InlineData("example.com##.banner")]        // Cosmetic-Filter
    [InlineData("192.168.1.1")]                 // a bare IP is not a domain
    [InlineData("localhost")]                   // no dot
    public void Lines_that_cannot_be_expressed_are_rejected(string raw)
    {
        Assert.Null(RuleParser.Parse(raw));
    }
}

public class ImpactTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static void Fill(TestDb fixture)
    {
        // Already blocked today.
        fixture.Seed("192.168.1.43", "tracker.example", Now.AddDays(-3), count: 40,
            action: "blocked", clientName: "Arbeitsrechner");
        // Gets through today.
        fixture.Seed("192.168.1.43", "shop.example", Now.AddDays(-2), count: 30,
            namePrefix: "www", clientName: "Arbeitsrechner");
        fixture.Seed("192.168.1.50", "shop.example", Now.AddDays(-1), count: 12,
            namePrefix: "api", clientName: "Kinder-Tablet");
        fixture.Seed("192.168.1.50", "sonst.example", Now.AddDays(-1), count: 5);
    }

    [Fact]
    public async Task A_block_rule_shows_what_would_newly_be_blocked()
    {
        using var fixture = new TestDb();
        Fill(fixture);
        var svc = new ImpactService(fixture.Db);

        var result = await svc.AnalyzeAsync("||shop.example^", TimeSpan.FromDays(30));

        Assert.NotNull(result);
        Assert.Equal(42, result.Matches);       // 30 + 12 Subdomains
        Assert.Equal(0, result.AlreadyBlocked);
        Assert.Equal(42, result.WouldChange);
        Assert.Equal(2, result.Clients);
        // The sentence for it belongs to the language layer, not to the result
        // - so what stands here is what it makes of the figures.
        Assert.Contains("geblockt",
            new StringsDe().ImpactSentence(result.Rule.IsAllow, result.WouldChange));
        Assert.Contains("blocked",
            new StringsEn().ImpactSentence(result.Rule.IsAllow, result.WouldChange));
    }

    [Fact]
    public async Task An_exception_shows_what_would_get_through_again()
    {
        using var fixture = new TestDb();
        Fill(fixture);
        var svc = new ImpactService(fixture.Db);

        var result = await svc.AnalyzeAsync("@@||tracker.example^", TimeSpan.FromDays(30));

        Assert.NotNull(result);
        Assert.Equal(40, result.Matches);
        Assert.Equal(40, result.AlreadyBlocked);
        // This is exactly the figure that counts: what the rule really
        // changes.
        Assert.Equal(40, result.WouldChange);
        Assert.Contains("durchgelassen",
            new StringsDe().ImpactSentence(result.Rule.IsAllow, result.WouldChange));
        Assert.Contains("let through",
            new StringsEn().ImpactSentence(result.Rule.IsAllow, result.WouldChange));
    }

    [Fact]
    public async Task A_block_rule_on_already_blocked_things_changes_nothing()
    {
        using var fixture = new TestDb();
        Fill(fixture);
        var svc = new ImpactService(fixture.Db);

        var result = await svc.AnalyzeAsync("||tracker.example^", TimeSpan.FromDays(30));

        Assert.NotNull(result);
        Assert.Equal(40, result.Matches);
        Assert.Equal(0, result.WouldChange);
    }

    [Fact]
    public async Task A_wildcard_does_not_hit_the_apex()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.0.1", "wild.example", Now.AddHours(-2), count: 10);            // Apex
        fixture.Seed("10.0.0.1", "wild.example", Now.AddHours(-2), count: 7, namePrefix: "sub");

        var result = await new ImpactService(fixture.Db).AnalyzeAsync("*.wild.example", TimeSpan.FromDays(1));

        Assert.NotNull(result);
        Assert.Equal(7, result.Matches);
    }

    [Fact]
    public async Task A_hosts_entry_matches_only_exactly()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.0.1", "exakt.example", Now.AddHours(-2), count: 9);            // exakt
        fixture.Seed("10.0.0.1", "exakt.example", Now.AddHours(-2), count: 4, namePrefix: "sub");

        var result = await new ImpactService(fixture.Db).AnalyzeAsync("0.0.0.0 exakt.example", TimeSpan.FromDays(1));

        Assert.NotNull(result);
        Assert.Equal(9, result.Matches);
    }

    [Fact]
    public async Task An_unknown_rule_returns_null()
    {
        using var fixture = new TestDb();
        Assert.Null(await new ImpactService(fixture.Db).AnalyzeAsync("example.com##.banner", TimeSpan.FromDays(1)));
    }

    [Fact]
    public async Task With_no_hits_the_analysis_stays_empty_but_valid()
    {
        using var fixture = new TestDb();
        Fill(fixture);

        var result = await new ImpactService(fixture.Db).AnalyzeAsync("||gibtsnicht.example^", TimeSpan.FromDays(30));

        Assert.NotNull(result);
        Assert.Equal(0, result.Matches);
        Assert.Empty(result.TopClients);
    }
}
