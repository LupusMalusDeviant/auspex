using Microsoft.EntityFrameworkCore;
using Auspex.Control.Data;

namespace Auspex.Control.Services;

public record ImpactClient(string Client, string? ClientName, long Matches, long WouldChange)
{
    public string Label => string.IsNullOrEmpty(ClientName) ? Client : ClientName;
}

public record ImpactName(string Name, long Matches, long WouldChange);

public record ImpactResult(
    ParsedRule Rule,
    long Matches,
    long AlreadyBlocked,
    long WouldChange,
    int Clients,
    DateTime? First,
    DateTime? Last,
    List<ImpactClient> TopClients,
    List<ImpactName> TopNames);

/// <summary>
/// Runs a rule against the stored history: what would it actually have done
/// over the past weeks? Arming a list change blind and then waiting to see
/// what breaks is the worse option.
/// </summary>
public sealed class ImpactService(AnalyticsDbContext db)
{
    public async Task<ImpactResult?> AnalyzeAsync(
        string rawRule, TimeSpan window, CancellationToken ct = default)
    {
        var rule = RuleParser.Parse(rawRule);
        if (rule is null) return null;

        var from = DateTime.UtcNow - window;
        var matching = Matching(rule, from);

        var matches = await matching.LongCountAsync(ct);
        if (matches == 0)
        {
            return new ImpactResult(rule, 0, 0, 0, 0, null, null, [], []);
        }

        var alreadyBlocked = await matching.LongCountAsync(q => q.Action == "blocked", ct);

        // What the rule really changes is only the part decided differently
        // today. A block rule on something already blocked changes nothing -
        // and that is exactly the figure you want to see before arming it.
        var wouldChange = rule.IsAllow ? alreadyBlocked : matches - alreadyBlocked;

        var clientRows = await matching
            .GroupBy(q => q.Client)
            .Select(g => new
            {
                Client = g.Key,
                Name = g.Max(x => x.ClientName),
                Matches = g.LongCount(),
                Blocked = g.LongCount(x => x.Action == "blocked"),
            })
            .OrderByDescending(x => x.Matches)
            .Take(10)
            .ToListAsync(ct);

        var nameRows = await matching
            .GroupBy(q => q.Name)
            .Select(g => new
            {
                Name = g.Key,
                Matches = g.LongCount(),
                Blocked = g.LongCount(x => x.Action == "blocked"),
            })
            .OrderByDescending(x => x.Matches)
            .Take(10)
            .ToListAsync(ct);

        return new ImpactResult(
            rule,
            matches,
            alreadyBlocked,
            wouldChange,
            await matching.Select(q => q.Client).Distinct().CountAsync(ct),
            await matching.MinAsync(q => (DateTime?)q.TimeUtc, ct),
            await matching.MaxAsync(q => (DateTime?)q.TimeUtc, ct),
            clientRows.Select(r => new ImpactClient(
                r.Client, r.Name, r.Matches,
                rule.IsAllow ? r.Blocked : r.Matches - r.Blocked)).ToList(),
            nameRows.Select(r => new ImpactName(
                r.Name, r.Matches,
                rule.IsAllow ? r.Blocked : r.Matches - r.Blocked)).ToList());
    }

    /// <summary>
    /// Translates the rule semantics into a query. EndsWith rather than a
    /// hand-built LIKE pattern: domains may contain underscores, and in LIKE
    /// that is a wildcard.
    /// </summary>
    private IQueryable<QueryRecord> Matching(ParsedRule rule, DateTime from)
    {
        var rows = db.Queries.Where(q => q.TimeUtc >= from);
        var dotted = "." + rule.Pattern;

        return rule.Kind switch
        {
            RuleKind.Exact => rows.Where(q => q.Name == rule.Pattern),
            RuleKind.SubOnly => rows.Where(q => q.Name.EndsWith(dotted)),
            _ => rows.Where(q => q.Name == rule.Pattern || q.Name.EndsWith(dotted)),
        };
    }
}
