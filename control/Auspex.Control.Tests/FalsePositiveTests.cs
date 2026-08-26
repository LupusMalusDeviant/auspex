using Auspex.Control.Services;
using Auspex.Control.Services.Localization;

namespace Auspex.Control.Tests;

public class FalsePositiveTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 14, 0, 0, DateTimeKind.Utc);

    private static DetectionContext Context() => new(Now.AddHours(-1), Now, Now.AddDays(-14), true);

    /// <summary>The core: dense repetition means "broken", not "advertising".</summary>
    [Fact]
    public async Task Dense_repetition_is_reported_as_a_false_alarm()
    {
        using var fixture = new TestDb();
        // 25 attempts within a minute - a loop.
        for (var i = 0; i < 25; i++)
        {
            fixture.Seed("192.168.1.43", "api.hersteller.example",
                Now.AddMinutes(-30).AddSeconds(i * 2), action: "blocked",
                clientName: "Fernseher Wohnzimmer", list: "hagezi-multi-pro");
        }

        var findings = await Detectors.FalsePositiveAsync(fixture.Db, Context(), default);

        var finding = Assert.Single(findings);
        Assert.Equal("fehlalarm-verdacht", finding.Detector);
        Assert.Equal("warn", finding.Severity);
        Assert.Equal("@@||api.hersteller.example^", finding.Suggestion);
        Assert.Contains("Fernseher Wohnzimmer", new StringsDe().Finding(finding).Titel);
        Assert.Contains("Fernseher Wohnzimmer", new StringsEn().Finding(finding).Titel);
    }

    /// <summary>
    /// Spread-out calls are ordinary browsing. If that were reported, the
    /// detector would be switched off after a day.
    /// </summary>
    [Fact]
    public async Task Blocks_spread_over_the_hour_are_not_reported()
    {
        using var fixture = new TestDb();
        for (var i = 0; i < 30; i++)
        {
            fixture.Seed("192.168.1.43", "werbung.example",
                Now.AddMinutes(-59 + i * 2), action: "blocked");
        }

        var findings = await Detectors.FalsePositiveAsync(fixture.Db, Context(), default);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task A_few_attempts_are_not_enough()
    {
        using var fixture = new TestDb();
        for (var i = 0; i < 5; i++)
        {
            fixture.Seed("192.168.1.43", "selten.example",
                Now.AddMinutes(-30).AddSeconds(i), action: "blocked");
        }

        Assert.Empty(await Detectors.FalsePositiveAsync(fixture.Db, Context(), default));
    }

    [Fact]
    public async Task Queries_that_were_not_blocked_do_not_count()
    {
        using var fixture = new TestDb();
        for (var i = 0; i < 25; i++)
        {
            fixture.Seed("192.168.1.43", "normal.example",
                Now.AddMinutes(-30).AddSeconds(i * 2));
        }

        Assert.Empty(await Detectors.FalsePositiveAsync(fixture.Db, Context(), default));
    }

    [Fact]
    public async Task The_evidence_names_the_blocking_rule()
    {
        using var fixture = new TestDb();
        for (var i = 0; i < 12; i++)
        {
            fixture.Seed("10.0.0.5", "dienst.example",
                Now.AddMinutes(-20).AddSeconds(i * 3), action: "blocked", list: "meine-liste");
        }

        var finding = Assert.Single(await Detectors.FalsePositiveAsync(fixture.Db, Context(), default));

        Assert.Contains("meine-liste", new StringsDe().Finding(finding).Numbers);
        Assert.Contains("12 Versuche", new StringsDe().Finding(finding).Numbers);
        Assert.Contains("meine-liste", new StringsEn().Finding(finding).Numbers);
        Assert.Contains("12 attempts", new StringsEn().Finding(finding).Numbers);
    }
}

public class SuggestionScopeTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 14, 0, 0, DateTimeKind.Utc);
    private static DetectionContext Context() => new(Now.AddHours(-1), Now, Now.AddDays(-14), true);

    /// <summary>
    /// Opening up the whole provider because of a single telemetry host
    /// would be the wrong reaction. If only one name was affected, the
    /// exception applies only to it.
    /// </summary>
    [Fact]
    public async Task One_affected_name_yields_a_narrow_exception()
    {
        using var fixture = new TestDb();
        for (var i = 0; i < 20; i++)
        {
            fixture.Db.Queries.Add(new Auspex.Control.Data.QueryRecord
            {
                Seq = i + 1,
                Boot = "test",
                TimeUtc = Now.AddMinutes(-30).AddSeconds(i * 2),
                Client = "192.168.1.43",
                Name = "analytics.tiktok.com",
                Domain = "tiktok.com",
                Type = "A",
                Action = "blocked",
                Source = "filter",
                Rcode = "NXDOMAIN",
            });
        }
        await fixture.Db.SaveChangesAsync();

        var finding = Assert.Single(await Detectors.FalsePositiveAsync(fixture.Db, Context(), default));

        Assert.Equal("@@||analytics.tiktok.com^", finding.Suggestion);
    }

    [Fact]
    public async Task Several_affected_names_yield_the_domain()
    {
        using var fixture = new TestDb();
        for (var i = 0; i < 20; i++)
        {
            fixture.Db.Queries.Add(new Auspex.Control.Data.QueryRecord
            {
                Seq = i + 1,
                Boot = "test",
                TimeUtc = Now.AddMinutes(-30).AddSeconds(i * 2),
                Client = "192.168.1.43",
                Name = $"host{i}.anbieter.example",
                Domain = "anbieter.example",
                Type = "A",
                Action = "blocked",
                Source = "filter",
                Rcode = "NXDOMAIN",
            });
        }
        await fixture.Db.SaveChangesAsync();

        var finding = Assert.Single(await Detectors.FalsePositiveAsync(fixture.Db, Context(), default));

        Assert.Equal("@@||anbieter.example^", finding.Suggestion);
    }
}
