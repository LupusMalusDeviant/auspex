using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Auspex.Control.Services.Extension;

/// <summary>
/// The token the browser extension identifies itself with.
///
/// Why not the dashboard's session cookie: it is on <c>SameSite=Lax</c>, and
/// a request from an extension counts as a foreign context to the browser —
/// the cookie simply would not go along. A token of its own is cleaner
/// anyway: it can be revoked on its own without anybody being thrown out of
/// the dashboard.
///
/// It lives encrypted beside the database, not inside it: the backup takes
/// the database with it, and an access key does not belong in an archive you
/// hand on.
/// </summary>
public class ExtensionTokenStore : IExtensionTokenStore
{
    private readonly IDataProtector _guard;
    private readonly ILogger<ExtensionTokenStore> _log;
    private readonly string _path;
    private readonly Lock _lock = new();

    /// <summary>
    /// What the token is protected under. English since 0.9.0 — but the old
    /// value stays here as well, because it is not a name: it goes into the
    /// key derivation, and a file written under the old one only opens under
    /// the old one.
    /// </summary>
    private const string Purpose = "Auspex.Extension.Token";

    /// <summary>Up to 0.9.0. Read-only; nothing is written under it any more.</summary>
    private const string OldPurpose = "Auspex.Erweiterung.Zeichen";

    private readonly IDataProtector _old;

    private string? _token;
    private DateTimeOffset? _erzeugt;

    public ExtensionTokenStore(
        IConfiguration configuration,
        IDataProtectionProvider guard,
        ILogger<ExtensionTokenStore> log)
    {
        _guard = guard.CreateProtector(Purpose);
        // The purpose string is part of the key derivation, so it is the
        // shape of stored data and not a name - renaming it alone would have
        // made every token on disk unreadable, and a token is shown exactly
        // once. See Load().
        _old = guard.CreateProtector(OldPurpose);
        _log = log;
        _path = configuration["Extension:TokenPath"] ?? "var/extension.json";
        Load();
    }

    public bool Present
    {
        get { lock (_lock) { return _token is not null; } }
    }

    public DateTimeOffset? Created
    {
        get { lock (_lock) { return _erzeugt; } }
    }

    /// <summary>
    /// Checks a presented token.
    ///
    /// The comparison runs in constant time. With an ordinary string
    /// comparison the duration gives away how many characters already match
    /// — over enough attempts a token can be guessed character by character.
    /// </summary>
    public bool Checks(string? vorgelegt)
    {
        if (string.IsNullOrEmpty(vorgelegt))
        {
            return false;
        }

        string? real;
        lock (_lock)
        {
            real = _token;
        }
        if (real is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(vorgelegt),
            System.Text.Encoding.UTF8.GetBytes(real));
    }

    /// <summary>Creates a new token and returns it once.</summary>
    public async Task<string> NewAsync(CancellationToken ct = default)
    {
        // 32 bytes from the operating system's random generator, in a spelling
        // without special characters - it gets copied by hand.
        var fresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var folder = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var content = JsonSerializer.Serialize(new StoredToken(_guard.Protect(fresh), DateTimeOffset.UtcNow));
        var provisional = _path + ".neu";
        await File.WriteAllTextAsync(provisional, content, ct);
        File.Move(provisional, _path, overwrite: true);

        lock (_lock)
        {
            _token = fresh;
            _erzeugt = DateTimeOffset.UtcNow;
        }

        _log.LogInformation("A new token for the browser extension has been issued");
        return fresh;
    }

    /// <summary>Revokes the token — the extension cannot get in afterwards.</summary>
    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
        lock (_lock)
        {
            _token = null;
            _erzeugt = null;
        }
        _log.LogInformation("The browser extension's token has been withdrawn");
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var abgelegt = JsonSerializer.Deserialize<StoredToken>(File.ReadAllText(_path));
            if (abgelegt is null)
            {
                return;
            }
            _token = Unprotect(abgelegt.Protected);
            _erzeugt = abgelegt.Created;
        }
        catch (Exception ex)
        {
            // Usually a changed key ring. Then the token is lost and has to be
            // created again - no reason to hold up startup.
            _log.LogWarning(ex, "The extension token cannot be read - a new one has to be issued");
        }
    }

    /// <summary>
    /// Opens the stored token, under the current purpose or the one from
    /// before 0.9.0.
    ///
    /// <para>
    /// Rewriting it under the new purpose afterwards would be tidier and is
    /// deliberately not done: this runs in the constructor, and a start that
    /// writes is a start that can fail on a read-only volume. The old file
    /// keeps working for as long as it is there; the next
    /// <see cref="NewAsync"/> writes the new one.
    /// </para>
    /// </summary>
    private string Unprotect(string stored)
    {
        try
        {
            return _guard.Unprotect(stored);
        }
        catch (CryptographicException)
        {
            var token = _old.Unprotect(stored);
            _log.LogInformation(
                "The extension token was stored under the purpose used before 0.9.0. "
                + "It keeps working; a newly issued one uses the current purpose.");
            return token;
        }
    }

    private record StoredToken(string Protected, DateTimeOffset Created);
}
