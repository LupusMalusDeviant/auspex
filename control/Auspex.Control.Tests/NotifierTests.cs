using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Auspex.Control.Data;
using Auspex.Control.Services;

namespace Auspex.Control.Tests;

/// <summary>Catches the emitted lines instead of discarding them.</summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Lines.Add(formatter(state, exception));
}

public class NotifierTests
{
    private static Finding MakeFinding(string severity = "high", string? evidence = null) => new()
    {
        DetectedUtc = DateTime.UtcNow,
        WindowStartUtc = DateTime.UtcNow.AddHours(-1),
        WindowEndUtc = DateTime.UtcNow,
        Detector = "tunneling-verdacht",
        Severity = severity,
        Client = "10.0.5.20",
        Subject = "tunnel-test.example",
        Title = "Suspected DNS tunnelling over tunnel-test.example",
        Explanation = "long explanation",
        Evidence = evidence ?? "130 distinct names, longest label 42 characters",
        Score = 130,
        Fingerprint = Guid.NewGuid().ToString(),
    };

    private static (FindingNotifier Notifier, RecordingLogger<FindingNotifier> Logger) Build(
        TestDb fixture, NotificationOptions? options = null)
    {
        var logger = new RecordingLogger<FindingNotifier>();
        var opts = Options.Create(options ?? new NotificationOptions());
        return (new FindingNotifier(fixture.Db, opts, logger), logger);
    }

    [Fact]
    public void The_report_is_a_single_line()
    {
        // Log rules work line by line - a wrapped finding would match only
        // halfway.
        var finding = MakeFinding(evidence: "first line\nsecond line\r\nthird");

        var line = FindingNotifier.Format(finding, "AUSPEX-FUND");

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.StartsWith("AUSPEX-FUND [high] tunneling-verdacht", line);
        Assert.Contains("client=10.0.5.20", line);
        Assert.Contains("subject=tunnel-test.example", line);
    }

    [Fact]
    public async Task Reports_only_from_the_configured_severity()
    {
        using var fixture = new TestDb();
        fixture.Db.Findings.AddRange(MakeFinding("info"), MakeFinding("warn"), MakeFinding("high"));
        await fixture.Db.SaveChangesAsync();

        var (notifier, logger) = Build(fixture, new NotificationOptions { MinSeverity = "warn" });
        var sent = await notifier.FlushAsync();

        Assert.Equal(2, sent);
        Assert.DoesNotContain(logger.Lines, l => l.Contains("[info]"));
    }

    [Fact]
    public async Task Does_not_report_the_same_finding_twice()
    {
        using var fixture = new TestDb();
        fixture.Db.Findings.Add(MakeFinding());
        await fixture.Db.SaveChangesAsync();

        var (notifier, logger) = Build(fixture);

        Assert.Equal(1, await notifier.FlushAsync());
        Assert.Equal(0, await notifier.FlushAsync());
        Assert.Single(logger.Lines);
    }

    [Fact]
    public async Task Findings_already_handled_are_not_reported()
    {
        using var fixture = new TestDb();
        var done = MakeFinding();
        done.Dismissed = true;
        fixture.Db.Findings.Add(done);
        await fixture.Db.SaveChangesAsync();

        var (notifier, _) = Build(fixture);

        Assert.Equal(0, await notifier.FlushAsync());
    }

    [Fact]
    public async Task Old_findings_do_not_go_out_retroactively()
    {
        using var fixture = new TestDb();
        var old = MakeFinding();
        old.DetectedUtc = DateTime.UtcNow.AddDays(-3);
        fixture.Db.Findings.Add(old);
        await fixture.Db.SaveChangesAsync();

        var (notifier, _) = Build(fixture, new NotificationOptions { MaxAge = TimeSpan.FromHours(6) });

        Assert.Equal(0, await notifier.FlushAsync());
    }

    [Fact]
    public async Task The_cap_prevents_a_flood_and_reports_the_rest_together()
    {
        using var fixture = new TestDb();
        for (var i = 0; i < 25; i++)
        {
            fixture.Db.Findings.Add(MakeFinding());
        }
        await fixture.Db.SaveChangesAsync();

        var (notifier, logger) = Build(fixture, new NotificationOptions { MaxPerRun = 5 });
        var sent = await notifier.FlushAsync();

        Assert.Equal(5, sent);
        // Five individual reports plus one collective line.
        Assert.Equal(6, logger.Lines.Count);
        Assert.Contains(logger.Lines, l => l.Contains("[sammel]") && l.Contains("20 weitere"));
        // The ones not reported individually count as handled too, otherwise
        // the flood repeats itself on the next pass.
        Assert.Equal(0, await notifier.FlushAsync());
    }

    [Fact]
    public async Task Switched_off_reports_nothing_at_all()
    {
        using var fixture = new TestDb();
        fixture.Db.Findings.Add(MakeFinding());
        await fixture.Db.SaveChangesAsync();

        var (notifier, logger) = Build(fixture, new NotificationOptions { Enabled = false });

        Assert.Equal(0, await notifier.FlushAsync());
        Assert.Empty(logger.Lines);
    }
}

public class EscalationTests
{
    /// <summary>The existing Whiskers rule, word for word.</summary>
    private static readonly System.Text.RegularExpressions.Regex WhiskersErrorRule = new(
        @"(?i)(unhandled exception|panic:|traceback \(most recent call last\)|segfault|oom-?killed|\bfatal\b|""level""\s*:\s*""(error|critical|fatal|emergency)""|\[(err|error|fatal|crit(ical)?)\])");

    private static Finding Finding(string severity) => new()
    {
        DetectedUtc = DateTime.UtcNow,
        Detector = "tunneling-verdacht",
        Severity = severity,
        Client = "192.168.1.43",
        Subject = "tunnel-test.example",
        Title = "Verdacht auf DNS-Tunneling",
        Evidence = "70 distinct names",
        Score = 70,
        Fingerprint = Guid.NewGuid().ToString(),
    };

    [Fact]
    public void Without_escalation_the_error_rule_stays_quiet()
    {
        var line = FindingNotifier.Format(Finding("high"), "AUSPEX-FUND");

        Assert.False(WhiskersErrorRule.IsMatch(line),
            "without escalation the general alarm channel must not fire");
    }

    [Fact]
    public void With_escalation_the_existing_error_rule_applies()
    {
        var line = FindingNotifier.Format(Finding("high"), "AUSPEX-FUND", "[ERROR]");

        Assert.True(WhiskersErrorRule.IsMatch(line),
            "escalated findings have to be caught by the existing rule");
        // The marker stays in so that a rule of your own can bite on top later.
        Assert.Contains("AUSPEX-FUND [high]", line);
        Assert.StartsWith("[ERROR] AUSPEX-FUND", line);
    }

    [Fact]
    public async Task Only_hard_findings_are_escalated()
    {
        using var fixture = new TestDb();
        fixture.Db.Findings.AddRange(Finding("high"), Finding("warn"));
        await fixture.Db.SaveChangesAsync();

        var logger = new RecordingLogger<FindingNotifier>();
        var notifier = new FindingNotifier(fixture.Db,
            Options.Create(new NotificationOptions { EscalateHigh = true, MinSeverity = "warn" }), logger);

        await notifier.FlushAsync();

        var eskaliert = logger.Lines.Where(l => WhiskersErrorRule.IsMatch(l)).ToList();
        Assert.Single(eskaliert);
        Assert.Contains("[high]", eskaliert[0]);
        // The general alarm channel loses its value if every anomaly ends up
        // in it.
        Assert.Equal(2, logger.Lines.Count);
    }
}

public class ClientNameTests
{
    [Fact]
    public void The_report_carries_the_device_name_as_its_own_field()
    {
        var f = new Auspex.Control.Data.Finding
        {
            Detector = "tunneling-verdacht",
            Severity = "high",
            Client = "192.168.1.43",
            ClientName = "Fernseher Wohnzimmer",
            Subject = "tunnel.example",
            Title = "Verdacht",
            Evidence = "70 names",
        };

        var line = FindingNotifier.Format(f, "AUSPEX-FUND");

        // Address and name kept apart: the line stays machine-parsable.
        Assert.Contains("client=192.168.1.43", line);
        Assert.Contains("name=\"Fernseher Wohnzimmer\"", line);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void Without_a_name_the_message_stays_unchanged()
    {
        var f = new Auspex.Control.Data.Finding
        {
            Detector = "nxdomain-flut",
            Severity = "warn",
            Client = "192.168.1.99",
            Title = "Titel",
            Evidence = "Belege",
        };

        Assert.DoesNotContain("name=", FindingNotifier.Format(f, "AUSPEX-FUND"));
    }

    [Fact]
    public void ClientLabel_falls_back_to_the_address()
    {
        var without = new Auspex.Control.Data.Finding { Client = "10.0.0.1" };
        var having = new Auspex.Control.Data.Finding { Client = "10.0.0.1", ClientName = "NAS" };

        Assert.Equal("10.0.0.1", without.ClientLabel);
        Assert.Equal("NAS (10.0.0.1)", having.ClientLabel);
    }
}
