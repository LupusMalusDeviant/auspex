namespace Auspex.Control.Services;

/// <summary>
/// Access to the dashboard. The dashboard can change filter lists and create
/// exceptions — running it unprotected once it listens on anything other
/// than loopback would be plain negligent.
/// </summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// The default is on. Switch it off only when something in front already
    /// authenticates — a reverse proxy with forward auth, say.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public string Username { get; set; } = "admin";

    /// <summary>
    /// Preferred: the hash from <c>Auspex.Control --hash-password</c>.
    /// </summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>
    /// A stopgap for simple setups: plaintext, hashed at startup. That puts
    /// it in the configuration file — so only when the file itself is
    /// protected.
    /// </summary>
    public string Password { get; set; } = "";

    /// <summary>How long a sign-in lasts.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(7);
}
