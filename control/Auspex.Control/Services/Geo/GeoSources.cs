using System.IO.Compression;

namespace Auspex.Control.Services.Geo;

/// <summary>
/// Settings for determining origin.
/// </summary>
public sealed class GeoOptions
{
    public const string SectionName = "Geo";

    /// <summary>
    /// Whether the origin data is fetched and refreshed.
    ///
    /// <para>
    /// <strong>The default is off, and deliberately so.</strong> On the first
    /// run this service downloads around 90 MB and writes a database of
    /// 717,000 address ranges from it; with <see cref="City"/> a 90 MB file
    /// and a pass over 7.9 million lines come on top. That is not something
    /// to send over a home line unasked, just because somebody started the
    /// container.
    /// </para>
    ///
    /// <para>
    /// Off only means: <em>do not fetch</em>. Whatever is already there
    /// stays in use — the lookup itself lives in <c>NetworkRanges</c> and
    /// <c>CityLookup</c> and does not hang off this switch. So whoever has
    /// the data and flips the switch loses nothing, they merely stop
    /// refreshing.
    /// </para>
    ///
    /// <para>
    /// It is switched on through <c>Geo__Enabled</c> in the environment — the
    /// shipped compose.yml carries it explicitly, so the decision is visible
    /// in a place that belongs to somebody.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether the city is determined as well. A switch of its own, because
    /// this source costs around 90 MB of download and a pass over 7.9
    /// million lines — for a value that is uncertain with large providers
    /// anyway.
    /// </summary>
    public bool City { get; set; } = true;

    /// <summary>Where the downloaded files live.</summary>
    public string Path { get; set; } = "var/geo";

    /// <summary>
    /// After how many days the sources are fetched again. Networks are
    /// reassigned continuously; a six-month-old mapping names operators that
    /// no longer exist in that form.
    /// </summary>
    public int RefreshDays { get; set; } = 30;

    /// <summary>
    /// Address ranges to operators. Free, no sign-up, refreshed daily.
    /// </summary>
    public string AsnUrl { get; set; } = "https://iptoasn.com/data/ip2asn-combined.tsv.gz";

    /// <summary>
    /// Address ranges to cities. <c>{monat}</c> is replaced by the current
    /// year and month. DB-IP Lite is under CC BY 4.0 — the attribution
    /// belongs visibly in the interface.
    /// </summary>
    public string CityUrl { get; set; } =
        "https://download.db-ip.com/free/dbip-city-lite-{monat}.csv.gz";
}

/// <summary>
/// Fetches and parses the lookup data.
///
/// <para>
/// What gets downloaded is a <em>file</em>, not an answer per address. That
/// is the decisive difference: a geo API would learn with every lookup where
/// this household browses — continuously, address by address, over months.
/// That is exactly what Auspex is built against. As it is, the provider only
/// learns that somebody fetched their file once a month.
/// </para>
/// </summary>
public sealed class GeoSources(HttpClient http, ILogger<GeoSources> log)
{
    /// <summary>
    /// Downloads a file when the one on disk is missing or too old. Returns
    /// the path, or <c>null</c> if there was nothing to fetch.
    /// </summary>
    public async Task<string?> FetchAsync(
        string url, string destination, TimeSpan maxAge, CancellationToken ct)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        if (File.Exists(destination) && DateTime.UtcNow - File.GetLastWriteTimeUtc(destination) < maxAge)
        {
            return destination;
        }

        try
        {
            log.LogInformation("Hole Nachschlagedaten von {Url}", url);

            // Next to the target file first, then rename: an abort partway
            // through should not leave half a file behind that counts as
            // valid on the next start.
            var provisional = destination + ".teil";
            await using (var source = await http.GetStreamAsync(url, ct))
            await using (var file = File.Create(provisional))
            {
                await source.CopyToAsync(file, ct);
            }

            File.Move(provisional, destination, overwrite: true);
            log.LogInformation("{File} fetched, {Bytes} bytes",
                Path.GetFileName(destination), new FileInfo(destination).Length);
            return destination;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No reason to disturb the service: without the file the columns
            // stay empty, and the next pass tries again. An older file that
            // is present stays valid.
            log.LogWarning(ex, "The lookup data at {Url} is unreachable", url);
            return File.Exists(destination) ? destination : null;
        }
    }

    /// <summary>
    /// Parses the operator file.
    ///
    /// <para>
    /// Five columns, tab-separated: start, end, AS number, country,
    /// description. Lines with AS 0 ("not routed") come along and are kept
    /// here — they close gaps, and the lookup treats the zero itself as "no
    /// information".
    /// </para>
    /// </summary>
    public static IEnumerable<(UInt128 From, UInt128 To, int Asn, string? Country, string? Operator)>
        AsnRows(string path)
    {
        using var file = File.OpenRead(path);
        using var stream = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            if (parts.Length < 5)
            {
                continue;
            }

            var from = AddressSpace.AsNumber(parts[0]);
            var until = AddressSpace.AsNumber(parts[1]);
            if (from is null || until is null || !int.TryParse(parts[2], out var asn))
            {
                continue;
            }

            // "None" is what the source uses for "no country known".
            var country = parts[3] is "None" or "" ? null : parts[3];
            var carrier = parts[4].Length == 0 ? null : Shorten(parts[4], 120);

            yield return (from.Value, until.Value, asn, country, carrier);
        }
    }

    private static string Shorten(string s, int length) =>
        s.Length <= length ? s : s[..length];

    /// <summary>The address of the city file for the current month.</summary>
    public static string CityUrlFor(string vorlage, DateTime now) =>
        vorlage.Replace("{monat}", now.ToString("yyyy-MM"));
}
