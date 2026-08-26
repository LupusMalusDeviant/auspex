using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Auspex.Control.Services;

/// <summary>
/// Checks the password. The hash is PBKDF2-SHA256 with a random salt;
/// comparison is constant-time, so the response time gives nothing away
/// about the password.
/// </summary>
public sealed class PasswordAuth
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 210_000; // OWASP recommendation for PBKDF2-SHA256

    private readonly AuthOptions _opt;
    private readonly ILogger<PasswordAuth> _log;
    private readonly string _hash;

    /// <summary>Password generated at startup, if none was set.</summary>
    public string? GeneratedPassword { get; }

    public PasswordAuth(IOptions<AuthOptions> options, ILogger<PasswordAuth> log)
    {
        _opt = options.Value;
        _log = log;

        if (!string.IsNullOrWhiteSpace(_opt.PasswordHash))
        {
            _hash = _opt.PasswordHash;
        }
        else if (!string.IsNullOrWhiteSpace(_opt.Password))
        {
            _hash = Hash(_opt.Password);
            _log.LogWarning(
                "The password sits in the configuration in the clear. Better: set Auth:PasswordHash.");
        }
        else
        {
            // Fail towards "closed" rather than "open" — but without locking
            // anybody out: the generated password is in the log.
            GeneratedPassword = GeneratePassword();
            _hash = Hash(GeneratedPassword);
            _log.LogWarning(
                "No password configured. For this start it is: {Password} " +
                "(user {Username}). For good: set Auth:PasswordHash.",
                GeneratedPassword, _opt.Username);
        }
    }

    public bool Verify(string? username, string? password)
    {
        if (string.IsNullOrEmpty(password)) return false;
        if (!string.Equals(username, _opt.Username, StringComparison.Ordinal)) return false;
        return VerifyHash(password, _hash);
    }

    /// <summary>
    /// Produces a hash. The separator is a colon, not a dollar as in the
    /// otherwise usual PHC format: this value lands in .env files and YAML,
    /// and there the dollar sign is a variable. Docker Compose expands it
    /// away silently — 91 characters become 36, and the sign-in fails with no
    /// error message at all.
    ///
    /// Base64 contains neither colon nor dollar, so the separation stays
    /// unambiguous in both spellings.
    /// </summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"pbkdf2-sha256:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(key)}";
    }

    private static bool VerifyHash(string password, string stored)
    {
        // Accept both separators: old hashes in the dollar format stay valid
        // even though new ones are produced with a colon.
        var parts = stored.Contains(':') ? stored.Split(':') : stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
        if (!int.TryParse(parts[1], out var iterations)) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string GeneratePassword()
    {
        // Without easily confused characters: this gets typed out by hand.
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return RandomNumberGenerator.GetString(alphabet, 20);
    }
}
