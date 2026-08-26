using System.Globalization;
using System.Reflection;
using Auspex.Control.Services;
using Auspex.Control.Services.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auspex.Control.Tests;

/// <summary>
/// Two things can go wrong with a translation, and neither shows in daily
/// use.
///
/// <para>
/// One is a gap: a text that exists only in German. The abstract class
/// prevents that at translation time — but it cannot prevent somebody
/// copying the German sentence into the English field and leaving it there.
/// That is what <see cref="No_German_sentence_in_the_English_version"/>
/// checks.
/// </para>
///
/// <para>
/// The other is the redirect in the language switch. It is the one place in
/// the whole rebuild where a mistake costs more than an ugly word.
/// </para>
/// </summary>
public class LanguageTests
{
    // ── The switch must not lead outside ──────────────────────────

    [Theory]
    [InlineData("/")]
    [InlineData("/querylog")]
    [InlineData("/router/ports")]
    [InlineData("/geraete?filter=abc")]
    public void Local_paths_are_kept(string path)
    {
        Assert.True(ReturnPath.IsLocal(path));
        Assert.Equal(path, ReturnPath.Safe(path));
    }

    [Theory]
    // Protocol-relative: looks local, leads outside.
    [InlineData("//fremde.example")]
    [InlineData("//fremde.example/auspex")]
    // Browsers read the backslash here like an ordinary one.
    [InlineData("/\\fremde.example")]
    [InlineData("https://fremde.example")]
    [InlineData("http://fremde.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("fremde.example")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_lands_on_the_overview(string? destination)
    {
        Assert.False(ReturnPath.IsLocal(destination));
        Assert.Equal("/", ReturnPath.Safe(destination));
    }

    // ── Die Sprachwahl selbst ─────────────────────────────────────────────

    [Theory]
    [InlineData("de", "de")]
    [InlineData("de-DE", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("en", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("en-US", "en")]
    // Whatever we do not speak gets the original.
    [InlineData("fr-FR", "de")]
    [InlineData("ja-JP", "de")]
    public void A_culture_finds_its_language(string culture, string erwartet) =>
        Assert.Equal(erwartet, Strings.For(new CultureInfo(culture)).Code);

    [Fact]
    public void An_unknown_code_has_no_culture() =>
        Assert.Null(Strings.CultureToCode("kl"));

    [Fact]
    public void German_comes_first_and_is_the_fallback()
    {
        // The middleware takes Cultures[0] as the default. With English
        // first, everybody without a cookie would get an English interface
        // — a silent switch nobody asked for.
        Assert.Equal("de-DE", Strings.Kulturen[0]);
    }

    // ── Completeness and cleanliness ──────────────────────────────

    /// <summary>
    /// All text members that return a string without arguments. Methods with
    /// parameters (<c>AccentName</c>, say) are not here; those are checked
    /// one by one.
    /// </summary>
    private static IEnumerable<(string Name, string Value)> Sentences(Strings t) =>
        typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => (p.Name, (string)p.GetValue(t)!));

    [Fact]
    public void No_text_is_empty()
    {
        foreach (var (name, value) in Sentences(new StringsDe()).Concat(Sentences(new StringsEn())))
        {
            Assert.False(string.IsNullOrWhiteSpace(value), $"{name} ist leer");
        }
    }

    /// <summary>
    /// The gap the compiler does not see: an English field still holding the
    /// German sentence.
    ///
    /// <para>
    /// Umlauts and eszett are a crude but reliable indicator for that — and
    /// the words below catch the German sentences without an umlaut. Neither
    /// produces an English hit that would need explaining: "Router" and
    /// "Indigo" are spelt the same in both languages and are therefore not
    /// on the list.
    /// </para>
    /// </summary>
    [Fact]
    public void No_German_sentence_in_the_English_version()
    {
        string[] giveaways =
        [
            "ä", "ö", "ü", "ß",
            " der ", " die ", " das ", " und ", " nicht ", " kein ", " keine ",
            " wird ", " werden ", " wurde ", " ist ", " sind ", " von ", " mit ",
        ];

        // A justified exception, not a softening: the instructions for the
        // router account name menu items of the Fritz!Box. A box with German
        // firmware has no item called "FRITZ!Box users" — translating the
        // path sends somebody looking for something that is called
        // differently on their screen. The sentence around it is English,
        // the path inside it stays as it stands on the device.
        string[] darfDeutschSein = ["RouterAccountHowTo", "WhatThisAccountMeans"];

        var en = new StringsEn();
        var gefunden = Sentences(en)
            .Where(s => !darfDeutschSein.Contains(s.Name))
            .Where(s => giveaways.Any(v =>
                $" {s.Value} ".Contains(v, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(gefunden.Count == 0,
            "Noch deutsch in der englischen Fassung: "
            + string.Join(", ", gefunden.Select(g => $"{g.Name} = \"{g.Value}\"")));
    }

    [Fact]
    public void The_two_versions_do_not_say_the_same_thing()
    {
        // A few words are the same in both languages — "Auto", "Normal",
        // "Router", "Control". If it were ALL of them, somebody would have
        // copied the English version from the German one without touching
        // it.
        var de = Sentences(new StringsDe()).ToDictionary(s => s.Name, s => s.Value);
        var en = Sentences(new StringsEn()).ToDictionary(s => s.Name, s => s.Value);

        var gleich = de.Count(p => en[p.Key] == p.Value);
        Assert.True(gleich < de.Count / 2,
            $"{gleich} von {de.Count} Texten sind in beiden Sprachen identisch — "
            + "das sieht nach einer nicht übersetzten Kopie aus.");
    }

    [Fact]
    public void Every_accent_tone_has_a_name_in_both_languages()
    {
        string[] tones =
            ["oxblut", "rost", "messing", "moos", "petrol", "stahl", "indigo", "pflaume"];

        foreach (var tone in tones)
        {
            foreach (Strings t in new Strings[] { new StringsDe(), new StringsEn() })
            {
                var name = t.AccentName(tone);
                Assert.False(string.IsNullOrWhiteSpace(name));
                // The fallback returns the key itself. If that comes out, the
                // tone is missing from the mapping.
                Assert.NotEqual(tone, name);
            }
        }
    }

    [Fact]
    public void Petrol_is_not_called_petrol_in_English()
    {
        // The one colour name where the literal transfer means something
        // else: "petrol" in English is the fuel.
        Assert.Equal("Teal", new StringsEn().AccentName("petrol"));
    }

    // ── The language must not disappear in passing ──────────────────

    /// <summary>
    /// When a colour swatch changes, the dashboard sends only the four axes
    /// <c>appearance.js</c> knows about — the language is not among them.
    /// If <c>Set</c> then writes the default, every click on a colour resets
    /// the language, and the browser extension jumps back to German the next
    /// time it is opened. Nobody notices that straight away: colour and
    /// language have nothing to do with each other, and nobody looks there.
    /// </summary>
    [Fact]
    public void A_colour_change_leaves_the_language_alone()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            var store = Store(path);
            store.SetLanguage("en");
            Assert.Equal("en", store.Current.Language);

            // Exactly what appearance.js sends: four axes, no language.
            store.Set(new Appearance
            {
                Theme = "dunkel",
                Accent = "moos",
                Density = "luftig",
                FontSize = "gross",
            });

            Assert.Equal("en", store.Current.Language);
            Assert.Equal("moos", store.Current.Accent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_unknown_language_falls_back_to_the_default()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            var store = Store(path);
            store.SetLanguage("kl");
            Assert.Equal("de", store.Current.Language);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static AppearanceStore Store(string path)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Appearance:Path"] = path })
            .Build();
        return new AppearanceStore(configuration,
            LoggerFactory.Create(b => { }).CreateLogger<AppearanceStore>());
    }

    // ── Kultur formatiert mit ─────────────────────────────────────────────

    [Fact]
    public void Numbers_follow_the_culture()
    {
        // This is the part of the translation nobody does by hand — and the
        // reason the culture hangs off the request and not off the process.
        var de = new CultureInfo("de-DE");
        var en = new CultureInfo("en-GB");

        Assert.Equal("1.234", 1234.ToString("N0", de));
        Assert.Equal("1,234", 1234.ToString("N0", en));
        Assert.Equal("87,4 %", 0.874.ToString("P1", de).Replace(' ', ' ').Replace(' ', ' '));
        Assert.Equal("87.4%", 0.874.ToString("P1", en));
    }

    [Fact]
    public void English_sticks_to_the_twenty_four_hour_day()
    {
        // en-GB and not en-US: a log with "2:05 PM" is harder to read than
        // one with "14:05". If this test breaks, somebody has changed the
        // culture — and the times in the query log would look different from
        // what the page means.
        var en = new CultureInfo("en-GB");
        var nachmittag = new DateTime(2026, 8, 24, 14, 5, 0, DateTimeKind.Local);

        var shortName = nachmittag.ToString("t", en);
        Assert.DoesNotContain("PM", shortName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("14", shortName);
    }
}

/// <summary>
/// Timestamps from the database.
///
/// <para>
/// SQLite returns DateTime with <c>Kind.Unspecified</c> - a time without an
/// origin. Both routes from there to a display then go silently wrong:
/// <c>ToLocalTime()</c> does nothing at all on Unspecified, and the implicit
/// conversion to DateTimeOffset attaches the local offset to the UTC value.
/// 17:53 UTC becomes "17:53+02:00" - a moment that never existed.
/// </para>
/// </summary>
public class LocalTimeTests
{
    private static readonly DateTime FromTheDatabase = new(2026, 8, 25, 17, 53, 16);

    [Fact]
    public void The_database_returns_time_without_an_origin()
    {
        // The premise of the whole problem - should it one day stop holding,
        // it ought to show up here and not somewhere else.
        Assert.Equal(DateTimeKind.Unspecified, FromTheDatabase.Kind);
    }

    [Fact]
    public void AsUtc_takes_the_field_name_at_its_word()
    {
        var z = Strings.AsUtc(FromTheDatabase);

        Assert.Equal(TimeSpan.Zero, z.Offset);
        Assert.Equal(FromTheDatabase, z.UtcDateTime);
    }

    [Fact]
    public void Nothing_produces_nothing()
    {
        Assert.Null(Strings.AsUtc((DateTime?)null));
        Assert.Equal(TimeSpan.Zero, Strings.AsUtc((DateTime?)FromTheDatabase)!.Value.Offset);
    }

    /// <summary>
    /// The actual regression guard: the overload has to beat the implicit
    /// conversion.
    ///
    /// <para>
    /// To be honest about it: on a machine in UTC this test is blind,
    /// because both routes give the same answer there - and CI runs in UTC.
    /// On any machine with an offset it bites. Which is why another one sits
    /// below it that takes the zone into its own hands.
    /// </para>
    /// </summary>
    [Fact]
    public void The_overload_beats_the_implicit_conversion()
    {
        var t = Strings.For("de");
        var erwartet = TimeZoneInfo
            .ConvertTimeFromUtc(FromTheDatabase, TimeZoneInfo.Local)
            .ToString("dd.MM. HH:mm", CultureInfo.InvariantCulture);

        Assert.Equal(erwartet, t.ShortDateTime(FromTheDatabase));
    }

    [Theory]
    [InlineData("Europe/Berlin", "25.08. 19:53")]
    [InlineData("Europe/Rome", "25.08. 19:53")]
    [InlineData("UTC", "25.08. 17:53")]
    public void In_a_chosen_zone_the_time_is_right(string zone, string erwartet)
    {
        // This does not trust TimeZoneInfo.Local but computes: so the test
        // bites on a machine standing in UTC as well.
        var tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
        var localTime = TimeZoneInfo.ConvertTime(Strings.AsUtc(FromTheDatabase), tz);

        Assert.Equal(erwartet, localTime.ToString("dd.MM. HH:mm", CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// The configurable display zone.
///
/// <para>
/// The tests set a process-wide zone and therefore have to reset it
/// afterwards - otherwise one rubs off on the next.
/// </para>
/// </summary>
public class DisplayTimeTests : IDisposable
{
    private readonly TimeZoneInfo _before = DisplayTime.Zone;

    public void Dispose() => DisplayTime.Set(_before);

    [Fact]
    public void A_chosen_zone_rubs_off_on_the_display()
    {
        var utc = new DateTimeOffset(2026, 8, 25, 17, 53, 0, TimeSpan.Zero);
        var t = Strings.For("de");

        DisplayTime.Set(TimeZoneInfo.FindSystemTimeZoneById("UTC"));
        Assert.Equal("25.08. 17:53", t.ShortDateTime(utc));

        DisplayTime.Set(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
        Assert.Equal("25.08. 19:53", t.ShortDateTime(utc));

        DisplayTime.Set(TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));
        Assert.Equal("26.08. 02:53", t.ShortDateTime(utc));
    }

    /// <summary>
    /// The change of day is the reason the date has to travel along and not
    /// only the clock time: in Tokyo it is already the next day.
    /// </summary>
    [Fact]
    public void The_date_travels_with_it_too()
    {
        DisplayTime.Set(TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));
        var t = Strings.For("de");

        Assert.Equal(
            "26.08.",
            t.ShortDate(new DateTimeOffset(2026, 8, 25, 17, 53, 0, TimeSpan.Zero)));
    }

    /// <summary>
    /// Summer and winter time: a zone is not a fixed offset. Which is
    /// exactly why the setting says "Europe/Berlin" and not "+02:00".
    /// </summary>
    [Fact]
    public void In_winter_a_different_offset_applies()
    {
        DisplayTime.Set(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
        var t = Strings.For("de");

        Assert.Equal("25.08. 19:53", t.ShortDateTime(
            new DateTimeOffset(2026, 8, 25, 17, 53, 0, TimeSpan.Zero)));
        Assert.Equal("25.12. 18:53", t.ShortDateTime(
            new DateTimeOffset(2026, 12, 25, 17, 53, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void An_unknown_name_does_not_silently_become_UTC()
    {
        Assert.False(DisplayTime.Knows("Europa/Berlin", out _));
        Assert.False(DisplayTime.Knows("", out _));
        Assert.False(DisplayTime.Knows(null, out _));
        Assert.True(DisplayTime.Knows("Europe/Berlin", out var zone));
        Assert.Equal("Europe/Berlin", zone.Id);
    }

    [Fact]
    public void The_dropdown_is_sorted_by_offset_and_contains_Berlin()
    {
        var list = DisplayTime.Selectable();

        Assert.Contains(list, x => x.Name == "Europe/Berlin");
        Assert.All(list, x => Assert.StartsWith("(UTC", x.Label));

        // Always IANA, never Windows names: the choice is stored and later
        // read by the Linux container. "W. Europe Standard Time" would be
        // worthless there. Without this assurance the result would depend on
        // which operating system somebody opened the settings on.
        Assert.All(list, x => Assert.DoesNotContain(" Standard Time", x.Name));
        Assert.All(list, x => Assert.True(
            x.Name.Contains('/') || x.Name is "UTC",
            $"kein IANA-Name: {x.Name}"));

        var offsets = list
            .Select(x => TimeZoneInfo.FindSystemTimeZoneById(x.Name).GetUtcOffset(DateTime.UtcNow))
            .ToList();
        Assert.Equal(offsets.OrderBy(v => v), offsets);
    }

    /// <summary>
    /// Night detection has to use the same zone as the display - otherwise
    /// "at night" stands on a finding with 05:00 printed next to it.
    /// </summary>
    [Fact]
    public void Night_follows_the_same_zone()
    {
        // 02:00 UTC: in Berlin 04:00 (night), in Tokyo 11:00 (not remotely).
        var window = new DateTime(2026, 8, 25, 2, 0, 0);

        DisplayTime.Set(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
        Assert.Equal(4, DisplayTime.ToDisplay(window).Hour);

        DisplayTime.Set(TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));
        Assert.Equal(11, DisplayTime.ToDisplay(window).Hour);
    }
}
