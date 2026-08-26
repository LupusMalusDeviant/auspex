using System.Text.Json;
using System.Text.Json.Serialization;

namespace Auspex.Control.Services;

/// <summary>
/// The chosen appearance — theme, accent, density, font size.
/// </summary>
public sealed record Appearance
{
    [JsonPropertyName("fassung")] public string Theme { get; init; } = "system";
    [JsonPropertyName("akzent")]  public string Accent  { get; init; } = "oxblut";
    [JsonPropertyName("dichte")]  public string Density  { get; init; } = "normal";
    [JsonPropertyName("schrift")] public string FontSize { get; init; } = "normal";

    /// <summary>
    /// The chosen language, as a code.
    ///
    /// <para>
    /// For the interface the cookie decides — the language has to be settled
    /// while the response is being built, and a cookie is the right means for
    /// that. It is here a second time for the same reason as the colour: the
    /// browser extension cannot reach the cookie. It has a different origin
    /// and identifies itself with a token, not with a session. Without this
    /// entry it would stay German while the dashboard next to it is English.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Optional, and that is the point: <c>appearance.js</c> sends only the
    /// four axes it knows about when the colour changes. With a fixed default
    /// here, every click on a colour swatch would silently reset the language
    /// to German — and the extension would follow. Null means "I am saying
    /// nothing about this"; the store then keeps what it has.
    /// </remarks>
    [JsonPropertyName("sprache")] public string? Language { get; init; }

    /// <summary>
    /// Which time zone clock times are shown in, as an IANA name
    /// ("Europe/Berlin").
    ///
    /// <para>
    /// Optional for the same reason as the language — <c>appearance.js</c>
    /// sends only its four axes when the colour changes. Here null means
    /// something extra as well: "no choice made", and then the container's
    /// zone from <c>TZ</c> applies. The setting overrides the default, it
    /// does not replace it.
    /// </para>
    /// </summary>
    [JsonPropertyName("zeitzone")] public string? TimeZone { get; init; }

    /// <summary>
    /// Hue and chroma of the accent. The browser gets them supplied, so the
    /// extension does not have to keep the same table a second time — two
    /// copies of a colour table drift apart the moment a tone is added.
    /// </summary>
    [JsonPropertyName("h")] public int H { get; init; } = 15;
    [JsonPropertyName("c")] public double C { get; init; } = 0.105;
}

/// <summary>
/// Holds the appearance server-side.
///
/// It used to live only in the browser's <c>localStorage</c>. That has two
/// drawbacks, both of which only show in everyday use: the choice applies on
/// <em>one</em> machine only, and the browser extension cannot reach it at
/// all — <c>localStorage</c> is bound to the origin, and an extension has a
/// different one.
///
/// The browser stays the fast source all the same: it paints from its own
/// storage before the first network call comes back, and reports the change
/// here afterwards. This file is the truth, the <c>localStorage</c> is the
/// cache in front of it.
/// </summary>
public sealed class AppearanceStore : IAppearanceStore
{
    /// <summary>
    /// The tones. They are here <em>and</em> in appearance.js — the server
    /// has to be able to deliver H and C without a browser having asked, and
    /// the script has to be able to paint before an answer is there. To stop
    /// the two drifting apart, a test checks them against each other.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (int H, double C)> Tones =
        new Dictionary<string, (int, double)>(StringComparer.OrdinalIgnoreCase)
        {
            ["oxblut"]  = (15,  0.105),
            ["rost"]    = (45,  0.115),
            ["messing"] = (80,  0.110),
            ["moos"]    = (145, 0.095),
            ["petrol"]  = (195, 0.095),
            ["stahl"]   = (240, 0.090),
            ["indigo"]  = (280, 0.100),
            ["pflaume"] = (330, 0.100),
        };

    private static readonly string[] Themes = ["system", "hell", "dunkel"];
    private static readonly string[] Languages =
        [.. Localization.Strings.Languages.Select(x => x.Code)];
    private static readonly string[] Densities = ["kompakt", "normal", "luftig"];
    private static readonly string[] FontSizes = ["klein", "normal", "gross"];

    private readonly string _path;
    private readonly ILogger<AppearanceStore> _log;
    private readonly Lock _lock = new();
    private Appearance _current = new();

    public AppearanceStore(IConfiguration configuration, ILogger<AppearanceStore> log)
    {
        _log = log;
        // The fallback lands next to the executable, which in a container
        // means inside the image: on the running installation the file was
        // never there, and the chosen time zone, language and accent went
        // away with every rebuild. The Dockerfile now points this at the
        // volume; the fallback stays for a plain "dotnet run".
        _path = configuration["Appearance:Path"]
                ?? Path.Combine(AppContext.BaseDirectory, "appearance.json");
        Load();
    }

    public Appearance Current
    {
        get { lock (_lock) { return _current; } }
    }

    /// <summary>
    /// Takes a choice. Unknown values are reset to the default rather than
    /// rejected: a browser with an old <c>localStorage</c> entry should not
    /// land on an error message but on an interface that looks like one.
    /// </summary>
    public Appearance Set(Appearance wish)
    {
        var akzent = Tones.ContainsKey(wish.Accent) ? wish.Accent.ToLowerInvariant() : "oxblut";
        var (h, c) = Tones[akzent];

        var clean = new Appearance
        {
            Theme = Themes.Contains(wish.Theme) ? wish.Theme : "system",
            Accent = akzent,
            Density = Densities.Contains(wish.Density) ? wish.Density : "normal",
            FontSize = FontSizes.Contains(wish.FontSize) ? wish.FontSize : "normal",
            // Only take it if something is actually there - otherwise what was
            // already chosen stays.
            Language = wish.Language is { Length: > 0 } wanted
                      && Languages.Contains(wanted)
                ? wanted
                : _current.Language ?? Languages[0],
            // An unknown zone does not silently become UTC, it is simply not
            // taken - otherwise a typo in the name looks like a zone that
            // happens to shift nothing.
            TimeZone = wish.TimeZone is { Length: > 0 } zone
                       && Localization.DisplayTime.Knows(zone, out _)
                ? zone
                : _current.TimeZone,
            H = h,
            C = c,
        };

        lock (_lock)
        {
            _current = clean;
        }

        ApplyZone(clean.TimeZone);
        Save(clean);
        return clean;
    }

    /// <summary>
    /// Sets the time zone only. Like <see cref="SetLanguage"/> an axis of its
    /// own: the dropdown on the settings page sends a zone and nothing else.
    /// </summary>
    /// <param name="name">
    /// IANA name, or empty for "no choice" — then the container's zone
    /// applies again.
    /// </param>
    public void SetTimeZone(string? name)
    {
        var chosen = name is { Length: > 0 } n && Localization.DisplayTime.Knows(n, out _)
            ? n.Trim()
            : null;

        lock (_lock)
        {
            _current = _current with { TimeZone = chosen };
        }

        ApplyZone(chosen);
        Save(Current);
    }

    /// <summary>
    /// Enters the choice where the display reads it.
    ///
    /// <para>
    /// With no choice, <see cref="TimeZoneInfo.Local"/> applies, that is the
    /// container's zone from <c>TZ</c>. So the previous default stays the
    /// default, and whoever never sets anything notices nothing of this axis.
    /// </para>
    /// </summary>
    private void ApplyZone(string? name)
    {
        if (name is { Length: > 0 } && Localization.DisplayTime.Knows(name, out var zone))
        {
            Localization.DisplayTime.Set(zone);
            return;
        }

        Localization.DisplayTime.Set(TimeZoneInfo.Local);
    }

    /// <summary>
    /// Sets the language only and leaves everything else standing. The switch
    /// in the header changes exactly one axis; going through
    /// <see cref="Set"/> it would have to send the other three along and
    /// could reset them by accident.
    /// </summary>
    public void SetLanguage(string code)
    {
        lock (_lock)
        {
            _current = _current with
            {
                Language = Languages.Contains(code) ? code : Languages[0],
            };
        }

        Save(Current);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var read = JsonSerializer.Deserialize<Appearance>(File.ReadAllText(_path));
            if (read is not null)
            {
                // Through Set, so a file bent by hand goes through the same
                // checking as an input does.
                Set(read);
            }
        }
        catch (Exception ex)
        {
            // A broken file must not prevent startup - then the default it is.
            // That is visible immediately.
            _log.LogWarning(ex, "The appearance cannot be read, the default applies");
        }
    }

    private void Save(Appearance d)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(d, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
        }
        catch (Exception ex)
        {
            // Not writable: the choice applies until the next restart. Better
            // than an error in the interface for something purely cosmetic.
            _log.LogWarning(ex, "The appearance cannot be written");
        }
    }
}
