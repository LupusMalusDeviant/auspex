using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Auspex.Control.Data;

namespace Auspex.Control.Tests;

/// <summary>
/// The migration that renamed the tables, run against a database that still
/// has the old ones.
///
/// <para>
/// Everything else in this project builds its schema with
/// <c>EnsureCreated</c>, which goes straight to the current model and never
/// touches a migration. So the riskiest file in the release — the one that
/// runs once, on a database holding months of history — had nothing checking
/// it. The draft EF produced for it wanted to drop three tables and create
/// them again; it was rewritten by hand with <c>RenameTable</c> and
/// <c>RenameColumn</c>, and this is what says the hand-written version does
/// what the name claims.
/// </para>
///
/// <para>
/// A file rather than <c>:memory:</c>: SQLite rewrites a table on some
/// <c>ALTER</c> statements, and that is a different code path on disk.
/// </para>
/// </summary>
public sealed class MigrationTests : IDisposable
{
    /// <summary>The migration immediately before the rename.</summary>
    private const string Before = "20260825152307_Verbindungen";

    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"auspex-migration-{Guid.NewGuid():N}.db");

    private AnalyticsDbContext Open()
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlite($"Data Source={_path}")
            .Options;
        return new AnalyticsDbContext(options);
    }

    [Fact]
    public async Task Renaming_the_tables_keeps_every_row()
    {
        // ── The state before ──────────────────────────────────────────────
        await using (var db = Open())
        {
            await db.Database.GetService<IMigrator>().MigrateAsync(Before);

            // Written as raw SQL under the old names on purpose: through the
            // model it would be impossible, and that is the point — this is
            // what a running installation looks like.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Ziele (Ip, Asn, Land, Stadt, Betreiber, StadtUnsicher,
                                   GeprueftUtc, StadtGeprueftUtc, Privat, ErstUtc, ZuletztUtc)
                VALUES ('140.82.121.4', 36459, 'US', 'Seattle', 'GITHUB', 0,
                        '2026-08-25 10:00:00', '2026-08-25 10:00:00', 0,
                        '2026-08-20 08:00:00', '2026-08-25 09:59:00');

                INSERT INTO Aufloesungen (Name, Ip, Domain, ErstUtc, ZuletztUtc, Anzahl)
                VALUES ('github.com', '140.82.121.4', 'github.com',
                        '2026-08-20 08:00:00', '2026-08-25 09:59:00', 42);

                INSERT INTO Verbindungen (Client, Geraet, Prozess, Ziel, Port, Protokoll,
                                          ErstUtc, ZuletztUtc, Anzahl, BytesRaus, BytesRein)
                VALUES ('192.168.1.43', 'Arbeitsrechner', 'chrome', '140.82.121.4',
                        443, 'tcp', '2026-08-25 09:00:00', '2026-08-25 09:59:00',
                        7, 1234, 5678);
                """);
        }

        // ── The migration ─────────────────────────────────────────────────
        await using (var db = Open())
        {
            await db.Database.MigrateAsync();
        }

        // ── And what is left ──────────────────────────────────────────────
        await using (var db = Open())
        {
            var destination = Assert.Single(db.Destinations);
            Assert.Equal("140.82.121.4", destination.Ip);
            Assert.Equal(36459, destination.Asn);
            Assert.Equal("US", destination.Country);
            Assert.Equal("Seattle", destination.City);
            Assert.Equal("GITHUB", destination.Operator);
            Assert.False(destination.IsPrivate);
            Assert.False(destination.CityUncertain);

            var resolution = Assert.Single(db.Resolutions);
            Assert.Equal("github.com", resolution.Name);
            Assert.Equal("140.82.121.4", resolution.Ip);
            Assert.Equal(42, resolution.Count);

            var connection = Assert.Single(db.Connections);
            Assert.Equal("Arbeitsrechner", connection.Device);
            Assert.Equal("chrome", connection.Process);
            Assert.Equal("140.82.121.4", connection.Destination);
            Assert.Equal(443, connection.Port);
            Assert.Equal("tcp", connection.Protocol);
            Assert.Equal(7, connection.Count);
            Assert.Equal(1234, connection.BytesOut);
            Assert.Equal(5678, connection.BytesIn);
        }
    }

    /// <summary>
    /// The indexes travel with the rename. They are what makes the analysis
    /// pages usable, and a missing one does not fail — it just gets slower
    /// month by month until somebody wonders why.
    /// </summary>
    [Fact]
    public async Task The_indexes_exist_afterwards_under_their_new_names()
    {
        await using (var db = Open())
        {
            await db.Database.MigrateAsync();
        }

        using var c = new SqliteConnection($"Data Source={_path}");
        c.Open();
        using var b = c.CreateCommand();
        b.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index'";
        var found = new List<string>();
        using (var reader = b.ExecuteReader())
        {
            while (reader.Read())
            {
                found.Add(reader.GetString(0));
            }
        }

        foreach (var index in new[]
        {
            "IX_Destinations_Ip",
            "IX_Destinations_Asn",
            "IX_Destinations_IsPrivate_CheckedUtc",
            "IX_Resolutions_Name_Ip",
            "IX_Resolutions_Domain_LastUtc",
            "IX_Connections_Destination",
            "IX_Connections_Device_LastUtc",
            "IX_Connections_Client_Process_Destination_Port_Protocol",
        })
        {
            Assert.Contains(index, found);
        }

        // And nothing left over under the old names — a stale index is dead
        // weight on every write.
        Assert.DoesNotContain(found, n => n.StartsWith("IX_Ziele", StringComparison.Ordinal));
        Assert.DoesNotContain(found, n => n.StartsWith("IX_Aufloesungen", StringComparison.Ordinal));
        Assert.DoesNotContain(found, n => n.StartsWith("IX_Verbindungen", StringComparison.Ordinal));
    }

    /// <summary>
    /// Down again. Not because anybody rolls back in practice, but because a
    /// <c>Down</c> that was never run is a claim nobody checked — and this one
    /// is the only way out if the deployment goes wrong at three in the
    /// morning.
    /// </summary>
    [Fact]
    public async Task The_way_back_works_too()
    {
        await using (var db = Open())
        {
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Destinations (Ip, Asn, Country, City, Operator, CityUncertain,
                                          CheckedUtc, CityCheckedUtc, IsPrivate, FirstUtc, LastUtc)
                VALUES ('1.1.1.1', 13335, 'US', NULL, 'CLOUDFLARENET', 0,
                        '2026-08-25 10:00:00', NULL, 0,
                        '2026-08-20 08:00:00', '2026-08-25 09:59:00');
                """);

            await db.Database.GetService<IMigrator>().MigrateAsync(Before);
        }

        using var c = new SqliteConnection($"Data Source={_path}");
        c.Open();
        using var b = c.CreateCommand();
        b.CommandText = "SELECT Betreiber FROM Ziele WHERE Ip = '1.1.1.1'";
        Assert.Equal("CLOUDFLARENET", b.ExecuteScalar() as string);
    }

    public void Dispose()
    {
        // Microsoft.Data.Sqlite keeps connections open to reuse them - right
        // in production, and here the reason Windows will not let the file go.
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }
}
