namespace Auspex.Control.Services.Router;

/// <summary>
/// What a router can do, read from its own description.
///
/// Deliberately not a hand-written catalogue: a Fritz!Box 5690 Pro reports
/// 39 services with 468 actions, and the next firmware reports different
/// ones. Maintaining all of that by hand would be out of date the moment it
/// was finished. Instead the device description and every SCPD file are read
/// on connect — so Auspex covers what the device offers, even on a model
/// that did not exist while it was being written.
/// </summary>
public record RouterCatalog(
    string Model,
    string FriendlyName,
    string? SoftwareVersion,
    IReadOnlyList<RouterServiceInfo> Services,
    IReadOnlyList<string> Incomplete)
{
    public int ActionCount => Services.Sum(s => s.Actions.Count);

    /// <summary>
    /// Whether services were lost during discovery.
    ///
    /// A Fritz!Box throttles when its close to 40 description files are
    /// fetched in quick succession - individual fetches then time out. A
    /// catalogue missing services because of that looks like a complete one:
    /// it shows 28 instead of 39, and nobody notices that Hosts of all things
    /// is missing. So this is carried along and displayed rather than passed
    /// over in silence.
    /// </summary>
    public bool IsComplete => Incomplete.Count == 0;

    public RouterServiceInfo? FindService(string name) =>
        Services.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public record RouterServiceInfo(
    string Name,
    string ServiceType,
    string ControlUrl,
    string ScpdUrl,
    IReadOnlyList<RouterAction> Actions)
{
    /// <summary>
    /// Several instances of the same service differ only in their control
    /// URL — a Fritz!Box has four WLANConfiguration. For display it
    /// therefore needs a name that tells them apart.
    /// </summary>
    public string DisplayName =>
        ControlUrl.TrimEnd('/').Split('/').LastOrDefault() is { Length: > 0 } last
        && !last.Equals(Name, StringComparison.OrdinalIgnoreCase)
            ? $"{Name} ({last})"
            : Name;

    public RouterAction? FindAction(string name) =>
        Actions.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public record RouterAction(
    string Name,
    IReadOnlyList<RouterArgument> Arguments)
{
    public IEnumerable<RouterArgument> In => Arguments.Where(a => a.Direction == "in");
    public IEnumerable<RouterArgument> Out => Arguments.Where(a => a.Direction == "out");

    /// <summary>
    /// Whether the action only reads. TR-064's naming convention is reliable
    /// enough for that — "Get..." reads, everything else can intervene — but
    /// it is a convention and not a promise. So it additionally depends on
    /// whether there are input parameters that set anything at all: an
    /// action with no output always changes something.
    /// </summary>
    public bool IsReadOnly =>
        Name.StartsWith("Get", StringComparison.OrdinalIgnoreCase)
        || Name.StartsWith("X_AVM-DE_Get", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Actions that can cut access to the router itself. Whoever switches
    /// off the wireless network their machine is on, through the dashboard,
    /// can no longer reach the place where they would switch it back.
    /// </summary>
    public bool IsDangerous =>
        !IsReadOnly
        && (Name.Contains("Reboot", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("FactoryReset", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("SetEnable", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("SetIPInterface", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("SetDHCPServerEnable", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("SetSubnetMask", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("SetIPRouter", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("ConfigFile", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("SecurityPort", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One parameter together with everything the SCPD file gives away about it.
/// The interface builds its input fields from these details: a boolean
/// becomes a checkbox, an enumeration a dropdown, a number with bounds a
/// field with bounds. Which is why there is more in here than name and type.
/// </summary>
public record RouterArgument(
    string Name,
    string Direction,
    string StateVariable,
    string DataType,
    IReadOnlyList<string> AllowedValues,
    string? Minimum,
    string? Maximum,
    string? DefaultValue)
{
    public bool IsBoolean => DataType.Equals("boolean", StringComparison.OrdinalIgnoreCase);

    public bool IsNumeric => DataType.StartsWith("ui", StringComparison.OrdinalIgnoreCase)
        || DataType.StartsWith("i", StringComparison.OrdinalIgnoreCase)
        && !DataType.Equals("string", StringComparison.OrdinalIgnoreCase);

    public bool HasChoices => AllowedValues.Count > 0;

    /// <summary>
    /// TR-064 prefixes every parameter with "New". For display that is just
    /// noise — but on the call it has to go back on.
    /// </summary>
    public string Label =>
        Name.StartsWith("New", StringComparison.Ordinal) && Name.Length > 3
            ? Name[3..]
            : Name;
}
