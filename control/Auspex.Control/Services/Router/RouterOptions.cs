namespace Auspex.Control.Services.Router;

/// <summary>
/// Access to the router. With no account stored the whole router section
/// stays invisible — not greyed out but absent: an interface showing buttons
/// that cannot do anything trains people to skim past error messages.
/// </summary>
public class RouterOptions
{
    public const string SectionName = "Router";

    /// <summary>
    /// The kind of router. Currently only "fritzbox" (TR-064). The setting
    /// is here so a second make can step alongside later without the
    /// configuration having to be rebuilt.
    /// </summary>
    public string Kind { get; set; } = "fritzbox";

    /// <summary>Address of the router, without scheme and port.</summary>
    public string Host { get; set; } = "192.168.1.1";

    /// <summary>
    /// TR-064 in the clear. Only the device description and the open read
    /// actions run over this — everything authenticated goes over TLS.
    /// </summary>
    public int Port { get; set; } = 49000;

    /// <summary>
    /// TR-064 over TLS. Digest authentication does protect the password from
    /// being read, but the rest of the traffic would lie open on the LAN on
    /// the plaintext port — and that carries the device list, wireless keys
    /// and more.
    /// </summary>
    public int TlsPort { get; set; } = 49443;

    /// <summary>
    /// User name of the router account. Empty means: no account stored, and
    /// the router section does not appear.
    /// </summary>
    public string User { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>
    /// A Fritz!Box's certificate is self-issued and issued to a name nobody
    /// on the LAN can verify. By default it is therefore not verified.
    /// Whoever runs their own CA on the network switches this off — then the
    /// normal chain applies.
    /// </summary>
    public bool AcceptSelfSignedCertificate { get; set; } = true;

    /// <summary>
    /// Permit read-only actions. A safe first pass: the catalogue is fully
    /// visible but nothing can be triggered.
    /// </summary>
    public bool ReadOnly { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long the discovered catalogue is valid. It only changes with a
    /// firmware update, so rarely — but it should refresh itself without
    /// anybody having to restart.
    /// </summary>
    public TimeSpan CatalogTtl { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Where credentials entered through the interface are written. Beside
    /// the database, not into it: the backup takes the database with it, and
    /// router credentials do not belong in an archive you hand on.
    /// </summary>
    public string SettingsPath { get; set; } = "var/router.json";

    /// <summary>
    /// Where the MAC-to-name mapping is written. Into the shared folder the
    /// resolver sees too — it needs it to attribute temporary IPv6 addresses
    /// to a device.
    /// </summary>
    public string DeviceNamePath { get; set; } = "var/devices.json";

    /// <summary>
    /// A copy with different credentials.
    ///
    /// Deliberately this way round rather than assembling a new object field
    /// by field on load: one had already been forgotten doing that, and the
    /// fault only showed when a file ended up in the wrong directory.
    /// Whoever adds a field in future has nothing to add here.
    /// </summary>
    public RouterOptions WithAccess(string host, string user, string password, bool readOnly) =>
        (RouterOptions)MemberwiseClone() is var kopie && kopie is RouterOptions r
            ? With(r, host, user, password, readOnly)
            : this;

    private static RouterOptions With(
        RouterOptions r, string host, string user, string password, bool readOnly)
    {
        r.Host = host;
        r.User = user;
        r.Password = password;
        r.ReadOnly = readOnly;
        return r;
    }

    /// <summary>Is an account stored?</summary>
    public bool Configured =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(User)
        && !string.IsNullOrWhiteSpace(Password);
}
