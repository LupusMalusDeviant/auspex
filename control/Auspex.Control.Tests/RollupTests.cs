using Microsoft.Extensions.Logging.Abstractions;
using Auspex.Control.Services;

namespace Auspex.Control.Tests;

public class RollupTests
{
    private static RollupService Build(TestDb fixture)
        => new(fixture.Db, NullLogger<RollupService>.Instance);

    [Fact]
    public async Task Completed_days_are_rolled_up()
    {
        using var fixture = new TestDb();
        var yesterday = DateTime.UtcNow.Date.AddDays(-1);

        fixture.Seed("10.0.0.1", "gut.example", yesterday.AddHours(9), count: 70, clientName: "Handy");
        fixture.Seed("10.0.0.1", "boese.example", yesterday.AddHours(10), count: 30, action: "blocked");
        fixture.Seed("10.0.0.2", "gut.example", yesterday.AddHours(11), count: 20);

        var rolledUp = await Build(fixture).RunAsync();

        Assert.Equal(1, rolledUp);
        var day = Assert.Single(fixture.Db.DailyTotals);
        Assert.Equal(120, day.Total);
        Assert.Equal(30, day.Blocked);
        Assert.Equal(2, day.Clients);
        Assert.Equal(2, day.Domains);

        Assert.Equal(2, fixture.Db.DailyClients.Count());
        Assert.Equal(2, fixture.Db.DailyDomains.Count());
    }

    /// <summary>
    /// The current day is not finished yet. Rolling it up would mean storing
    /// half a measurement as a whole one - and it could never be corrected
    /// afterwards.
    /// </summary>
    [Fact]
    public async Task Today_is_not_rolled_up()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.0.1", "heute.example", DateTime.UtcNow.AddHours(-1), count: 10);

        Assert.Equal(0, await Build(fixture).RunAsync());
        Assert.Empty(fixture.Db.DailyTotals);
    }

    [Fact]
    public async Task Rolling_up_twice_produces_no_duplicates()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.0.1", "gestern.example", DateTime.UtcNow.Date.AddDays(-1).AddHours(9), count: 10);

        var rollup = Build(fixture);
        Assert.Equal(1, await rollup.RunAsync());
        Assert.Equal(0, await rollup.RunAsync());
        Assert.Single(fixture.Db.DailyTotals);
    }

    [Fact]
    public async Task Days_without_raw_data_yield_no_daily_total()
    {
        using var fixture = new TestDb();
        // Two days apart - the day in between was quiet.
        fixture.Seed("10.0.0.1", "a.example", DateTime.UtcNow.Date.AddDays(-3).AddHours(9), count: 5);
        fixture.Seed("10.0.0.1", "b.example", DateTime.UtcNow.Date.AddDays(-1).AddHours(9), count: 5);

        await Build(fixture).RunAsync();

        // A daily total of zeros would feign a measurement that never
        // happened - the time series fills gaps in anyway.
        Assert.Equal(2, fixture.Db.DailyTotals.Count());
    }

    [Fact]
    public async Task Daily_totals_survive_the_deletion_of_the_raw_data()
    {
        using var fixture = new TestDb();
        var yesterday = DateTime.UtcNow.Date.AddDays(-1);
        fixture.Seed("10.0.0.1", "alt.example", yesterday.AddHours(9), count: 50, action: "blocked");

        await Build(fixture).RunAsync();

        // Rohdaten weg, wie es die Aufbewahrungsfrist irgendwann tut.
        fixture.Db.Queries.RemoveRange(fixture.Db.Queries);
        await fixture.Db.SaveChangesAsync();

        var longTerm = new LongTermService(fixture.Db);
        var overview = await longTerm.GetOverviewAsync(30);

        Assert.Equal(50, overview.Total);
        Assert.Equal(50, overview.Blocked);
        Assert.Equal(1, overview.BlockRate);
    }

    [Fact]
    public async Task Old_daily_totals_are_removed_after_the_retention()
    {
        using var fixture = new TestDb();
        fixture.Db.DailyTotals.Add(new Auspex.Control.Data.DailyTotal
        {
            Day = DateTime.UtcNow.Date.AddDays(-800), Total = 1,
        });
        fixture.Db.DailyTotals.Add(new Auspex.Control.Data.DailyTotal
        {
            Day = DateTime.UtcNow.Date.AddDays(-10), Total = 1,
        });
        await fixture.Db.SaveChangesAsync();

        await Build(fixture).PruneAsync(730);

        Assert.Single(fixture.Db.DailyTotals);
    }

    [Fact]
    public async Task The_time_series_fills_quiet_days()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.0.1", "a.example", DateTime.UtcNow.Date.AddDays(-5).AddHours(9), count: 5);
        fixture.Seed("10.0.0.1", "b.example", DateTime.UtcNow.Date.AddDays(-1).AddHours(9), count: 5);
        await Build(fixture).RunAsync();

        var buckets = await new LongTermService(fixture.Db).GetTimelineAsync(7);

        Assert.Equal(7, buckets.Count);
        Assert.Contains(buckets, b => b.Total == 0);
        Assert.Equal(10, buckets.Sum(b => b.Total));
    }
}
