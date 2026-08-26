using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Auspex.Control.Data;

namespace Auspex.Control.Services;

public record BackupManifest(
    DateTimeOffset Created,
    string SchemaVersion,
    long Queries,
    long Findings,
    int DailyTotals,
    string[] LearnProfiles);

public record RestoreResult(
    bool Ok,
    string Message,
    long Queries = 0,
    long Findings = 0,
    int DailyTotals = 0,
    int Rules = 0,
    int Lists = 0,
    int LearnEntries = 0);

/// <summary>
/// Backs up everything that hurts when the volumes are lost: the history,
/// the findings, our own rules, the managed lists and what has been learned.
///
/// What has been learned and the lists live in the resolver, not here — they
/// are fetched and restored through its API. That is why the backup is not a
/// plain copying of files.
/// </summary>
public sealed class BackupService(
    AnalyticsDbContext db,
    IAuspexClient auspex,
    IRuleWriter rules,
    IOptions<AnalyticsOptions> analytics,
    ILogger<BackupService> log)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task WriteAsync(Stream target, CancellationToken ct = default)
    {
        using var zip = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);

        // VACUUM INTO rather than a file copy: the database keeps running, and
        // the WAL part would not be included in a raw copy.
        var temp = Path.Combine(Path.GetTempPath(), $"auspex-backup-{Guid.NewGuid():N}.db");
        try
        {
            // Raw and interpolated on purpose: VACUUM INTO takes no
            // parameter, so there is no parameterised form of this statement.
            // The value is a temporary path this method builds itself from a
            // GUID, and the quote doubling is the belt to the braces.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{temp.Replace("'", "''")}'", ct);
#pragma warning restore EF1002
            zip.CreateEntryFromFile(temp, "analytics.db", CompressionLevel.SmallestSize);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        foreach (var kind in new[] { RuleTarget.Allow, RuleTarget.Block })
        {
            var lines = await rules.ReadAsync(kind, ct);
            if (lines.Count == 0) continue;
            await WriteTextAsync(zip, $"rules/{kind.ToString().ToLowerInvariant()}.txt",
                string.Join('\n', lines), ct);
        }

        var lists = await auspex.GetListsAsync(ct);
        if (lists?.Managed is { Length: > 0 })
        {
            await WriteTextAsync(zip, "resolver/lists.json", JsonSerializer.Serialize(lists.Managed, Json), ct);
        }

        var profile = new List<string>();
        foreach (var stats in await auspex.GetLearnAsync(ct))
        {
            var entries = await auspex.GetLearnEntriesAsync(stats.Profile, ct);
            if (entries.Count == 0) continue;
            await WriteTextAsync(zip, $"resolver/learn/{Sanitize(stats.Profile)}.json",
                JsonSerializer.Serialize(entries, Json), ct);
            profile.Add(stats.Profile);
        }

        var manifest = new BackupManifest(
            DateTimeOffset.UtcNow,
            await SchemaVersionAsync(ct),
            await db.Queries.LongCountAsync(ct),
            await db.Findings.LongCountAsync(ct),
            await db.DailyTotals.CountAsync(ct),
            [.. profile]);

        await WriteTextAsync(zip, "manifest.json", JsonSerializer.Serialize(manifest, Json), ct);
        log.LogInformation("Backup created: {Queries} queries, {Profiles} learning profiles",
            manifest.Queries, profile.Count);
    }

    private static async Task WriteTextAsync(ZipArchive zip, string name, string content, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.SmallestSize);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content.AsMemory(), ct);
    }

    private async Task<string> SchemaVersionAsync(CancellationToken ct)
    {
        var applied = await db.Database.GetAppliedMigrationsAsync(ct);
        return applied.LastOrDefault() ?? "none";
    }

    private static string Sanitize(string name)
        => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));

    public string DefaultFileName => $"auspex-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.zip";

    internal AnalyticsOptions Options => analytics.Value;
}
