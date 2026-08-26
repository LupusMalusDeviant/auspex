using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Auspex.Control.Data;
using Auspex.Control.Services;

namespace Auspex.Control.Tests;

public class BackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "auspex-b-" + Guid.NewGuid().ToString("N"));

    private (BackupService Backup, RestoreService Restore) Build(TestDb fixture)
    {
        // The resolver is unreachable: lists and learned state drop out with
        // it, the database and the rules still have to carry.
        var http = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:1"),
            Timeout = TimeSpan.FromMilliseconds(200),
        };
        var auspex = new AuspexClient(http, new ConfigurationBuilder().Build(), NullLogger<AuspexClient>.Instance);
        var rules = new RuleWriter(
            Options.Create(new RuleFileOptions
            {
                AllowPath = Path.Combine(_dir, "allowlist.txt"),
                BlockPath = Path.Combine(_dir, "blocklist.txt"),
            }),
            auspex, NullLogger<RuleWriter>.Instance);

        return (
            new BackupService(fixture.Db, auspex, rules, Options.Create(new AnalyticsOptions()),
                NullLogger<BackupService>.Instance),
            new RestoreService(fixture.Db, auspex, rules, NullLogger<RestoreService>.Instance));
    }

    [Fact]
    public async Task The_backup_contains_database_and_manifest()
    {
        using var fixture = new TestDb();
        fixture.Seed("10.0.0.1", "a.example", DateTime.UtcNow.AddHours(-1), count: 10);

        var (backup, _) = Build(fixture);
        using var destination = new MemoryStream();
        await backup.WriteAsync(destination);

        destination.Position = 0;
        using var zip = new ZipArchive(destination, ZipArchiveMode.Read);

        Assert.NotNull(zip.GetEntry("manifest.json"));
        Assert.NotNull(zip.GetEntry("analytics.db"));
        // Written out consistently, not copied raw: the file has to be
        // readable on its own.
        Assert.True(zip.GetEntry("analytics.db")!.Length > 0);
    }

    [Fact]
    public async Task Restoring_merges_rather_than_replaces()
    {
        using var source = new TestDb();
        source.Seed("10.0.0.1", "alt.example", DateTime.UtcNow.AddHours(-2), count: 5);

        var (backup, _) = Build(source);
        using var archive = new MemoryStream();
        await backup.WriteAsync(archive);

        // The target system has its own, newer data.
        using var destination = new TestDb();
        destination.Seed("10.0.0.2", "neu.example", DateTime.UtcNow.AddMinutes(-5), count: 3);

        var (_, restore) = Build(destination);
        archive.Position = 0;
        var result = await restore.RestoreAsync(archive);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(5, result.Queries);
        // Whatever arrived since the backup must not be lost.
        Assert.Equal(8, destination.Db.Queries.Count());
    }

    [Fact]
    public async Task Restoring_twice_duplicates_nothing()
    {
        using var source = new TestDb();
        source.Seed("10.0.0.1", "a.example", DateTime.UtcNow.AddHours(-2), count: 7);

        var (backup, _) = Build(source);
        using var archive = new MemoryStream();
        await backup.WriteAsync(archive);

        using var destination = new TestDb();
        var (_, restore) = Build(destination);

        archive.Position = 0;
        await restore.RestoreAsync(archive);
        archive.Position = 0;
        var zweitesMal = await restore.RestoreAsync(archive);

        Assert.True(zweitesMal.Ok);
        Assert.Equal(0, zweitesMal.Queries);
        Assert.Equal(7, destination.Db.Queries.Count());
    }

    [Fact]
    public async Task A_foreign_file_is_rejected()
    {
        using var fixture = new TestDb();
        var (_, restore) = Build(fixture);

        using var not_a_zip = new MemoryStream("this is not an archive"u8.ToArray());
        var result = await restore.RestoreAsync(not_a_zip);

        Assert.False(result.Ok);
        Assert.Contains("ZIP", result.Message);
    }

    [Fact]
    public async Task An_archive_without_a_manifest_is_rejected()
    {
        using var fixture = new TestDb();
        var (_, restore) = Build(fixture);

        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("irgendwas.txt");
        }
        archive.Position = 0;

        var result = await restore.RestoreAsync(archive);

        Assert.False(result.Ok);
        Assert.Contains("manifest.json", result.Message);
    }

    /// <summary>
    /// Paths from an uploaded file are not trustworthy - without checking,
    /// "../" could write outside.
    /// </summary>
    [Fact]
    public async Task A_path_outside_the_target_is_rejected()
    {
        using var fixture = new TestDb();
        var (_, restore) = Build(fixture);

        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("../../entwischt.txt");
        }
        archive.Position = 0;

        var result = await restore.RestoreAsync(archive);

        Assert.False(result.Ok);
        Assert.Contains("Verdächtiger Pfad", result.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
