using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Auspex.Control.Services;

namespace Auspex.Control.Tests;

public class RuleWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "auspex-" + Guid.NewGuid().ToString("N"));

    private RuleWriter Build(bool enabled = true)
    {
        // The resolver is deliberately unreachable here: that separates
        // "written" from "reloaded", which is what the result turns on.
        var http = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:1"),
            Timeout = TimeSpan.FromMilliseconds(200),
        };
        var auspex = new AuspexClient(http, new ConfigurationBuilder().Build(), NullLogger<AuspexClient>.Instance);

        return new RuleWriter(
            Options.Create(new RuleFileOptions
            {
                Enabled = enabled,
                AllowPath = Path.Combine(_dir, "allowlist.txt"),
                BlockPath = Path.Combine(_dir, "blocklist.txt"),
            }),
            auspex,
            NullLogger<RuleWriter>.Instance);
    }

    [Fact]
    public async Task Exception_and_block_land_in_separate_files()
    {
        var writer = Build();

        await writer.AddAsync("@@||shop.example^", "Fehlalarm", RuleTarget.Allow);
        await writer.AddAsync("||tracker.example^", "from the query log", RuleTarget.Block);

        var allow = await File.ReadAllTextAsync(writer.PathFor(RuleTarget.Allow));
        var block = await File.ReadAllTextAsync(writer.PathFor(RuleTarget.Block));

        Assert.Contains("@@||shop.example^", allow);
        Assert.DoesNotContain("tracker.example", allow);
        Assert.Contains("||tracker.example^", block);
        Assert.DoesNotContain("shop.example", block);
    }

    [Fact]
    public async Task The_rule_is_written_with_a_reason()
    {
        var writer = Build();

        var result = await writer.AddAsync("@@||api.hersteller.example^", "fehlalarm-verdacht: Fernseher");

        Assert.True(result.Written);
        // The resolver is not there - that must not devalue the writing.
        Assert.False(result.Reloaded);

        var content = await File.ReadAllTextAsync(writer.PathFor(RuleTarget.Allow));
        Assert.Contains("@@||api.hersteller.example^", content);
        Assert.Contains("fehlalarm-verdacht: Fernseher", content);
        Assert.StartsWith("#", content.Split('\n')[0]);
    }

    [Fact]
    public async Task The_same_rule_is_not_written_twice()
    {
        var writer = Build();

        await writer.AddAsync("@@||doppelt.example^", "erster Versuch");
        await writer.AddAsync("@@||doppelt.example^", "second attempt");

        var lines = await File.ReadAllLinesAsync(writer.PathFor(RuleTarget.Allow));
        Assert.Single(lines, l => l.Trim() == "@@||doppelt.example^");
    }

    [Fact]
    public async Task EnsureExists_creates_both_files_with_an_explanation()
    {
        var writer = Build();

        await writer.EnsureExistsAsync();

        foreach (var target in new[] { RuleTarget.Allow, RuleTarget.Block })
        {
            var path = writer.PathFor(target);
            Assert.True(File.Exists(path), $"{target} fehlt");
            // Whoever finds the file in a year's time should know where it came
        // from.
            Assert.Contains("Auspex.Control", await File.ReadAllTextAsync(path));
        }
    }

    [Fact]
    public async Task Switched_off_nothing_is_written()
    {
        var writer = Build(enabled: false);

        var result = await writer.AddAsync("@@||egal.example^", "Grund");

        Assert.False(result.Written);
        Assert.False(File.Exists(writer.PathFor(RuleTarget.Allow)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
