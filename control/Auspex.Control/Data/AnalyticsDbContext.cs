using Microsoft.EntityFrameworkCore;

namespace Auspex.Control.Data;

public class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<QueryRecord> Queries => Set<QueryRecord>();
    public DbSet<IngestState> IngestStates => Set<IngestState>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<DailyTotal> DailyTotals => Set<DailyTotal>();
    public DbSet<DailyClient> DailyClients => Set<DailyClient>();
    public DbSet<DailyDomain> DailyDomains => Set<DailyDomain>();
    public DbSet<TemporaryAllow> TemporaryAllows => Set<TemporaryAllow>();
    public DbSet<RouterObservation> RouterObservations => Set<RouterObservation>();
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<Resolution> Resolutions => Set<Resolution>();
    public DbSet<Connection> Connections => Set<Connection>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var query = model.Entity<QueryRecord>();

        // Almost every analysis filters on time; the composite indexes cover the
        // three groupings the pages actually run (time series, per client, per
        // domain).
        query.HasIndex(q => q.TimeUtc);
        query.HasIndex(q => new { q.Client, q.TimeUtc });
        query.HasIndex(q => new { q.Domain, q.TimeUtc });
        query.HasIndex(q => new { q.Action, q.TimeUtc });

        // The same entry must not land twice, not even when the ingest fetches
        // a stretch again after a crash.
        query.HasIndex(q => new { q.Boot, q.Seq }).IsUnique();

        // A day is rolled up exactly once; the unique index turns that from an
        // intention into a guarantee.
        model.Entity<DailyTotal>().HasIndex(d => d.Day).IsUnique();
        model.Entity<DailyClient>().HasIndex(d => new { d.Day, d.Client }).IsUnique();
        model.Entity<DailyDomain>().HasIndex(d => new { d.Day, d.Domain }).IsUnique();

        var finding = model.Entity<Finding>();
        finding.HasIndex(f => f.Fingerprint).IsUnique();
        finding.HasIndex(f => new { f.Dismissed, f.DetectedUtc });
        finding.HasIndex(f => new { f.NotifiedUtc, f.DetectedUtc });

        // Kind and key form the identity; the same mapping must not sit there
        // twice, or the comparison reports it as new forever.
        model.Entity<RouterObservation>().HasIndex(o => new { o.Kind, o.Key }).IsUnique();

        // One row per address. The unique index is not just hygiene here: the
        // ingest sees the same address a hundred times a day and has to find
        // it again on exactly one row.
        var destination = model.Entity<Destination>();
        destination.HasIndex(z => z.Ip).IsUnique();
        // The enrichment looks for exactly this: what is still pending?
        destination.HasIndex(z => new { z.IsPrivate, z.CheckedUtc });
        destination.HasIndex(z => z.Asn);

        var resolution = model.Entity<Resolution>();
        resolution.HasIndex(a => new { a.Name, a.Ip }).IsUnique();
        resolution.HasIndex(a => a.Ip);
        resolution.HasIndex(a => new { a.Domain, a.LastUtc });
        // Fuer das Aufraeumen nach Aufbewahrungsfrist.
        resolution.HasIndex(a => a.LastUtc);

        // Program, destination, port and protocol form the relation. The client
        // belongs in it: the same program on two machines is two relations,
        // and without it one would carry the other's numbers forward.
        var connection = model.Entity<Connection>();
        connection.HasIndex(v => new { v.Client, v.Process, v.Destination, v.Port, v.Protocol })
            .IsUnique();
        connection.HasIndex(v => new { v.Device, v.LastUtc });
        connection.HasIndex(v => v.Destination);
        connection.HasIndex(v => v.LastUtc);
    }
}
