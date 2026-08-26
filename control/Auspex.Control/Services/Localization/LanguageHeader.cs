using Microsoft.AspNetCore.Localization;

namespace Auspex.Control.Services.Localization;

/// <summary>
/// Reads the language from the <c>X-Auspex-Language</c> header.
///
/// <para>
/// For the interface a cookie decides. The browser extension cannot use
/// that: it has a different origin and identifies itself with a token, not
/// with a session — its calls deliberately go out with
/// <c>credentials: "omit"</c>. Without a second route it would get German
/// error messages back while the dashboard next to it is English.
/// </para>
///
/// <para>
/// Deliberately a header of <em>our own</em> and not <c>Accept-Language</c>.
/// Every browser sends that unasked, and a browser that happens to be set to
/// English should not switch the installation over without anybody wanting
/// it. This header appears only where somebody set it deliberately — the
/// extension, carrying the language it previously read from the dashboard.
/// </para>
/// </summary>
public sealed class LanguageHeaderProvider : RequestCultureProvider
{
    public const string Header = "X-Auspex-Language";

    /// <summary>
    /// The name this header had up to version 0.9.
    ///
    /// <para>
    /// It is still read, and that is not ballast but the difference between
    /// a rename and a break: an extension already sitting in a browser only
    /// knows the old name. Without this line it would speak German again
    /// from the next rollout on, with nothing anywhere saying why.
    /// </para>
    /// </summary>
    public const string HeaderBis09 = "X-Auspex-Sprache";

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext http)
    {
        var wish = http.Request.Headers[Header].ToString();
        if (string.IsNullOrWhiteSpace(wish))
        {
            wish = http.Request.Headers[HeaderBis09].ToString();
        }

        if (string.IsNullOrWhiteSpace(wish))
        {
            return Task.FromResult<ProviderCultureResult?>(null);
        }

        var culture = Strings.CultureToCode(wish.Trim());
        return Task.FromResult<ProviderCultureResult?>(
            culture is null ? null : new ProviderCultureResult(culture, culture));
    }
}
