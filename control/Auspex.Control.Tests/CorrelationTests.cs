using Auspex.Control.Services;
using Auspex.Control.Services.Localization;

namespace Auspex.Control.Tests;

public class CorrelationTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 14, 0, 0, DateTimeKind.Utc);
    private static DetectionContext Context() => new(Now.AddHours(-1), Now, Now.AddDays(-14), true);

    [Fact]
    public async Task Three_devices_with_the_same_new_domain_are_reported()
    {
        using var fixture = new TestDb();
        // History, so that the devices are not new in themselves.
        foreach (var client in new[] { "10.0.0.1", "10.0.0.2", "10.0.0.3" })
        {
            fixture.Seed(client, "alltag.example", Now.AddDays(-3), count: 20);
        }
        // Dieselbe neue Domain, kurz hintereinander.
        fixture.Seed("10.0.0.1", "neu.example", Now.AddMinutes(-20), clientName: "Handy");
        fixture.Seed("10.0.0.2", "neu.example", Now.AddMinutes(-19), clientName: "Tablet");
        fixture.Seed("10.0.0.3", "neu.example", Now.AddMinutes(-18), clientName: "Laptop");

        var findings = await Detectors.CorrelationAsync(fixture.Db, Context(), default);

        var finding = Assert.Single(findings);
        Assert.Equal("gleichlauf", finding.Detector);
        Assert.Equal("neu.example", finding.Subject);
        // The sentence only comes into being at display time - and has to
        // carry the number in both languages, otherwise the translation has
        // lost it.
        Assert.Contains("3 Geräte", new StringsDe().Finding(finding).Titel);
        Assert.Contains("3 devices", new StringsEn().Finding(finding).Titel);
        // The devices belong in the report, abbreviated but counted in full.
        Assert.Contains("+1", finding.Client);
    }

    [Fact]
    public async Task Two_devices_are_not_enough()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.0.1", "neu.example", Now.AddMinutes(-20));
        fixture.Seed("10.0.0.2", "neu.example", Now.AddMinutes(-19));

        Assert.Empty(await Detectors.CorrelationAsync(fixture.Db, Context(), default));
    }

    /// <summary>
    /// If even one device already knew the domain it is not synchrony but
    /// everyday traffic - otherwise the detector reports every popular
    /// service.
    /// </summary>
    [Fact]
    public async Task A_known_domain_is_not_synchrony()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.0.9", "bekannt.example", Now.AddDays(-2), count: 5);
        foreach (var client in new[] { "10.0.0.1", "10.0.0.2", "10.0.0.3" })
        {
            fixture.Seed(client, "bekannt.example", Now.AddMinutes(-20));
        }

        Assert.Empty(await Detectors.CorrelationAsync(fixture.Db, Context(), default));
    }

    [Fact]
    public async Task Spread_over_the_hour_is_not_synchrony()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.0.1", "verteilt.example", Now.AddMinutes(-55));
        fixture.Seed("10.0.0.2", "verteilt.example", Now.AddMinutes(-30));
        fixture.Seed("10.0.0.3", "verteilt.example", Now.AddMinutes(-5));

        Assert.Empty(await Detectors.CorrelationAsync(fixture.Db, Context(), default));
    }

    [Fact]
    public async Task Many_devices_or_very_dense_weighs_more()
    {
        using var fixture = new TestDb();
        for (var i = 1; i <= 5; i++)
        {
            fixture.Seed($"10.0.0.{i}", "welle.example", Now.AddMinutes(-20).AddSeconds(i * 10));
        }

        var finding = Assert.Single(await Detectors.CorrelationAsync(fixture.Db, Context(), default));
        Assert.Equal("warn", finding.Severity);
        Assert.Equal(5, finding.Score);
    }

    [Fact]
    public async Task Without_a_baseline_the_detector_stays_silent()
    {
        using var fixture = new TestDb();
        foreach (var client in new[] { "10.0.0.1", "10.0.0.2", "10.0.0.3" })
        {
            fixture.Seed(client, "neu.example", Now.AddMinutes(-20));
        }

        var ctx = new DetectionContext(Now.AddHours(-1), Now, Now.AddDays(-14), HasBaseline: false);
        Assert.Empty(await Detectors.CorrelationAsync(fixture.Db, ctx, default));
    }
}
