using Auspex.Control.Data;
using Auspex.Control.Services;
using Auspex.Control.Services.Localization;

namespace Auspex.Control.Tests;

/// <summary>
/// The blind spot this detector closes: "repetitionburst" compares against
/// its own history and sees only spikes. A device running against a block
/// equally loudly for days has no spike — factor one — and stayed invisible
/// even though it caused most of the load. Measured for real: 486 queries
/// for one blocked name in 46 minutes, not a single finding.
/// </summary>
public class SteadyTalkerTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 14, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A short baseline so the test does not have to sow twenty thousand
    /// rows: three hours beforehand are enough to represent "for a while
    /// now".
    /// </summary>
    private static DetectionContext Context() => new(Now.AddHours(-1), Now, Now.AddHours(-4), true);

    /// <summary>Evenly across the hour, with history at the same level.</summary>
    private static void SowSteadyTalker(TestDb fixture, string domain = "unagi.amazon.example")
    {
        // Baseline: 180 queries over three hours = 60/h.
        for (var i = 0; i < 180; i++)
        {
            fixture.Seed("192.168.1.29", domain,
                Now.AddHours(-4).AddMinutes(i), action: "blocked",
                clientName: "Arbeitslaptop", list: "hagezi-multi-pro");
        }

        // Window: 130 queries over 50 minutes.
        for (var i = 0; i < 130; i++)
        {
            fixture.Seed("192.168.1.29", domain,
                Now.AddMinutes(-55).AddSeconds(i * 23), action: "blocked",
                clientName: "Arbeitslaptop", list: "hagezi-multi-pro");
        }
    }

    [Fact]
    public async Task A_steady_talker_is_reported()
    {
        using var fixture = new TestDb();
        SowSteadyTalker(fixture);

        var findings = await Detectors.SteadyTalkerAsync(fixture.Db, Context(), default);

        var finding = Assert.Single(findings);
        Assert.Equal("dauersender", finding.Detector);
        Assert.Equal("info", finding.Severity);
        foreach (Strings t in new Strings[] { new StringsDe(), new StringsEn() })
        {
            Assert.Contains("Arbeitslaptop", t.Finding(finding).Titel);
            Assert.Contains("unagi.amazon.example", t.Finding(finding).Titel);
        }
    }

    /// <summary>
    /// An exception would be the wrong answer here: whoever blocks telemetry
    /// wants it to stay blocked. The interface creates an allow rule from
    /// <c>Suggestion</c> without asking — if one stood here, a click would
    /// lead to exactly the opposite of what the finding says.
    /// </summary>
    [Fact]
    public async Task The_finding_suggests_no_exception()
    {
        using var fixture = new TestDb();
        SowSteadyTalker(fixture);

        var finding = Assert.Single(await Detectors.SteadyTalkerAsync(fixture.Db, Context(), default));

        Assert.Null(finding.Suggestion);
    }

    /// <summary>
    /// A state that reports itself hourly becomes wallpaper. The fingerprint
    /// therefore carries the day, not the hour — and the detector sets it
    /// itself instead of having it handed over by the store.
    /// </summary>
    [Fact]
    public async Task It_is_reported_at_most_once_a_day()
    {
        using var fixture = new TestDb();
        SowSteadyTalker(fixture);

        var finding = Assert.Single(await Detectors.SteadyTalkerAsync(fixture.Db, Context(), default));

        Assert.Equal("dauersender|192.168.1.29|unagi.amazon.example|20260824", finding.Fingerprint);
    }

    /// <summary>
    /// The same hourly figure, but dumped in five minutes: that is an
    /// outburst and belongs to the other detector. Without this check both
    /// would report the same event.
    /// </summary>
    [Fact]
    public async Task A_short_outburst_does_not_belong_here()
    {
        using var fixture = new TestDb();
        for (var i = 0; i < 180; i++)
        {
            fixture.Seed("192.168.1.29", "api.example",
                Now.AddHours(-4).AddMinutes(i), action: "blocked");
        }
        for (var i = 0; i < 130; i++)
        {
            fixture.Seed("192.168.1.29", "api.example",
                Now.AddMinutes(-30).AddSeconds(i * 2), action: "blocked");
        }

        Assert.Empty(await Detectors.SteadyTalkerAsync(fixture.Db, Context(), default));
    }

    /// <summary>
    /// Without history it is not "steady" but new. Otherwise every
    /// freshly installed app would count as a steady talker on its first
    /// day.
    /// </summary>
    [Fact]
    public async Task Without_history_no_steady_talker()
    {
        using var fixture = new TestDb();
        for (var i = 0; i < 130; i++)
        {
            fixture.Seed("192.168.1.29", "neu.example",
                Now.AddMinutes(-55).AddSeconds(i * 23), action: "blocked");
        }

        Assert.Empty(await Detectors.SteadyTalkerAsync(fixture.Db, Context(), default));
    }

    /// <summary>
    /// A genuine spike - ten times the usual - is a storm and not a baseline
    /// level. Otherwise both detectors report the same hour.
    /// </summary>
    [Fact]
    public async Task A_genuine_spike_stays_with_the_storm()
    {
        using var fixture = new TestDb();
        // Grundlinie 60/h ...
        for (var i = 0; i < 180; i++)
        {
            fixture.Seed("192.168.1.29", "api.example",
                Now.AddHours(-4).AddMinutes(i), action: "blocked");
        }
        // ... and ten times that in the window, evenly spread.
        for (var i = 0; i < 600; i++)
        {
            fixture.Seed("192.168.1.29", "api.example",
                Now.AddMinutes(-55).AddSeconds(i * 5), action: "blocked");
        }

        Assert.Empty(await Detectors.SteadyTalkerAsync(fixture.Db, Context(), default));
    }

    /// <summary>
    /// Allowed queries are not a finding. The detector describes load
    /// against a block, not a talkative device as such.
    /// </summary>
    [Fact]
    public async Task Allowed_queries_do_not_count()
    {
        using var fixture = new TestDb();
        for (var i = 0; i < 180; i++)
        {
            fixture.Seed("192.168.1.29", "sync.example", Now.AddHours(-4).AddMinutes(i));
        }
        for (var i = 0; i < 130; i++)
        {
            fixture.Seed("192.168.1.29", "sync.example", Now.AddMinutes(-55).AddSeconds(i * 23));
        }

        Assert.Empty(await Detectors.SteadyTalkerAsync(fixture.Db, Context(), default));
    }
}

/// <summary>
/// The false-alarm heuristic assumed a repetition loop meant "something is
/// broken right now". For telemetry that is not true: it keeps asking at a
/// fixed rate, nobody wants the suggested exception, and the finding comes
/// back every hour. Measured, it accounted for 123 of 131 findings and
/// buried the other five.
/// </summary>
public class FalseAlarmSteadyStateTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 14, 0, 0, DateTimeKind.Utc);

    private static DetectionContext Context() => new(Now.AddHours(-1), Now, Now.AddDays(-14), true);

    private static void SowLoop(TestDb fixture)
    {
        for (var i = 0; i < 25; i++)
        {
            fixture.Seed("192.168.1.29", "sessions.bugsnag.example",
                Now.AddMinutes(-30).AddSeconds(i * 2), action: "blocked",
                clientName: "Arbeitslaptop", list: "hagezi-multi-pro");
        }
    }

    private static void SowEarlierFinding(TestDb fixture, int daysBack)
    {
        var time = Now.AddDays(-daysBack);
        fixture.Db.Findings.Add(new Finding
        {
            Detector = "fehlalarm-verdacht",
            Severity = "warn",
            Client = "192.168.1.29",
            Subject = "sessions.bugsnag.example",
            Title = "seen before",
            DetectedUtc = time,
            WindowStartUtc = time.AddHours(-1),
            WindowEndUtc = time,
            Fingerprint = $"fehlalarm-verdacht|192.168.1.29|sessions.bugsnag.example|{time:yyyyMMddHH}",
        });
        fixture.Db.SaveChanges();
    }

    [Fact]
    public async Task The_first_time_it_is_reported()
    {
        using var fixture = new TestDb();
        SowLoop(fixture);

        Assert.Single(await Detectors.FalsePositiveAsync(fixture.Db, Context(), default));
    }

    [Fact]
    public async Task Still_on_the_second_day()
    {
        using var fixture = new TestDb();
        SowLoop(fixture);
        SowEarlierFinding(fixture, 1);

        // Once can be chance - twice is not yet a pattern, and a detector
        // that falls silent too early keeps real false alarms quiet.
        Assert.Single(await Detectors.FalsePositiveAsync(fixture.Db, Context(), default));
    }

    [Fact]
    public async Task From_the_third_day_it_is_no_longer_a_false_alarm()
    {
        using var fixture = new TestDb();
        SowLoop(fixture);
        SowEarlierFinding(fixture, 1);
        SowEarlierFinding(fixture, 2);

        Assert.Empty(await Detectors.FalsePositiveAsync(fixture.Db, Context(), default));
    }

    /// <summary>
    /// Several findings on one day are one day, not three. Otherwise the
    /// detector would fall silent after three hours of the same hour.
    /// </summary>
    [Fact]
    public async Task Several_findings_on_the_same_day_count_once()
    {
        using var fixture = new TestDb();
        SowLoop(fixture);
        SowEarlierFinding(fixture, 1);
        var time = Now.AddDays(-1).AddHours(-3);
        fixture.Db.Findings.Add(new Finding
        {
            Detector = "fehlalarm-verdacht",
            Client = "192.168.1.29",
            Subject = "sessions.bugsnag.example",
            DetectedUtc = time,
            WindowStartUtc = time.AddHours(-1),
            WindowEndUtc = time,
            Fingerprint = "anderer-abdruck",
        });
        fixture.Db.SaveChanges();

        Assert.Single(await Detectors.FalsePositiveAsync(fixture.Db, Context(), default));
    }

    /// <summary>
    /// Whatever was more than a week ago is the past. A false alarm coming
    /// back after a long quiet spell should be noticed again.
    /// </summary>
    [Fact]
    public async Task Very_old_findings_no_longer_count()
    {
        using var fixture = new TestDb();
        SowLoop(fixture);
        SowEarlierFinding(fixture, 9);
        SowEarlierFinding(fixture, 10);

        Assert.Single(await Detectors.FalsePositiveAsync(fixture.Db, Context(), default));
    }

    /// <summary>
    /// A different device is a different case - the same domain says nothing
    /// about whether it is broken over there.
    /// </summary>
    [Fact]
    public async Task The_history_applies_per_device()
    {
        using var fixture = new TestDb();
        SowLoop(fixture);
        SowEarlierFinding(fixture, 1);
        SowEarlierFinding(fixture, 2);

        for (var i = 0; i < 25; i++)
        {
            fixture.Seed("192.168.1.43", "sessions.bugsnag.example",
                Now.AddMinutes(-30).AddSeconds(i * 2), action: "blocked",
                clientName: "Arbeitsrechner");
        }

        var finding = Assert.Single(await Detectors.FalsePositiveAsync(fixture.Db, Context(), default));
        Assert.Equal("192.168.1.43", finding.Client);
    }
}
