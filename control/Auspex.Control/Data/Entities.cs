using System.ComponentModel.DataAnnotations;

namespace Auspex.Control.Data;

/// <summary>
/// A permanently stored query. The data plane's ring buffer holds only the
/// last few minutes — the history lives here.
/// </summary>
public class QueryRecord
{
    public long Id { get; set; }

    /// <summary>Cursor number from the data plane.</summary>
    public long Seq { get; set; }

    /// <summary>Boot id of the resolver instance this entry came from.</summary>
    [MaxLength(32)]
    public string Boot { get; set; } = "";

    public DateTime TimeUtc { get; set; }

    [MaxLength(64)]
    public string Client { get; set; } = "";

    /// <summary>Device name, if the data plane knows one.</summary>
    [MaxLength(128)]
    public string? ClientName { get; set; }

    [MaxLength(64)]
    public string? Profile { get; set; }

    [MaxLength(253)]
    public string Name { get; set; } = "";

    /// <summary>Registrable domain (eTLD+1), computed by the data plane.</summary>
    [MaxLength(253)]
    public string Domain { get; set; } = "";

    [MaxLength(16)]
    public string Type { get; set; } = "";

    [MaxLength(16)]
    public string Action { get; set; } = "";

    [MaxLength(16)]
    public string Source { get; set; } = "";

    [MaxLength(512)]
    public string? Rule { get; set; }

    /// <summary>
    /// The CNAME target that caused the block. Without it a block on a
    /// harmless-looking first-party domain would be inexplicable.
    /// </summary>
    [MaxLength(253)]
    public string? Cname { get; set; }

    [MaxLength(128)]
    public string? List { get; set; }

    [MaxLength(64)]
    public string? Schedule { get; set; }

    [MaxLength(128)]
    public string? Upstream { get; set; }

    [MaxLength(16)]
    public string Rcode { get; set; } = "";

    /// <summary>
    /// AD bit of the upstream answer: the signature chain was checked.
    /// </summary>
    public bool Validated { get; set; }

    public double Millis { get; set; }

    [MaxLength(512)]
    public string? Error { get; set; }

    /// <summary>
    /// The longest label to the left of the registrable domain. Computed at
    /// ingest, because SQLite has no usable string splitting in SQL — and
    /// tunnelling detection looks at exactly this.
    /// </summary>
    public int LongestLabel { get; set; }
}

/// <summary>Where the ingest left off.</summary>
public class IngestState
{
    public int Id { get; set; }

    [MaxLength(32)]
    public string Boot { get; set; } = "";

    public long LastSeq { get; set; }
    public DateTime? LastRunUtc { get; set; }

    /// <summary>
    /// The total of all entries the ring buffer overwrote before they were
    /// collected. If this stays above zero, the poll interval is too long or
    /// the buffer too small.
    /// </summary>
    public long LostTotal { get; set; }

    public long Ingested { get; set; }
}

/// <summary>One finding from anomaly detection.</summary>
public class Finding
{
    public long Id { get; set; }

    public DateTime DetectedUtc { get; set; }
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }

    [MaxLength(48)]
    public string Detector { get; set; } = "";

    /// <summary>info | warn | high</summary>
    [MaxLength(8)]
    public string Severity { get; set; } = "info";

    [MaxLength(64)]
    public string Client { get; set; } = "";

    [MaxLength(128)]
    public string? ClientName { get; set; }

    /// <summary>
    /// The name where known, otherwise the address. "Suspected tunnelling at
    /// living room TV" can be acted on straight away, an IP cannot.
    /// </summary>
    public string ClientLabel => string.IsNullOrEmpty(ClientName) ? Client : $"{ClientName} ({Client})";

    [MaxLength(253)]
    public string? Subject { get; set; }

    /// <summary>
    /// The finished sentence, as older versions wrote it.
    ///
    /// <para>
    /// For new findings this stays empty: detection runs in the background
    /// and has no reader, and therefore no language — it only stores
    /// <see cref="Values"/>, and the sentence is produced at display time.
    /// The columns stay all the same, so findings from before that remain
    /// readable instead of sitting there blank.
    /// </para>
    /// </summary>
    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(1000)]
    public string Explanation { get; set; } = "";

    /// <summary>The numbers behind the finding, so it stays checkable.</summary>
    [MaxLength(1000)]
    public string Evidence { get; set; } = "";

    /// <summary>
    /// What the detector measured, as JSON — see
    /// <c>Services.Localization.FindingValues</c>. Null for findings from
    /// the time when finished sentences were still stored.
    /// </summary>
    [MaxLength(1000)]
    public string? Values { get; set; }

    /// <summary>
    /// The rule that would fix the finding — for false positives, the
    /// exception. Turns a report into an action.
    /// </summary>
    [MaxLength(300)]
    public string? Suggestion { get; set; }

    /// <summary>When the suggested rule was applied.</summary>
    public DateTime? AppliedUtc { get; set; }

    public double Score { get; set; }
    public bool Dismissed { get; set; }

    /// <summary>
    /// When the finding was reported outwards. Null means: still pending.
    /// Kept separate from detection, so a crash between the two does not
    /// make the finding vanish silently.
    /// </summary>
    public DateTime? NotifiedUtc { get; set; }

    /// <summary>
    /// A fingerprint from detector, client, subject and time window. Stops
    /// the same finding turning up again on every run.
    /// </summary>
    [MaxLength(200)]
    public string Fingerprint { get; set; } = "";
}

/// <summary>
/// Daily totals. Once a day is complete it is rolled up once — after that
/// the analysis survives the deletion of the raw data.
/// </summary>
public class DailyTotal
{
    public long Id { get; set; }
    public DateTime Day { get; set; }
    public long Total { get; set; }
    public long Blocked { get; set; }
    public long Validated { get; set; }
    public long Upstream { get; set; }
    public int Clients { get; set; }
    public int Domains { get; set; }
}

/// <summary>Daily totals per device.</summary>
public class DailyClient
{
    public long Id { get; set; }
    public DateTime Day { get; set; }

    [MaxLength(64)]
    public string Client { get; set; } = "";

    [MaxLength(128)]
    public string? ClientName { get; set; }

    public long Total { get; set; }
    public long Blocked { get; set; }
}

/// <summary>Tageswerte je registrierbarer Domain.</summary>
public class DailyDomain
{
    public long Id { get; set; }
    public DateTime Day { get; set; }

    [MaxLength(253)]
    public string Domain { get; set; } = "";

    public long Total { get; set; }
    public long Blocked { get; set; }
}

/// <summary>
/// An exception that expires by itself.
///
/// The normal case from the browser extension: a page will not load, you
/// allow it, and in a quarter of an hour the state is back to the one you
/// actually want. Permanent exceptions otherwise pile up, and after a year
/// nobody remembers why a line is in there — the block list quietly loses
/// its effect without anyone noticing.
///
/// The rule itself lives in the resolver's device profile; all that is here
/// is when it should disappear from there again.
/// </summary>
public class TemporaryAllow
{
    public long Id { get; set; }

    /// <summary>Name of the device profile the rule sits in.</summary>
    [MaxLength(64)]
    public string Device { get; set; } = "";

    /// <summary>The rule, word for word as it stands in the profile.</summary>
    [MaxLength(256)]
    public string Rule { get; set; } = "";

    /// <summary>The name it was about — for display.</summary>
    [MaxLength(256)]
    public string Domain { get; set; } = "";

    public DateTime CreatedUtc { get; set; }

    /// <summary>When the rule gets removed again.</summary>
    public DateTime UntilUtc { get; set; }

    /// <summary>Where it came from — "erweiterung" or "oberflaeche".</summary>
    [MaxLength(24)]
    public string Source { get; set; } = "";
}

/// <summary>
/// What the router last showed — a port mapping, a device.
///
/// Without this state no service can say whether a mapping is new: after
/// every restart everything would be new, and a report that claims the same
/// thing at every start soon goes unread. So the state lives in the database
/// and not in memory.
/// </summary>
public class RouterObservation
{
    public long Id { get; set; }

    /// <summary>port | geraet</summary>
    [MaxLength(16)]
    public string Kind { get; set; } = "";

    /// <summary>
    /// The identity it is recognised by: for a mapping, protocol and
    /// external port; for a device, the MAC.
    /// </summary>
    [MaxLength(160)]
    public string Key { get; set; } = "";

    /// <summary>
    /// The content in readable form. If it changes under the same key, the
    /// mapping was redirected — onto a different device, say.
    /// </summary>
    [MaxLength(400)]
    public string Detail { get; set; } = "";

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    /// <summary>When it was last missing. Null means: present right now.</summary>
    public DateTime? GoneUtc { get; set; }
}

/// <summary>
/// A destination on the network: an address the resolver handed out.
///
/// <para>
/// The resolver has known these addresses all along — it writes them into
/// <c>answers</c> — but the control plane discarded them at ingest. That left
/// the link between a name and what stands behind it missing: whoever wants
/// to know where a device sends things gets only the <em>name</em> from the
/// query log. Who owns it and where it sits is something only the address
/// says.
/// </para>
///
/// <para>
/// One row per address, not per query. Enriching with country, city and
/// operator costs a lookup; repeating it for each of the hundred thousand
/// daily queries would be waste, where a few thousand addresses carry the
/// same information.
/// </para>
/// </summary>
public class Destination
{
    public long Id { get; set; }

    /// <summary>Die Adresse in kanonischer Schreibweise.</summary>
    [MaxLength(45)]
    public string Ip { get; set; } = "";

    /// <summary>
    /// From the local network. Such addresses are not looked up — an answer
    /// about <c>192.168.1.1</c> would be empty at best and at worst a false
    /// statement about your own router.
    /// </summary>
    public bool IsPrivate { get; set; }

    /// <summary>When it was last looked up. Null means: still pending.</summary>
    public DateTime? CheckedUtc { get; set; }

    /// <summary>Two-letter country code, as the source supplies it.</summary>
    [MaxLength(2)]
    public string? Country { get; set; }

    [MaxLength(80)]
    public string? City { get; set; }

    /// <summary>Autonomous system — the number a network is registered under.</summary>
    public int? Asn { get; set; }

    /// <summary>Who owns the network, in plain words.</summary>
    [MaxLength(120)]
    public string? Operator { get; set; }

    /// <summary>
    /// The city is to be read with care.
    ///
    /// <para>
    /// Large providers spread the same address by anycast across nodes all
    /// over the world. What a city database says about it is the location of
    /// <em>one</em> node — often the nearest one, that is, your own city. A
    /// dot on a map would then claim the provider sits next door. The
    /// operator is still correct in these cases, which is why there is a
    /// marker here rather than a deleted value.
    /// </para>
    /// </summary>
    /// <remarks>
    /// What gets recorded is what held at lookup time. What gets
    /// <em>displayed</em> is what holds now: the interface derives the marker
    /// from the operator instead of reading this column. The reason is in the
    /// dossier — the list of distribution networks changes, and a derived
    /// value that gets stored goes stale with it.
    /// </remarks>
    public bool CityUncertain { get; set; }

    /// <summary>
    /// When the city was last searched for. Null means: still pending.
    ///
    /// <para>
    /// A field of its own, because <see cref="CityUncertain"/> previously
    /// meant both — "already searched" and "read with care". While both hung
    /// off one flag, an address where the search found nothing was marked
    /// uncertain and never searched again. After a fault in the search
    /// itself, exactly those addresses would have stayed empty forever.
    /// </para>
    /// </summary>
    public DateTime? CityCheckedUtc { get; set; }

    /// <summary>When this address first and last turned up.</summary>
    public DateTime FirstUtc { get; set; }
    public DateTime LastUtc { get; set; }
}

/// <summary>
/// Which name pointed at which address.
///
/// <para>
/// Deliberately without a device: <em>who</em> asked is in the query log, and
/// joining the two is a join over the name. The other way round every row
/// would have as many copies as there are devices, without a single extra
/// piece of information.
/// </para>
/// </summary>
public class Resolution
{
    public long Id { get; set; }

    [MaxLength(253)]
    public string Name { get; set; } = "";

    /// <summary>Registrable domain — so the display can group.</summary>
    [MaxLength(253)]
    public string Domain { get; set; } = "";

    [MaxLength(45)]
    public string Ip { get; set; } = "";

    public DateTime FirstUtc { get; set; }
    public DateTime LastUtc { get; set; }

    /// <summary>How often this mapping was seen.</summary>
    public long Count { get; set; }
}

/// <summary>
/// An observed connection: which program talked to which destination.
///
/// <para>
/// This is the information a DNS filter fundamentally cannot give. Auspex
/// sees "192.168.1.43 asked for graph.microsoft.com" — which of the seventy
/// running programs that was is written nowhere. These rows therefore do not
/// come from the resolver but from a small service on the machine itself,
/// polling the operating system's connection table.
/// </para>
///
/// <para>
/// One row per <em>relation</em>, not per connection: program, destination,
/// port and protocol form the key, everything else is carried forward. A
/// browser opens hundreds of connections to one destination in a day;
/// storing them individually would mean keeping a table that grows and
/// explains nothing the summary does not explain as well.
/// </para>
///
/// <para>
/// <strong>Which device is meant is not stated by the sensor but by its
/// sender address</strong> — the same rule as with the browser extension. So
/// nobody can record connections for someone else's device through this
/// route, not even by accident.
/// </para>
/// </summary>
public class Connection
{
    public long Id { get; set; }

    /// <summary>Sender address the report came from.</summary>
    [MaxLength(64)]
    public string Client { get; set; } = "";

    /// <summary>Device name, as the resolver knows it.</summary>
    [MaxLength(128)]
    public string? Device { get; set; }

    /// <summary>
    /// Name of the program, without path and without extension — "msedge",
    /// "Teams". The path is deliberately left out: it gives away user names
    /// and install locations without answering the question any better.
    /// </summary>
    [MaxLength(128)]
    public string Process { get; set; } = "";

    [MaxLength(45)]
    public string Destination { get; set; } = "";

    public int Port { get; set; }

    /// <summary>tcp or udp.</summary>
    [MaxLength(4)]
    public string Protocol { get; set; } = "tcp";

    public DateTime FirstUtc { get; set; }
    public DateTime LastUtc { get; set; }

    /// <summary>How often a connection there was newly opened.</summary>
    public long Count { get; set; }

    /// <summary>
    /// How much flowed, where the sensor was able to count it.
    ///
    /// <para>
    /// Null does not mean "nothing" but "not counted". Windows only supplies
    /// per-connection byte counters through TCP ESTATS, and that needs
    /// administrator rights. Without them the column stays empty — and an
    /// empty column is more honest than a zero that looks like a
    /// measurement.
    /// </para>
    /// </summary>
    public long? BytesOut { get; set; }
    public long? BytesIn { get; set; }
}
