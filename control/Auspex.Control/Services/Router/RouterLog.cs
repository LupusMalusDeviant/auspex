using System.Text.RegularExpressions;

namespace Auspex.Control.Services.Router;
using Auspex.Control.Services.Localization;

/// <summary>
/// Splits the router's event log into individual entries.
///
/// TR-064 delivers it as a single string in which date, time and message run
/// together with no line break — several hundred events as a wall of text.
/// In that form it is useless: you cannot filter, cannot search and cannot
/// tell whether a message is from yesterday or from July.
/// </summary>
public static partial class RouterLog
{
    // The Fritz!Box format: "01.08.26 03:19:12 message text". The next
    // entry begins with the next timestamp - that is where it is split.
    [GeneratedRegex(@"(\d{2}\.\d{2}\.\d{2})\s+(\d{2}:\d{2}:\d{2})\s+")]
    private static partial Regex TimestampPattern();

    public static IReadOnlyList<RouterLogEntry> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var hits = TimestampPattern().Matches(raw);
        var entries = new List<RouterLogEntry>(hits.Count);

        for (var i = 0; i < hits.Count; i++)
        {
            var m = hits[i];
            var start = m.Index + m.Length;
            var end = i + 1 < hits.Count ? hits[i + 1].Index : raw.Length;
            var text = raw[start..end].Trim();
            if (text.Length == 0)
            {
                continue;
            }

            entries.Add(new RouterLogEntry(
                Date: m.Groups[1].Value,
                Time: m.Groups[2].Value,
                Timestamp: ReadTimestamp(m.Groups[1].Value, m.Groups[2].Value),
                Text: text,
                Kategorie: Einordnen(text)));
        }

        return entries;
    }

    private static DateTime? ReadTimestamp(string date, string time)
    {
        // "01.08.26" - two-digit year, meaning 20xx.
        return DateTime.TryParseExact(
            $"{date} {time}", "dd.MM.yy HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var t)
            ? t
            : null;
    }

    /// <summary>
    /// A rough classification by what the text says. Deliberately rough: the
    /// point is to cut a hundred lines down to the ten that matter right now
    /// — not to classify every message exactly.
    /// </summary>
    private static string Einordnen(string text)
    {
        var t = text.ToLowerInvariant();

        if (t.Contains("fehler") || t.Contains("not successful") || t.Contains("abgelehnt")
            || t.Contains("antwortet nicht") || t.Contains("zeitüberschreitung"))
        {
            return "fehler";
        }
        if (t.Contains("wlan") || t.Contains("funk") || t.Contains("kanal"))
        {
            return "wlan";
        }
        if (t.Contains("internetverbindung") || t.Contains("dsl") || t.Contains("pppoe")
            || t.Contains("präfix") || t.Contains("ip-adresse"))
        {
            return "internet";
        }
        if (t.Contains("anmeld") || t.Contains("kennwort") || t.Contains("benutzer"))
        {
            return "anmeldung";
        }
        if (t.Contains("telefon") || t.Contains("anruf") || t.Contains("rufnummer"))
        {
            return "telefonie";
        }
        return "sonstiges";
    }
}

public record RouterLogEntry(
    string Date,
    string Time,
    DateTime? Timestamp,
    string Text,
    string Kategorie)
{
    /// <summary>
    /// Whether the message sounds like a problem. Only drives the colouring —
    /// a message containing "error" need not be a fault.
    /// </summary>
    public bool IsError => Kategorie == "fehler";
}

/// <summary>One of the router's wireless networks.</summary>
public record RouterWlan(
    string ControlUrl,
    string Ssid,
    bool Enabled,
    string Band,
    string Channel,
    string Security,
    bool IsGuest)
{
    public string Instance => ControlUrl.TrimEnd('/').Split('/').LastOrDefault() ?? ControlUrl;

    /// <summary>
    /// The frequency band as people call it. TR-064 supplies "2400", "5000",
    /// "6000" or "unknown" — as a number in a table that sits there like a
    /// measurement, when it is a category.
    /// </summary>
    public string? BandLesbar => Band switch
    {
        // The comma hangs off the language: in English it is "2.4 GHz".
        //
        // Not through ToString with the ambient culture - outside a request
        // that is the machine's, which makes the value depend on which
        // machine it is produced on. That is exactly what the test run on the
        // build server exposed: locally "2,4" came out, there "2.4".
        "2400" => Strings.Current.WlanBand24,
        "5000" => "5 GHz",
        "6000" => "6 GHz",
        null or "" or "unknown" => null,
        var b => b,
    };

    /// <summary>
    /// The encryption in plain words. The box reports the beacon type —
    /// "11iandWPA3" is correct but tells nobody anything; what is meant is
    /// WPA2 and WPA3 side by side.
    ///
    /// <para>
    /// The "and" between them is the only part of it that has a language —
    /// WPA2 and WPA3 are called that everywhere. It therefore comes from
    /// <c>Strings</c>, the rest stays here.
    /// </para>
    /// </summary>
    public string SecurityReadable => Security switch
    {
        "None" => Strings.Current.WlanOpen,
        "Basic" => Strings.Current.WlanWep,
        "11i" => "WPA2",
        "WPAand11i" => Strings.Current.And("WPA", "WPA2"),
        "11iandWPA3" => Strings.Current.And("WPA2", "WPA3"),
        "WPA3" => "WPA3",
        null or "" => Strings.Current.Unknown,
        var t => t,
    };

    /// <summary>
    /// Whether the encryption is cause for concern. An open or WEP-secured
    /// network is not a detail in the margin.
    /// </summary>
    public bool SecurityWeak => Security is "None" or "Basic";

    /// <summary>
    /// What should stand in the list. The name alone is not enough: a
    /// Fritz!Box likes to give 2.4 and 5 GHz the same one, and then two
    /// identical-looking rows sit under each other.
    /// </summary>
    public string DisplayName =>
        BandLesbar is { } b ? $"{Ssid} · {b}" : Ssid;
}

/// <summary>One port mapping.</summary>
public record RouterPortMapping(
    string Description,
    string Protocol,
    string ExternalPort,
    string InternalPort,
    string InternalClient,
    bool Enabled,
    string RemoteHost);


/// <summary>
/// A list from the router — together with the information whether it came
/// about at all.
///
/// The reason is a fault that must not happen in a security tool: on a
/// rejected sign-in the calls returned an empty list, indistinguishable from
/// "there really is nothing". The port mappings page then reported "0
/// mappings — no door leads in from outside". That is not imprecision, it is
/// a false statement about the security of the network, and it arrives of
/// all times when the credentials have stopped being right.
///
/// Empty and "could not ask" are two different things from here on.
/// </summary>
public sealed record RouterList<T>(IReadOnlyList<T> Entries, string? Error)
{
    public bool Ok => Error is null;
    public int Count => Entries.Count;

    public static RouterList<T> From(IReadOnlyList<T> entries) => new(entries, null);

    public static RouterList<T> Failure(string error) => new([], error);

    /// <summary>
    /// When the router does not carry the service at all. Not an error but a
    /// property of the device — and not a "there is nothing" either.
    /// </summary>
    public static RouterList<T> Unknown(string was) =>
        new([], Strings.Current.RouterDoesNotKnow(was));
}
