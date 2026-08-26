using System.Net;

namespace Auspex.Sensor;

/// <summary>One relation, in the shape it gets reported.</summary>
public sealed record Relation
{
    public required string Process { get; init; }
    public required string Destination { get; init; }
    public required int Port { get; init; }
    public string Protocol { get; init; } = "tcp";
    public long Count { get; set; }
    public DateTimeOffset First { get; set; }
    public DateTimeOffset Last { get; set; }
    public long? BytesOut { get; set; }
    public long? BytesIn { get; set; }
}

/// <summary>
/// Keeps the books on what changed between two polls.
///
/// <para>
/// The connection table is a snapshot: it says what is <em>open right
/// now</em>, not what was opened. A connection lasting through ten polls
/// would appear ten times — counting that as ten connections would simply be
/// wrong.
/// </para>
///
/// <para>
/// So what gets counted is the increment: what is there now and was not
/// before. That is the number of connection setups the sensor has seen — and
/// the qualifier "seen" belongs in there, because something can open and
/// close between two polls.
/// </para>
/// </summary>
public sealed class Ledger(TimeProvider clock)
{
    private HashSet<(int, int, string, int)> _before = [];
    private readonly Dictionary<(string, string, int), Relation> _open = [];

    /// <summary>
    /// Takes a snapshot and carries the relations forward.
    /// </summary>
    /// <param name="connections">What the operating system holds right now.</param>
    /// <param name="names">Process id to program name.</param>
    /// <param name="bytes">
    /// Byte counters per connection, where they were available. Empty means
    /// "not counted", not "nothing flowed".
    /// </param>
    public void Record(
        IReadOnlyList<OpenConnection> connections,
        IReadOnlyDictionary<int, string> names,
        IReadOnlyDictionary<(int, int, string, int), (long Out, long In)>? bytes = null)
    {
        var now = clock.GetUtcNow();
        var current = new HashSet<(int, int, string, int)>();

        foreach (var v in connections)
        {
            var key = v.Key;
            current.Add(key);

            if (!names.TryGetValue(v.Pid, out var process) || process.Length == 0)
            {
                // Without a name the row is worthless: "process 4711 is talking
                // to Google" does not answer the question this page asks.
                continue;
            }

            var relation = (process, v.Remote.ToString(), v.Port);
            if (!_open.TryGetValue(relation, out var b))
            {
                b = new Relation
                {
                    Process = process,
                    Destination = v.Remote.ToString(),
                    Port = v.Port,
                    First = now,
                    Last = now,
                };
                _open[relation] = b;
            }

            b.Last = now;

            // Only count what is new. An existing connection turns up in
            // every poll and would otherwise be a fresh setup each time.
            if (!_before.Contains(key))
            {
                b.Count++;
            }

            if (bytes is not null && bytes.TryGetValue(key, out var z))
            {
                b.BytesOut = (b.BytesOut ?? 0) + z.Out;
                b.BytesIn = (b.BytesIn ?? 0) + z.In;
            }
        }

        _before = current;
    }

    /// <summary>
    /// Hands out what has accumulated since last time, and starts over.
    ///
    /// <para>
    /// Relations without a single counted setup stay out: they exist, but
    /// nothing happened that would be worth reporting. Otherwise a standing
    /// connection would add a row to every batch that says nothing new.
    /// </para>
    /// </summary>
    public List<Relation> Collect()
    {
        var fertig = _open.Values.Where(b => b.Count > 0 || b.BytesOut > 0).ToList();
        _open.Clear();
        return fertig;
    }
}
