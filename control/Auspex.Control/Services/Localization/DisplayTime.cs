namespace Auspex.Control.Services.Localization;

/// <summary>
/// Which time zone the dashboard shows its clock times in.
///
/// <para>
/// This used to be <c>ToLocalTime()</c>, that is, the container's zone — and
/// that was UTC while the house is in Berlin. Every clock time was two hours
/// early, with nothing anywhere looking broken. After that the zone came
/// from an environment variable, which fixed the fault but put a setting in
/// a place you can only reach with a shell. Now it stands where all the
/// other display questions stand.
/// </para>
///
/// <para>
/// <strong>Process-wide and not per session</strong>, and that is a
/// decision: Auspex watches <em>one</em> network in <em>one</em> place. A
/// query at 03:12 is a night-time event even if somebody is looking at it
/// from Tokyo — the time of day is part of the statement here and not a
/// convenience for the viewer. For the same reason it does not hang off the
/// language: English does not mean living somewhere else.
/// </para>
///
/// <para>
/// With no choice made, the container's zone stands. That comes from
/// <c>TZ</c> in compose.yml and remains the default — the setting overrides
/// it, it does not replace it.
/// </para>
/// </summary>
public static class DisplayTime
{
    private static TimeZoneInfo _zone = TimeZoneInfo.Local;

    /// <summary>The zone things are shown in.</summary>
    public static TimeZoneInfo Zone => _zone;

    /// <summary>
    /// Sets the zone. The caller is the <c>AppearanceStore</c> — on load and
    /// on every change, so there is exactly one truth.
    /// </summary>
    public static void Set(TimeZoneInfo zone) => _zone = zone;

    /// <summary>
    /// Looks a zone up and says whether it exists.
    ///
    /// <para>
    /// An unknown name must not slip through: it would otherwise produce UTC
    /// without complaint, and that would look like a time zone that simply
    /// shifts nothing.
    /// </para>
    /// </summary>
    public static bool Knows(string? name, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Local;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(name.Trim());
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>A moment in the display zone.</summary>
    public static DateTimeOffset ToDisplay(DateTimeOffset moment) =>
        TimeZoneInfo.ConvertTime(moment, _zone);

    /// <summary>
    /// The same for a <c>DateTime</c> from the database — that comes back
    /// with <c>Kind.Unspecified</c> and is taken here at the field's word.
    /// See <see cref="Strings.AsUtc(DateTime)"/>.
    /// </summary>
    public static DateTimeOffset ToDisplay(DateTime utc) => ToDisplay(Strings.AsUtc(utc));

    /// <inheritdoc cref="In(DateTime)"/>
    public static DateTimeOffset? ToDisplay(DateTime? utc) =>
        utc is { } u ? ToDisplay(u) : null;

    /// <summary>
    /// Every selectable zone, sorted by offset and labelled with it —
    /// "(UTC+02:00) Europe/Berlin".
    ///
    /// <para>
    /// The system's complete list and not a hand-picked one: a selection of
    /// "the most important zones" is always the list of somebody who lives
    /// somewhere other than the next person. Sorted by offset, because you
    /// recognise your zone by its distance from UTC sooner than by its
    /// initial letter.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(string Name, string Label)> Selectable()
    {
        var now = DateTime.UtcNow;
        return
        [
            .. TimeZoneInfo.GetSystemTimeZones()
                .Select(z => (Name: AlsIana(z), Zone: z))
                .Where(x => x.Name is not null)
                .DistinctBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => (x.Name, x.Zone, Offset: x.Zone.GetUtcOffset(now)))
                .OrderBy(x => x.Offset)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => (
                    Name: x.Name!,
                    Label: $"(UTC{(x.Offset < TimeSpan.Zero ? "-" : "+")}"
                                  + $"{x.Offset.Duration():hh\\:mm}) {x.Name}")),
        ];
    }

    /// <summary>
    /// A zone's IANA name, or null when there is none.
    ///
    /// <para>
    /// <strong>Not cosmetic.</strong> Windows calls its zones "W. Europe
    /// Standard Time", Linux "Europe/Berlin" — and
    /// <see cref="TimeZoneInfo.GetSystemTimeZones"/> returns whatever the
    /// system happens to carry. The choice lands in <c>appearance.json</c>
    /// though, and the container reads that later. A Windows name stored on
    /// a Windows machine would be unknown there at best and silently
    /// something else at worst.
    /// </para>
    ///
    /// <para>
    /// It showed on a test that would have been red here and green in CI: CI
    /// runs on Linux, this machine does not.
    /// </para>
    /// </summary>
    private static string? AlsIana(TimeZoneInfo zone) =>
        zone.HasIanaId ? zone.Id
        : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana
        : null;
}
