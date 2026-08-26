using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Auspex.Control.Data;

namespace Auspex.Control.Tests;

/// <summary>
/// SQLite in memory rather than a fake: the detectors are almost nothing but
/// LINQ queries, and an in-memory provider without real SQL would check
/// precisely not what matters.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private long _seq;
    // Eigene Boot-Kennung je Instanz, wie zwei echte Resolver sie haetten.
    private readonly string _boot = Guid.NewGuid().ToString("N")[..16];

    public AnalyticsDbContext Db { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new AnalyticsDbContext(options);
        Db.Database.EnsureCreated();
    }

    /// <summary>Creates queries. The instant is relative to <c>now</c>.</summary>
    public void Seed(
        string client,
        string domain,
        DateTime timeUtc,
        int count = 1,
        string action = "allowed",
        string rcode = "NOERROR",
        string? namePrefix = null,
        int longestLabel = 0,
        string? list = null,
        string? clientName = null,
        bool validated = false,
        string? source = null,
        string? rule = null)
    {
        // Switched off for seeding only: with many rows SaveChanges gets
        // quadratically expensive otherwise. Outside this the context stays
        // normal, or you would be testing a configuration that does not
        // exist in reality.
        Db.ChangeTracker.AutoDetectChangesEnabled = false;
        for (var i = 0; i < count; i++)
        {
            var name = namePrefix is null ? domain : $"{namePrefix}{i}.{domain}";
            Db.Queries.Add(new QueryRecord
            {
                Seq = ++_seq,
                Boot = _boot,
                TimeUtc = timeUtc,
                Client = client,
                ClientName = clientName,
                Name = name,
                Domain = domain,
                Type = "A",
                Action = action,
                Source = source ?? (action == "blocked" ? "filter" : "upstream"),
                Validated = validated,
                Rcode = rcode,
                List = list,
                Rule = rule,
                Millis = 1,
                LongestLabel = longestLabel,
            });
        }
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
        Db.ChangeTracker.AutoDetectChangesEnabled = true;
    }

    /// <summary>One name-to-address mapping, as the resolver records it.</summary>
    public void SeedResolution(string name, string ip, long count = 1)
    {
        Db.Resolutions.Add(new Resolution
        {
            Name = name,
            Domain = name,
            Ip = ip,
            FirstUtc = DateTime.UtcNow.AddDays(-1),
            LastUtc = DateTime.UtcNow,
            Count = count,
        });
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    /// <summary>One connection, as the Windows sensor reports it.</summary>
    public void SeedConnection(
        string client, string process, string destination, DateTime lastUtc,
        long count = 1, int port = 443, string? device = null)
    {
        Db.Connections.Add(new Connection
        {
            Client = client,
            Device = device,
            Process = process,
            Destination = destination,
            Port = port,
            Protocol = "tcp",
            FirstUtc = lastUtc.AddMinutes(-1),
            LastUtc = lastUtc,
            Count = count,
        });
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
