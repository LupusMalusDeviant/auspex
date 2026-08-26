using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Auspex.Control.Data;

namespace Auspex.Control.Services;
using Auspex.Control.Services.Localization;

/// <summary>
/// Restores a backup — merging, not replacing. Whoever restores after a loss
/// usually has a few hours of new data again; deleting that would be a
/// second loss.
/// </summary>
public sealed class RestoreService(
    AnalyticsDbContext db,
    IAuspexClient auspex,
    IRuleWriter rules,
    ILogger<RestoreService> log)
{
    public async Task<RestoreResult> RestoreAsync(Stream source, CancellationToken ct = default)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"auspex-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            if (Entpacken(source, temp) is { } error)
            {
                return new RestoreResult(false, error);
            }

            var manifestPath = Path.Combine(temp, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return new RestoreResult(false, Strings.Current.NotAnAuspexBackup);
            }

            var manifest = JsonSerializer.Deserialize<BackupManifest>(
                await File.ReadAllTextAsync(manifestPath, ct),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null)
            {
                return new RestoreResult(false, "manifest.json cannot be read.");
            }

            var own = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault() ?? "none";
            if (manifest.SchemaVersion != own)
            {
                // Merging assumes identical columns. With a differing version,
                // better to refuse than to bend the data.
                return new RestoreResult(false,
                    Strings.Current.SchemaMismatch(manifest.SchemaVersion.ToString(), own.ToString()));
            }

            var result = await MergeDatabaseAsync(Path.Combine(temp, "analytics.db"), ct);
            result = result with
            {
                Ok = true,
                Message = Strings.Current.BackupMerged,
                Rules = await RestoreRulesAsync(temp, ct),
                Lists = await RestoreListsAsync(temp, ct),
                LearnEntries = await RestoreLearnAsync(temp, ct),
            };

            log.LogInformation("Backup restored: {Result}", result);
            return result;
        }
        catch (InvalidDataException)
        {
            return new RestoreResult(false, Strings.Current.NotAReadableZip);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>Unpacks and returns an error message if something is off.</summary>
    private static string? Entpacken(Stream source, string temp)
    {
        // leaveOpen: the stream belongs to the caller, not to us.
        using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var root = Path.GetFullPath(temp);

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;

            // Paths from an uploaded file are not to be trusted: without this
            // check, "../" could write outside the target directory.
            var destination = Path.GetFullPath(Path.Combine(temp, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.Ordinal))
            {
                return Strings.Current.SuspiciousPath(entry.FullName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
        return null;
    }

    /// <summary>
    /// Merges the tables over ATTACH. INSERT OR IGNORE leans on the unique
    /// indexes: duplicate queries, findings and daily totals therefore drop
    /// out by themselves.
    /// </summary>
    private async Task<RestoreResult> MergeDatabaseAsync(string backupPath, CancellationToken ct)
    {
        if (!File.Exists(backupPath))
        {
            return new RestoreResult(true, Strings.Current.NoDatabaseInside);
        }

        var beforeQueries = await db.Queries.LongCountAsync(ct);
        var beforeFindings = await db.Findings.LongCountAsync(ct);
        var beforeDays = await db.DailyTotals.CountAsync(ct);

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using (var attach = connection.CreateCommand())
        {
            attach.CommandText = "ATTACH DATABASE $pfad AS sicherung";
            var p = attach.CreateParameter();
            p.ParameterName = "$pfad";
            p.Value = backupPath;
            attach.Parameters.Add(p);
            await attach.ExecuteNonQueryAsync(ct);
        }

        try
        {
            foreach (var table in new[]
                     {
                         "Queries", "Findings", "DailyTotals", "DailyClients", "DailyDomains",
                     })
            {
                // Without the id column: the target assigns that itself. Copied
                // along, the primary keys of both sides would collide and
                // INSERT OR IGNORE would silently drop the rows — the
                // difference between "already present" and "discarded" would
                // be invisible from outside.
                var columns = await ColumnsWithoutIdAsync(connection, table, ct);
                if (columns.Count == 0) continue;

                var list = string.Join(", ", columns.Select(c => $"\"{c}\""));
                await using var cmd = connection.CreateCommand();
                cmd.CommandText =
                    $"INSERT OR IGNORE INTO {table} ({list}) SELECT {list} FROM sicherung.{table}";
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            await using var detach = connection.CreateCommand();
            detach.CommandText = "DETACH DATABASE sicherung";
            await detach.ExecuteNonQueryAsync(ct);
        }

        return new RestoreResult(true, "",
            await db.Queries.LongCountAsync(ct) - beforeQueries,
            await db.Findings.LongCountAsync(ct) - beforeFindings,
            await db.DailyTotals.CountAsync(ct) - beforeDays);
    }

    /// <summary>
    /// A table's column names, without the primary key. Read from the schema
    /// rather than hard-wired — otherwise this list would have to be
    /// maintained with every migration, and nobody would remember.
    /// </summary>
    private static async Task<List<string>> ColumnsWithoutIdAsync(
        System.Data.Common.DbConnection connection, string table, CancellationToken ct)
    {
        var columns = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(reader.GetOrdinal("name"));
            var isKey = reader.GetInt32(reader.GetOrdinal("pk")) > 0;
            if (!isKey)
            {
                columns.Add(name);
            }
        }
        return columns;
    }

    private async Task<int> RestoreRulesAsync(string temp, CancellationToken ct)
    {
        var übernommen = 0;
        foreach (var target in new[] { RuleTarget.Allow, RuleTarget.Block })
        {
            var path = Path.Combine(temp, "rules", $"{target.ToString().ToLowerInvariant()}.txt");
            if (!File.Exists(path)) continue;

            foreach (var line in await File.ReadAllLinesAsync(path, ct))
            {
                var rule = line.Trim();
                if (rule.Length == 0 || rule.StartsWith('#')) continue;
                if ((await rules.AddAsync(rule, "from a backup", target, ct)).Written)
                {
                    übernommen++;
                }
            }
        }
        return übernommen;
    }

    private async Task<int> RestoreListsAsync(string temp, CancellationToken ct)
    {
        var path = Path.Combine(temp, "resolver", "lists.json");
        if (!File.Exists(path)) return 0;

        var lists = JsonSerializer.Deserialize<ManagedList[]>(
            await File.ReadAllTextAsync(path, ct),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var übernommen = 0;
        foreach (var list in lists ?? [])
        {
            if (await auspex.AddListAsync(list, ct)) übernommen++;
        }
        return übernommen;
    }

    private async Task<int> RestoreLearnAsync(string temp, CancellationToken ct)
    {
        var directory = Path.Combine(temp, "resolver", "learn");
        if (!Directory.Exists(directory)) return 0;

        var übernommen = 0;
        foreach (var stats in await auspex.GetLearnAsync(ct))
        {
            var path = Path.Combine(directory, $"{Sanitize(stats.Profile)}.json");
            if (!File.Exists(path)) continue;

            übernommen += await auspex.ImportLearnAsync(
                stats.Profile, await File.ReadAllTextAsync(path, ct), ct);
        }
        return übernommen;
    }

    private static string Sanitize(string name)
        => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
}
