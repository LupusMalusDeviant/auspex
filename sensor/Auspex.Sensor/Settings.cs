using System.Text.Json;
using System.Text.Json.Serialization;

namespace Auspex.Sensor;

/// <summary>
/// What the sensor needs to know: where to report, and what to identify
/// itself with.
///
/// <para>
/// Read from <c>sensor.json</c> next to the executable, overridden by
/// environment variables. Both, because both routes are needed: a file for
/// the machine you are sitting at, environment variables for anything that
/// gets rolled out.
/// </para>
///
/// <para>
/// <strong>The defaults sit as <c>??</c> in the accessors, not as
/// initialisers on the fields.</strong> That looks more roundabout and is
/// the reason the sensor does what is written here at all: an initialiser
/// like <c>= true</c> does not survive deserialisation when the key is
/// missing from the file — what comes out then is <c>false</c> or <c>0</c>,
/// with nothing anywhere saying so.
/// </para>
///
/// <para>
/// Measured: with <c>"bytes": true</c> in the file the sensor counted;
/// without that line it considered itself switched off. With the intervals
/// it would not even have shown — 0 seconds is clamped to 1 and 5 below, and
/// the sensor would have reported fifteen times as often as documented.
/// </para>
/// </summary>
public sealed record Settings
{
    /// <summary>Address of the dashboard, without a trailing slash.</summary>
    [JsonPropertyName("base")]
    public string BaseUrl { get; init; } = "";

    /// <summary>The name this key had up to version 0.9.</summary>
    [JsonPropertyName("basis")]
    public string? BaseUrlBis09 { get; init; }

    /// <summary>
    /// The same token as the browser extension.
    ///
    /// <para>
    /// Deliberately the same and not a second one: it applies to <em>this one
    /// dashboard</em> and permits exactly the same thing in both cases — a
    /// device talking about itself. A second key would be a second place for
    /// one to get lost, without the rights differing at all.
    /// </para>
    /// </summary>
    [JsonPropertyName("token")]
    public string Token { get; init; } = "";

    /// <inheritdoc cref="BaseUrlBis09"/>
    [JsonPropertyName("zeichen")]
    public string? TokenBis09 { get; init; }

    [JsonPropertyName("pollSeconds")]
    public int? PollSecondsRaw { get; init; }

    /// <inheritdoc cref="BaseUrlBis09"/>
    [JsonPropertyName("abfrageSekunden")]
    public int? PollSecondsBis09 { get; init; }

    [JsonPropertyName("reportSeconds")]
    public int? ReportSecondsRaw { get; init; }

    /// <inheritdoc cref="BaseUrlBis09"/>
    [JsonPropertyName("meldungSekunden")]
    public int? ReportSecondsBis09 { get; init; }

    [JsonPropertyName("bytes")]
    public bool? BytesRaw { get; init; }

    [JsonPropertyName("verbose")]
    public bool? VerboseRaw { get; init; }

    /// <inheritdoc cref="BaseUrlBis09"/>
    [JsonPropertyName("laut")]
    public bool? VerboseBis09 { get; init; }

    /// <summary>How often the connection table is read.</summary>
    [JsonIgnore]
    public int PollSeconds => PollSecondsRaw ?? PollSecondsBis09 ?? 2;

    /// <summary>How often a report goes out.</summary>
    [JsonIgnore]
    public int ReportSeconds => ReportSecondsRaw ?? ReportSecondsBis09 ?? 30;

    /// <summary>
    /// Whether counting bytes per connection is attempted.
    ///
    /// <para>
    /// Needs administrator rights: Windows only hands out per-connection byte
    /// counters through TCP ESTATS, and those have to be switched on per
    /// connection. Without the rights the column stays empty — which is more
    /// honest than a zero.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public bool Bytes => BytesRaw ?? true;

    /// <summary>Whether every poll is written to the console.</summary>
    [JsonIgnore]
    public bool Verbose => VerboseRaw ?? VerboseBis09 ?? false;

    [JsonIgnore]
    public bool Complete => BaseUrl.Length > 0 && Token.Length > 0;

    /// <summary>
    /// Reads the settings. Order: file, then environment — last one wins.
    /// </summary>
    public static Settings Read(string? path = null)
    {
        var file = path ?? Path.Combine(AppContext.BaseDirectory, "sensor.json");
        var text = File.Exists(file) ? File.ReadAllText(file) : null;
        return FromText(text);
    }

    /// <summary>
    /// Like <see cref="Read"/>, but from text — so a test can check that the
    /// defaults hold.
    /// </summary>
    internal static Settings FromText(string? json)
    {
        var e = new Settings();

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                e = JsonSerializer.Deserialize(json, SensorJson.Default.Settings) ?? e;
            }
            catch (JsonException ex)
            {
                // A broken file should say it is broken rather than quietly
                // falling back to defaults: otherwise you go looking for the
                // fault in the dashboard.
                Console.Error.WriteLine($"sensor.json cannot be read: {ex.Message}");
            }
        }

        // These keys were German up to version 0.9. They are still read,
        // and that is not ballast: a sensor.json already sitting on a machine
        // only knows the old names. Without these lines the sensor would find
        // neither address nor token after the next update - and would report
        // "address and token are missing" for something that is right there.
        return e with
        {
            BaseUrl = (EnvVar("AUSPEX_BASE") ?? EnvVar("AUSPEX_BASIS")
                       ?? Fill(e.BaseUrl, e.BaseUrlBis09)).TrimEnd('/'),
            Token = EnvVar("AUSPEX_TOKEN") ?? EnvVar("AUSPEX_ZEICHEN")
                    ?? Fill(e.Token, e.TokenBis09),
            VerboseRaw = (EnvVar("AUSPEX_VERBOSE") ?? EnvVar("AUSPEX_LAUT")) is { } verbose
                ? verbose is "1" or "true" or "ja" or "yes"
                : e.VerboseRaw,
        };
    }

    /// <summary>
    /// The new value, and if that is empty, the old one.
    ///
    /// <para>
    /// Both sides are <c>string?</c> even though the properties carry
    /// <c>= ""</c> initialisers — because that initialiser is precisely what
    /// does not survive deserialisation when the key is missing. The same
    /// trap is described at the top of this file; it sprang a second time
    /// right here, when the old key name was added.
    /// </para>
    /// </summary>
    private static string Fill(string? neu, string? alt) =>
        neu is { Length: > 0 } ? neu : alt ?? "";

    private static string? EnvVar(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value.Trim() : null;
}
