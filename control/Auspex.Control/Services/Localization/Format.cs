namespace Auspex.Control.Services.Localization;

// Date and time.
//
// .NET formats numbers correctly by itself as soon as the culture hangs off
// the request: "1.234" becomes "1,234" and "87,4 %" becomes "87.4%". For
// dates that is NOT the case once a fixed pattern sits in the code — and the
// interface had eleven of them, all in the form "dd.MM. HH:mm". Those would
// have looked German in English too.
//
// Hence here, and not as a pattern at the call site: there are three forms
// that get used, and each should be defined exactly once per language.

public abstract partial class Strings
{
    /// <summary>Day and time without a year — "24.08. 14:05" / "24 Aug, 14:05".</summary>
    public abstract string ShortDateTime(DateTimeOffset moment);

    /// <summary>The day only — "24.08." / "24 Aug".</summary>
    public abstract string ShortDate(DateTimeOffset moment);

    /// <summary>Day with year, no time — for axis labels.</summary>
    public abstract string DateWithYear(DateTimeOffset moment);

    /// <summary>With a year — for anything that can be more than a few days old.</summary>
    public abstract string LongDateTime(DateTimeOffset moment);

    /// <summary>
    /// A span as "3 hours ago". Roughly rounded: on an overview page "3
    /// hours ago" is more useful than "2 h 47 min ago".
    /// </summary>
    public abstract string TimeAgo(TimeSpan span);

    // ── The same for DateTime, and deliberately so ────────────────────────
    //
    // The database fields are all called "…Utc" and do contain UTC — but
    // SQLite returns them as DateTime with Kind.Unspecified, and that is a
    // time with no origin. What follows from it is worse than a wrong value,
    // because it looks like no conversion at all:
    //
    //   * .ToLocalTime() on Unspecified does NOTHING. The value stays UTC and
    //     gets labelled as local time.
    //   * The implicit conversion to DateTimeOffset is worse still: it reads
    //     Unspecified as local time and staples the local offset onto it.
    //     17:53 UTC becomes "17:53+02:00" — a moment that never existed, and
    //     one that stays unchanged through the next ToLocalTime().
    //
    // That is exactly what happened here: T.ShortDateTime(f.WindowStartUtc)
    // looked right and went through the implicit conversion.
    //
    // These overloads take the trap's teeth out. C# prefers the exact match
    // over an implicit conversion, so they apply at every existing call site
    // by themselves — and at every future one.

    /// <inheritdoc cref="ShortDateTime(DateTimeOffset)"/>
    public string ShortDateTime(DateTime utc) => ShortDateTime(AsUtc(utc));

    /// <inheritdoc cref="KurzDatum(DateTimeOffset)"/>
    public string ShortDate(DateTime utc) => ShortDate(AsUtc(utc));

    /// <inheritdoc cref="TagMitJahr(DateTimeOffset)"/>
    public string DateWithYear(DateTime utc) => DateWithYear(AsUtc(utc));

    /// <inheritdoc cref="LangDatumZeit(DateTimeOffset)"/>
    public string LongDateTime(DateTime utc) => LongDateTime(AsUtc(utc));

    /// <summary>
    /// Takes a moment at its word: the fields are called "…Utc", so it is
    /// UTC — even when the database has cut its Kind off.
    /// </summary>
    public static DateTimeOffset AsUtc(DateTime moment) =>
        new(DateTime.SpecifyKind(moment, DateTimeKind.Utc));

    /// <inheritdoc cref="AlsUtc(DateTime)"/>
    public static DateTimeOffset? AsUtc(DateTime? moment) =>
        moment is { } z ? AsUtc(z) : null;
}

public sealed partial class StringsDe
{
    public override string ShortDateTime(DateTimeOffset z) =>
        DisplayTime.ToDisplay(z).ToString("dd.MM. HH:mm");

    public override string ShortDate(DateTimeOffset z) =>
        DisplayTime.ToDisplay(z).ToString("dd.MM.");

    public override string DateWithYear(DateTimeOffset z) =>
        DisplayTime.ToDisplay(z).ToString("dd.MM.yyyy");

    public override string LongDateTime(DateTimeOffset z) =>
        DisplayTime.ToDisplay(z).ToString("dd.MM.yyyy HH:mm");

    public override string TimeAgo(TimeSpan s) =>
        s.TotalDays >= 1 ? $"vor {(int)s.TotalDays} {((int)s.TotalDays == 1 ? "Tag" : "Tagen")}"
        : s.TotalHours >= 1 ? $"vor {(int)s.TotalHours} {((int)s.TotalHours == 1 ? "Stunde" : "Stunden")}"
        : s.TotalMinutes >= 1 ? $"vor {(int)s.TotalMinutes} {((int)s.TotalMinutes == 1 ? "Minute" : "Minuten")}"
        : "gerade eben";
}

public sealed partial class StringsEn
{
    // "24 Aug, 14:05" - day first as in the original, month as an
    // abbreviation rather than a number. "08/24" would be the 8th day of the
    // 24th month in en-GB, and whoever reads it the American way arrives at a
    // different date from whoever reads it the British way. A month name
    // cannot be read the wrong way round.
    public override string ShortDateTime(DateTimeOffset z) =>
        DisplayTime.ToDisplay(z).ToString("d MMM, HH:mm");

    public override string ShortDate(DateTimeOffset z) =>
        DisplayTime.ToDisplay(z).ToString("d MMM");

    public override string DateWithYear(DateTimeOffset z) =>
        DisplayTime.ToDisplay(z).ToString("d MMM yyyy");

    public override string LongDateTime(DateTimeOffset z) =>
        DisplayTime.ToDisplay(z).ToString("d MMM yyyy, HH:mm");

    public override string TimeAgo(TimeSpan s) =>
        s.TotalDays >= 1 ? $"{(int)s.TotalDays} {((int)s.TotalDays == 1 ? "day" : "days")} ago"
        : s.TotalHours >= 1 ? $"{(int)s.TotalHours} {((int)s.TotalHours == 1 ? "hour" : "hours")} ago"
        : s.TotalMinutes >= 1 ? $"{(int)s.TotalMinutes} {((int)s.TotalMinutes == 1 ? "minute" : "minutes")} ago"
        : "just now";
}
