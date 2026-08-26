using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Auspex.Control.Services.Router;

/// <summary>
/// Holds the router credentials and makes them changeable at runtime.
///
/// They used to live in the environment only, set when the container starts.
/// That has a catch you only notice in use: whoever has not stored an
/// account yet does not see the router section — and therefore does not see
/// the instructions for storing one either. The only information about where
/// it belongs stood behind the door it unlocks.
///
/// Hence an additional store beside the database. The environment keeps
/// precedence: whoever describes their installation through compose.yml
/// should not find a click in the interface silently overwriting it.
/// </summary>
public class RouterSettingsStore : IRouterSettingsStore
{
    private readonly RouterOptions _fromConfiguration;
    private readonly IDataProtector _guard;
    private readonly ILogger<RouterSettingsStore> _log;
    private readonly string _path;
    private readonly Lock _lock = new();

    private RouterOptions _current;

    public RouterSettingsStore(
        IOptions<RouterOptions> optionen,
        IDataProtectionProvider guard,
        ILogger<RouterSettingsStore> log)
    {
        _fromConfiguration = optionen.Value;
        _guard = guard.CreateProtector("Auspex.Router.Zugangsdaten");
        _log = log;
        _path = _fromConfiguration.SettingsPath;
        _current = Load();
    }

    /// <summary>
    /// Counts every change. Whoever caches things derived from it — an HTTP
    /// client with stored credentials, say — can tell from this that they
    /// have to be rebuilt.
    /// </summary>
    public int Version { get; private set; }

    public RouterOptions Current
    {
        get { lock (_lock) { return _current; } }
    }

    /// <summary>
    /// Whether the credentials come from the environment. Then the interface
    /// keeps its hands off them and says why.
    /// </summary>
    public bool FromEnvironment =>
        !string.IsNullOrWhiteSpace(_fromConfiguration.User)
        && !string.IsNullOrWhiteSpace(_fromConfiguration.Password);

    private RouterOptions Load()
    {
        if (FromEnvironment || !File.Exists(_path))
        {
            return _fromConfiguration;
        }

        try
        {
            var abgelegt = JsonSerializer.Deserialize<StoredCredentials>(
                File.ReadAllText(_path));
            if (abgelegt is null)
            {
                return _fromConfiguration;
            }

            return _fromConfiguration.WithAccess(
                abgelegt.Host, abgelegt.User,
                _guard.Unprotect(abgelegt.PasswordProtected), abgelegt.ReadOnly);
        }
        catch (Exception ex)
        {
            // An unreadable stored state must not hold the application up.
            // Usually the key ring has changed - then the password is lost
            // anyway and has to be entered again.
            _log.LogWarning(ex,
                "The router credentials under {Path} cannot be read - the configuration applies", _path);
            return _fromConfiguration;
        }
    }

    public async Task SaveAsync(
        string host, string user, string password, bool readOnly, CancellationToken ct = default)
    {
        if (FromEnvironment)
        {
            throw new InvalidOperationException(
                "The credentials come from the environment and are not overwritten here.");
        }

        var abgelegt = new StoredCredentials(
            host.Trim(), user.Trim(), _guard.Protect(password), readOnly);

        var folder = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // Write beside it first, then rename: an aborted write should not
        // leave half a file behind that is no longer readable on the next
        // start.
        var provisional = _path + ".neu";
        await File.WriteAllTextAsync(provisional, JsonSerializer.Serialize(abgelegt), ct);
        File.Move(provisional, _path, overwrite: true);

        lock (_lock)
        {
            _current = Load();
            Version++;
        }

        _log.LogInformation("Router credentials for {Host} stored", host);
    }

    /// <summary>Removes the credentials — the router section disappears afterwards.</summary>
    public void Clear()
    {
        if (FromEnvironment)
        {
            throw new InvalidOperationException(
                "The credentials come from the environment and are not removed here.");
        }

        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        lock (_lock)
        {
            _current = _fromConfiguration;
            Version++;
        }

        _log.LogInformation("Router-Zugangsdaten entfernt");
    }

    /// <summary>
    /// What lies on disk. The password encrypted with the application's key
    /// ring — a file in the data directory is no place for a password in the
    /// clear, even when only the service can reach it.
    /// </summary>
    private record StoredCredentials(
        string Host, string User, string PasswordProtected, bool ReadOnly);
}
