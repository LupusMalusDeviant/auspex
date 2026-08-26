using Auspex.Control.Services;

namespace Auspex.Control.Tests;

public class IngestTests
{
    [Theory]
    // Ordinary names: the longest label left of the domain.
    [InlineData("api.hersteller.example", "hersteller.example", 3)]
    [InlineData("hersteller.example", "hersteller.example", 0)]
    [InlineData("a.b.c.hersteller.example", "hersteller.example", 1)]
    // Tunneling: kodierte Nutzdaten im Namen.
    [InlineData("aGVsbG8gd29ybGQgdGhpcyBpcyBkYXRh.tunnel.example", "tunnel.example", 32)]
    // Domain unknown: then the whole name counts.
    [InlineData("irgendwas.example", null, 9)]
    [InlineData("", "example", 0)]
    public void LongestLabel_finds_the_longest_label(string name, string? domain, int expected)
    {
        Assert.Equal(expected, IngestService.LongestLabel(name, domain));
    }
}

public class AnalyticsTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task The_time_series_fills_quiet_hours_with_zero()
    {
        using var fixture = new TestDb();
        var svc = new AnalyticsService(fixture.Db);

        // Zwei Anfragen, sechs Stunden auseinander - dazwischen Stille.
        fixture.Seed("10.0.0.1", "a.example", Now.AddHours(-7));
        fixture.Seed("10.0.0.1", "b.example", Now.AddHours(-1));

        var timeline = await svc.GetTimelineAsync(TimeSpan.FromHours(8));

        // Without filling in, the quiet spell would simply vanish from the
        // chart instead of being visible as a zero.
        Assert.True(timeline.Count >= 8, $"nur {timeline.Count} Stundenwerte");
        Assert.Equal(2, timeline.Sum(b => b.Total));
        Assert.Contains(timeline, b => b.Total == 0);
    }

    [Fact]
    public async Task The_overview_computes_the_block_rate()
    {
        using var fixture = new TestDb();
        var svc = new AnalyticsService(fixture.Db);

        fixture.Seed("10.0.0.1", "gut.example", Now.AddMinutes(-10), count: 75);
        fixture.Seed("10.0.0.1", "boese.example", Now.AddMinutes(-10), count: 25, action: "blocked");

        var overview = await svc.GetOverviewAsync(TimeSpan.FromHours(1));

        Assert.Equal(100, overview.Total);
        Assert.Equal(25, overview.Blocked);
        Assert.Equal(0.25, overview.BlockRate, 3);
        Assert.Equal(2, overview.Domains);
        Assert.Equal(1, overview.Clients);
    }

    [Fact]
    public async Task TopDomains_groups_by_registrable_domain()
    {
        using var fixture = new TestDb();
        var svc = new AnalyticsService(fixture.Db);

        // Three different host names, one domain - that is exactly what the
        // data layer supplies the registrable domain for.
        fixture.Seed("10.0.0.1", "cdn.example", Now.AddMinutes(-5), count: 30, namePrefix: "host");
        fixture.Seed("10.0.0.2", "cdn.example", Now.AddMinutes(-5), count: 10, namePrefix: "andere");
        fixture.Seed("10.0.0.1", "klein.example", Now.AddMinutes(-5), count: 5);

        var top = await svc.GetTopDomainsAsync(TimeSpan.FromHours(1), blockedOnly: false);

        Assert.Equal("cdn.example", top[0].Domain);
        Assert.Equal(40, top[0].Total);
        Assert.Equal(2, top[0].Clients);
    }
}

public class ListStatsTests
{
    [Fact]
    public async Task TopLists_counts_blocks_per_list()
    {
        using var fixture = new TestDb();
        var svc = new AnalyticsService(fixture.Db);
        var now = DateTime.UtcNow;

        fixture.Seed("10.0.0.1", "a.example", now.AddMinutes(-5), count: 12, action: "blocked", list: "hagezi");
        fixture.Seed("10.0.0.1", "b.example", now.AddMinutes(-5), count: 3, action: "blocked", list: "eigene");
        // Queries that were not blocked must not count.
        fixture.Seed("10.0.0.1", "c.example", now.AddMinutes(-5), count: 40);

        var lists = await svc.GetTopListsAsync(TimeSpan.FromHours(1));

        Assert.Equal(2, lists.Count);
        Assert.Equal("hagezi", lists[0].List);
        Assert.Equal(12, lists[0].Blocked);
    }
}

public class DnssecTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task The_validation_rate_counts_only_upstream_answers()
    {
        using var fixture = new TestDb();
        var svc = new AnalyticsService(fixture.Db);

        fixture.Seed("10.0.0.1", "signiert.example", Now.AddMinutes(-5), count: 60, validated: true);
        fixture.Seed("10.0.0.1", "unsigniert.example", Now.AddMinutes(-5), count: 40);
        // Neither blocks nor cache hits belong in the denominator - otherwise
        // the rate measures the cache hit rate as well.
        fixture.Seed("10.0.0.1", "geblockt.example", Now.AddMinutes(-5), count: 100, action: "blocked");
        fixture.Seed("10.0.0.1", "gecacht.example", Now.AddMinutes(-5), count: 200, source: "cache");

        var overview = await svc.GetOverviewAsync(TimeSpan.FromHours(1));

        Assert.Equal(60, overview.Validated);
        Assert.Equal(100, overview.Upstream);
        Assert.Equal(0.6, overview.ValidatedRate, 3);
    }

    [Fact]
    public async Task With_no_upstream_answers_the_rate_is_zero_not_a_division_by_zero()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.0.1", "only.example", Now.AddMinutes(-5), count: 10, action: "blocked");

        var overview = await new AnalyticsService(fixture.Db).GetOverviewAsync(TimeSpan.FromHours(1));

        Assert.Equal(0, overview.ValidatedRate);
    }
}
