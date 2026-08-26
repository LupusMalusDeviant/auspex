namespace Auspex.Control.Services;

/// <summary>
/// A query as a person counts it: one name, one device, one second —
/// regardless of the resolver exchanging several records for it.
/// </summary>
/// <param name="Representative">
/// The first entry of the group. It carries everything that is the same
/// across the group anyway, and is the reference for the actions in the row.
/// </param>
/// <param name="Second">Local time as <c>HH:mm:ss</c>.</param>
/// <param name="Types">The record types involved, in query order.</param>
/// <param name="Count">How many entries were folded together.</param>
/// <param name="MaxMs">The slowest answer in the group.</param>
public sealed record QueryGroup(
    QueryLogEntry Vertreter,
    string Sekunde,
    IReadOnlyList<string> Types,
    int Count,
    double MaxMs);

/// <summary>
/// Folds together what was one event.
///
/// One visit to <c>example.com</c> produces three entries — A, AAAA and
/// HTTPS — and modern clients ask for all three at once. The log therefore
/// carried three near-identical rows on top of each other, eight for a
/// talkative device. The eye fell on repetition instead of on change.
/// </summary>
public static class QueryGrouping
{
    /// <summary>
    /// A device, not an address. The same device turns up with IPv4 and with
    /// rotating IPv6 privacy addresses; filtering by address showed a
    /// different slice of the same device depending on the time of day. The
    /// name is what stays stable.
    /// </summary>
    public static string Device(QueryLogEntry e) =>
        string.IsNullOrEmpty(e.ClientName) ? e.Client : e.ClientName;

    /// <summary>
    /// A, AAAA, HTTPS — in the order a client asks for them. Alphabetically
    /// AAAA would come before A, and the most common combination would look
    /// different every time from what you expect.
    /// </summary>
    public static int TypeRank(string type) => type switch
    {
        "A" => 0,
        "AAAA" => 1,
        "HTTPS" => 2,
        "PTR" => 3,
        _ => 4,
    };

    /// <summary>
    /// A tone for the list a rule came from — 1 to 6.
    ///
    /// Colour in this interface does not stand for judgement; state is
    /// carried by a stripe pattern. Here it separates a <em>category</em>:
    /// which list did the blocking? That is the question you actually have
    /// while skimming the query log, and it can be answered at a glance
    /// rather than by reading.
    ///
    /// Deliberately computed rather than maintained: a mapping table would
    /// have to be maintained by everybody who adds a list, and would be
    /// incomplete the day after. <c>string.GetHashCode</c> is out — in .NET
    /// the value is randomised per process, and a list would have a
    /// different colour after every restart.
    /// </summary>
    public static int ListTone(string? list)
    {
        if (string.IsNullOrWhiteSpace(list))
        {
            return 0;
        }

        // FNV-1a, not the obvious digit sum with factor 31: 31 mod 6 is 1, so
        // the whole calculation collapses into the plain sum of the
        // characters, and similar names land in the same bucket. With six
        // common list names that produced only three tones - found by the
        // test, not by the eye.
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var h = offset;
            foreach (var c in list.Trim().ToLowerInvariant())
            {
                h = (h ^ c) * prime;
            }

            // The upper bits are better mixed than the lower ones.
            return (int)((h >> 13) % 6) + 1;
        }
    }

    /// <summary>
    /// Groups by device, name and second — <b>and additionally by decision
    /// and rule</b>.
    ///
    /// The last two are why this is checked here: only what was
    /// <em>decided the same way</em> may be folded together. If an A query
    /// got through and the HTTPS query did not, a shared row would hide
    /// exactly the difference that is interesting — and nobody would ever
    /// learn there was one.
    ///
    /// The input order is preserved: the log comes newest first, and that is
    /// how it should stay.
    /// </summary>
    public static IReadOnlyList<QueryGroup> Group(IEnumerable<QueryLogEntry> entries) =>
    [
        .. entries
            .GroupBy(e => (
                // By DEVICE, not by address. A device with IPv4 and a rotating
                // IPv6 privacy address would otherwise produce two rows for
                // the same visit - contradicting the very principle the whole
                // identity handling rests on.
                //
                // The address is not lost by that: action and rule are still
                // in the key, so only what was decided the same way is folded
                // together. If a query over IPv6 got through and the one over
                // IPv4 did not, they would stay two rows - exactly the
                // difference you want to see.
                Device: Device(e),
                e.Name,
                Sekunde: Localization.DisplayTime.ToDisplay(e.Time).ToString("HH:mm:ss"),
                e.Action,
                e.Rule))
            .Select(g => new QueryGroup(
                g.First(),
                g.Key.Sekunde,
                [.. g.Select(x => x.Type).Distinct().OrderBy(TypeRank).ThenBy(x => x, StringComparer.Ordinal)],
                g.Count(),
                g.Max(x => x.Millis)))
    ];
}
