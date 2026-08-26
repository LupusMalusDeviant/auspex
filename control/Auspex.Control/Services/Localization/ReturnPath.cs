namespace Auspex.Control.Services.Localization;

/// <summary>
/// Checks whether a return address points at this installation.
///
/// <para>
/// The language switch is told where to go back to afterwards — otherwise
/// every switch lands you on the overview instead of where you were. But a
/// parameter that steers a redirect is exactly what an <em>open redirect</em>
/// is made of: a link of the form
/// <c>/language/en?back=https://elsewhere.example</c> carries the name of
/// your own installation and leads somewhere else. Whoever sees it in a
/// message checks the beginning and clicks.
/// </para>
///
/// <para>
/// The check therefore lives here and not in the endpoint: a rule that
/// permits a redirect belongs in a place a test can reach.
/// </para>
/// </summary>
public static class ReturnPath
{
    /// <summary>
    /// True when <paramref name="target"/> is a path on this installation.
    ///
    /// <para>
    /// What is allowed is a path beginning with exactly <em>one</em> slash.
    /// The cases that have to fall through all look like local paths:
    /// </para>
    /// <list type="bullet">
    /// <item><c>//elsewhere.example</c> — protocol-relative, leads outside.</item>
    /// <item><c>/\elsewhere.example</c> — the same; browsers read the
    /// backslash in that position like an ordinary slash.</item>
    /// <item><c>https://…</c>, <c>javascript:…</c> — do not begin with a
    /// slash at all.</item>
    /// </list>
    /// </summary>
    public static bool IsLocal(string? destination) =>
        destination is { Length: > 0 }
        && destination[0] == '/'
        && (destination.Length == 1 || (destination[1] != '/' && destination[1] != '\\'));

    /// <summary>
    /// The address a redirect may go to — the requested one when it is
    /// local, otherwise the overview.
    /// </summary>
    public static string Safe(string? wish) =>
        IsLocal(wish) ? wish! : "/";
}
