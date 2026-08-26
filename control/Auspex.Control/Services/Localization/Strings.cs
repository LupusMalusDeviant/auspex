using System.Globalization;

namespace Auspex.Control.Services.Localization;

/// <summary>
/// The interface's display text, once per language.
///
/// <para>
/// Deliberately <em>not</em> <c>.resx</c> files with <c>IStringLocalizer</c>,
/// even though that would be the usual route. The reason lies in the nature
/// of the text: Auspex does not label, Auspex talks. "No door leads in from
/// outside" is not a caption but a sentence — and sentences with numbers
/// inserted need a different word order in every language.
/// </para>
///
/// <para>
/// A dictionary of keys and strings falls over on two counts with that sort
/// of thing. First silently: with a key missing, <c>IStringLocalizer</c>
/// returns the key name, and the page shows "QueryLog_Summary" where a
/// sentence should be — visible only when somebody looks. Second on
/// insertion: <c>{0}</c> and <c>{1}</c> do not say what they mean, and a
/// swapped pair produces a grammatically flawless, wrong sentence.
/// </para>
///
/// <para>
/// So the text stands here as <c>abstract</c>. Whoever adds a line has to
/// add it in <em>every</em> language, or the compiler turns that into an
/// error. That was precisely my worry on this point — a half-translated
/// interface is worse than a monolingual one — and this way it cannot arise
/// in the first place.
/// </para>
///
/// <para>
/// The class is spread across several files, one per area of the interface;
/// each file carries the abstract declaration and both translations side by
/// side. Translating then means opening one file and writing the sentence
/// underneath, not jumping between two XML trees.
/// </para>
/// </summary>
public abstract partial class Strings
{
    /// <summary>The language code, as it appears in the switch and the cookie.</summary>
    public abstract string Code { get; }

    /// <summary>The name of the language in itself — "Deutsch", "English".</summary>
    public abstract string OwnName { get; }

    /// <summary>
    /// The supported languages, in the order of the switch.
    ///
    /// <para>
    /// English as <c>en-GB</c> and not <c>en-US</c>: Auspex is a log, and a
    /// log with "2:05 PM" is harder to read than one with "14:05".
    /// <c>en-GB</c> writes the time on a 24-hour clock and the date day
    /// first — putting it closer to the German original without keeping
    /// anything German about it. What visibly changes are decimal separators
    /// (1.234,5 against 1,234.5), month names and weekday order.
    /// </para>
    /// </summary>
    public static readonly (string Culture, string Code)[] Languages =
    [
        ("de-DE", "de"),
        ("en-GB", "en"),
    ];

    /// <summary>Die Kulturkennungen, wie die Middleware sie erwartet.</summary>
    public static string[] Kulturen => [.. Languages.Select(s => s.Culture)];

    /// <summary>
    /// Picks the text for a culture. Anything unknown falls back to German
    /// rather than failing: a foreign language setting in the browser should
    /// produce a German interface, not an error page.
    /// </summary>
    public static Strings For(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? new StringsEn()
            : new StringsDe();

    /// <summary>
    /// The text for the current request's language.
    ///
    /// <para>
    /// For the interface <c>Strings</c> is injected — that is the usual thing
    /// there and makes the dependency visible. Not every service can do
    /// that: <c>Tr064Client</c> for instance lives as a singleton, and a
    /// singleton must not hold anything scoped. Hence this route for the
    /// places where an error message comes into being.
    /// </para>
    ///
    /// <para>
    /// This is not a way round injection: the culture hangs off the request's
    /// execution context, which ASP.NET Core sets and which travels across
    /// <c>await</c>. Whoever asks outside a request — a background service —
    /// gets the default, and there that is the right answer too.
    /// </para>
    /// </summary>
    public static Strings Current => For(CultureInfo.CurrentUICulture);

    /// <summary>Like <see cref="For(CultureInfo)"/>, but from a code.</summary>
    public static Strings For(string code) =>
        code.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? new StringsEn()
            : new StringsDe();

    /// <summary>Finds the culture for a code from the URL.</summary>
    public static string? CultureToCode(string code) =>
        Languages.FirstOrDefault(s =>
            s.Code.Equals(code, StringComparison.OrdinalIgnoreCase)).Culture;
}

/// <summary>The German version. It is the original.</summary>
public sealed partial class StringsDe : Strings
{
    public override string Code => "de";
    public override string OwnName => "Deutsch";
}

/// <summary>
/// The English version.
///
/// The tone is translated along with the words, not only the words: the
/// German original speaks tersely and says what to do. Where a literal
/// rendering would sound stiff, what stands here is the sentence an English
/// text would actually write in that place.
/// </summary>
public sealed partial class StringsEn : Strings
{
    public override string Code => "en";
    public override string OwnName => "English";
}
