using System.Text.Json;
using System.Text.Json.Serialization;

namespace Auspex.Control.Services.Localization;

/// <summary>
/// The numbers behind a finding.
///
/// <para>
/// The detectors used to write finished sentences into the database —
/// "<em>living room TV</em> asks <em>telemetry.example</em> 12x more often
/// than usual". That was convenient and it was wrong, and the translation
/// merely made it visible: <strong>detection runs in the background, every
/// five minutes, without anybody having asked for it.</strong> There is no
/// reader at that moment and therefore no language. What the detector wrote
/// in there was the server's language — and that has nothing to do with the
/// language of whoever is looking.
/// </para>
///
/// <para>
/// A finding therefore carries only what it measured. The sentence is
/// produced at display time, in the language of whoever is looking. As a
/// side effect the finding becomes checkable: the numbers stand there
/// individually rather than baked into a text.
/// </para>
///
/// <para>
/// Every field is optional — each detector fills its own. One record with
/// many holes is the more honest thing here than seven records, six of which
/// would have to be carried along at every call site.
/// </para>
/// </summary>
public sealed record FindingValues
{
    /// <summary>Wie oft im Fenster.</summary>
    [JsonPropertyName("anzahl")] public long? Count { get; init; }

    /// <summary>Every query in the window, where the share matters.</summary>
    [JsonPropertyName("gesamt")] public long? Total { get; init; }

    /// <summary>Queries that could not be resolved.</summary>
    [JsonPropertyName("nx")] public long? Nx { get; init; }

    /// <summary>Anteil, 0 bis 1.</summary>
    [JsonPropertyName("anteil")] public double? Anteil { get; init; }

    /// <summary>Verschiedene Domains.</summary>
    [JsonPropertyName("domains")] public int? Domains { get; init; }

    /// <summary>Verschiedene Namen.</summary>
    [JsonPropertyName("namen")] public int? Names { get; init; }

    /// <summary>An example name, where one helps the search.</summary>
    [JsonPropertyName("beispiel")] public string? Example { get; init; }

    /// <summary>Longest label in characters — the tunnelling marker.</summary>
    [JsonPropertyName("maxLabel")] public int? MaxLabel { get; init; }

    /// <summary>How much more often than in the baseline.</summary>
    [JsonPropertyName("faktor")] public double? Faktor { get; init; }

    /// <summary>Grundlinie, Anfragen je Stunde.</summary>
    [JsonPropertyName("proStunde")] public double? PerHour { get; init; }

    /// <summary>Beobachtet, Anfragen je Minute.</summary>
    [JsonPropertyName("proMinute")] public double? ProMinute { get; init; }

    /// <summary>Extrapolated to a whole day.</summary>
    [JsonPropertyName("amTag")] public long? PerDay { get; init; }

    /// <summary>How many days the baseline covers.</summary>
    [JsonPropertyName("basisTage")] public double? BaselineDays { get; init; }

    /// <summary>The span the finding stretches over, in seconds.</summary>
    [JsonPropertyName("spanneSek")] public double? SpanneSek { get; init; }

    /// <summary>How many devices are involved.</summary>
    [JsonPropertyName("geraete")] public int? Devices { get; init; }

    /// <summary>First and last contact, for the synchrony.</summary>
    [JsonPropertyName("erste")] public DateTime? First { get; init; }
    [JsonPropertyName("letzte")] public DateTime? Last { get; init; }

    /// <summary>Which rule blocked, and from which list.</summary>
    [JsonPropertyName("regel")] public string? Rule { get; init; }
    [JsonPropertyName("liste")] public string? ListName { get; init; }

    /// <summary>Was it night? The same figure means something else at 3am.</summary>
    [JsonPropertyName("nachts")] public bool Nachts { get; init; }

    // ── What the router reports ───────────────────────────────────────────

    /// <summary>"neu", "geaendert" or "weg" — for router observations.</summary>
    [JsonPropertyName("art")] public string? ChangeKind { get; init; }

    /// <summary>Protocol and external port of a mapping.</summary>
    [JsonPropertyName("protokoll")] public string? Protocol { get; init; }
    [JsonPropertyName("port")] public string? Port { get; init; }

    /// <summary>
    /// Whether the mapping applies to any remote end — the difference
    /// between "one machine may come in" and "the internet may come in".
    /// </summary>
    [JsonPropertyName("fuerAlle")] public bool ForAll { get; init; }

    /// <summary>State before and now, on a change.</summary>
    [JsonPropertyName("vorher")] public string? Before { get; init; }
    [JsonPropertyName("jetzt")] public string? Now { get; init; }

    /// <summary>The link a device came in over.</summary>
    [JsonPropertyName("anschluss")] public string? Connection { get; init; }

    /// <summary>Address of a newly seen device.</summary>
    [JsonPropertyName("adresse")] public string? Address { get; init; }

    /// <summary>Whether the device randomises its MAC per network.</summary>
    [JsonPropertyName("zufallMac")] public bool ZufallMac { get; init; }

    /// <summary>Whether it is connected right now.</summary>
    [JsonPropertyName("online")] public bool Online { get; init; }

    /// <summary>Die Spanne als <see cref="TimeSpan"/>.</summary>
    [JsonIgnore]
    public TimeSpan Span => TimeSpan.FromSeconds(SpanneSek ?? 0);

    private static readonly JsonSerializerOptions Sparsam = new()
    {
        // What a detector did not measure should not be in the database
        // either. Otherwise seven eighths of every row read "null".
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string AsJson() => JsonSerializer.Serialize(this, Sparsam);

    /// <summary>
    /// Reads the values back. Whatever cannot be read produces <c>null</c>
    /// rather than an exception: a finding from an older version should not
    /// bring the page down but fall back to its stored text.
    /// </summary>
    public static FindingValues? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FindingValues>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
