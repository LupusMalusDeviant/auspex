using Auspex.Control.Services.Geo;
using Auspex.Control.Services.Router;

namespace Auspex.Control.Services;

// The seams facing outwards.
//
// Here stand the interfaces of the services that leave the process: the
// resolver over HTTP, the router over TR-064 and its web interface, the rule
// files in the file system, the origin databases on disk, the stored
// settings.
//
// DELIBERATELY NOT FOR EVERYTHING. One interface per class is not
// architecture but a second file per class: it doubles every change and
// obscures which boundary is really one. What stands here has one of two
// reasons:
//
//   * It talks to something that is not there in a test - a router, a
//     resolver, a database of 717,000 address ranges.
//   * It is a decision you should be able to replace without touching the
//     callers.
//
// Pure computation does not stand here. QueryGrouping, Detectors and the
// formatting have no outside world; an interface in front of them would be a
// dummy for something that depends only on its inputs anyway - and a test
// that fakes it then tests the fake.

/// <summary>The resolver, over its HTTP API.</summary>
/// <summary>
/// Reading and writing device profiles — the slice of the resolver API that
/// the quarantine needs and nothing more.
///
/// Split out so a caller that only moves profiles around does not have to
/// depend on the whole control API, and so a test double for it is three
/// lines rather than thirty. <see cref="IAuspexClient"/> carries it, so
/// nothing at the wiring end changes.
/// </summary>
public interface IClientProfiles
{
    Task<IReadOnlyList<ManagedClient>> GetClientsAsync(CancellationToken ct = default);
    Task<string?> PutClientAsync(ManagedClient client, CancellationToken ct = default);
}

public interface IAuspexClient : IClientProfiles
{
    string BaseAddress { get; }

    Task<AuspexStatus?> GetStatusAsync(CancellationToken ct = default);
    Task<IReadOnlyList<QueryLogEntry>> GetQueryLogAsync(int limit = 100, CancellationToken ct = default);
    Task<QueryLogBatch?> GetQueryLogStreamAsync(long since, int limit, CancellationToken ct = default);
    Task<Explanation?> ExplainAsync(string domain, string? client = null, CancellationToken ct = default);
    Task<bool> ReloadAsync(bool force, CancellationToken ct = default);

    Task<IReadOnlyList<LearnStats>> GetLearnAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LearnEntry>> GetLearnEntriesAsync(string profile, CancellationToken ct = default);
    Task<Allowlist?> GetAllowlistAsync(string profile, string granularity = "domain", CancellationToken ct = default);
    Task<int> ImportLearnAsync(string profile, string entriesJson, CancellationToken ct = default);
    Task<bool> ResetLearnAsync(string profile, CancellationToken ct = default);
    Task<bool> ForgetAsync(string profile, string name, CancellationToken ct = default);
    Task<bool> ForgetNameAsync(string name, CancellationToken ct = default);

    Task<WhoEntry?> WhoAsync(string ip, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceEntry>> GetServicesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SafeSearchProvider>> GetSafeSearchAsync(CancellationToken ct = default);
    Task<bool> RemoveClientAsync(string name, CancellationToken ct = default);

    Task<ListsResponse?> GetListsAsync(CancellationToken ct = default);
    Task<bool> AddListAsync(ManagedList list, CancellationToken ct = default);
    Task<bool> SetListEnabledAsync(string name, bool enabled, CancellationToken ct = default);
    Task<bool> RemoveListAsync(string name, CancellationToken ct = default);

    Task<bool> WarmCacheAsync(IReadOnlyList<string> names, CancellationToken ct = default);
    Task<bool> PurgeCacheAsync(CancellationToken ct = default);
}

/// <summary>The router — devices, wireless, port mappings, events.</summary>
public interface IRouterAdmin
{
    bool Configured { get; }
    bool ReadOnly { get; }

    Task<RouterCatalog> GetCatalogAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RouterDevice>> GetDevicesAsync(CancellationToken ct = default);
    Task<string?> GetHostChangeCounterAsync(CancellationToken ct = default);
    Task<RouterResult> SetInternetAccessAsync(string ipv4, bool allowed, CancellationToken ct = default);
    Task<bool?> GetInternetAccessAsync(string ipv4, bool afterChange = false, CancellationToken ct = default);
    Task<RouterList<RouterLogEntry>> GetLogAsync(CancellationToken ct = default);
    Task<RouterList<RouterWlan>> GetWlansAsync(CancellationToken ct = default);
    Task<RouterResult> SetWlanAsync(string controlUrl, bool on, CancellationToken ct = default);
    Task<RouterList<RouterPortMapping>> GetPortMappingsAsync(CancellationToken ct = default);
    Task<RouterResult> DeletePortMappingAsync(RouterPortMapping m, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetInfoAsync(CancellationToken ct = default);
    Task<RouterResult> InvokeAsync(string serviceName, string controlUrl, string actionName,
        IReadOnlyDictionary<string, string?> values, CancellationToken ct = default);
}

/// <summary>
/// Writes rules for the resolver.
///
/// <para>
/// The seam here is not the file system but the effect: after a call the
/// resolver blocks or allows something other than before.
/// </para>
/// </summary>
public interface IRuleWriter
{
    bool Enabled { get; }
    string PathFor(RuleTarget target);
    Task<RuleWriteResult> AddAsync(string rule, string reason,
        RuleTarget target = RuleTarget.Allow, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ReadAsync(
        RuleTarget target = RuleTarget.Allow, CancellationToken ct = default);
    Task EnsureExistsAsync(CancellationToken ct = default);
}

/// <summary>Address ranges to operators — local, never over an API.</summary>
public interface INetworkRanges
{
    string DbPath { get; }
    void Prepare();
    (DateTime? Fetched, long Rows) State();
    NetworkInfo? Lookup(UInt128 address);
    Dictionary<UInt128, NetworkInfo> Lookup(IEnumerable<UInt128> addresses);
    long Import(IEnumerable<(UInt128 From, UInt128 To, int Asn, string? Country, string? Operator)> lines);
}

/// <summary>Die serverseitig gehaltene Darstellung.</summary>
public interface IAppearanceStore
{
    Appearance Current { get; }
    Appearance Set(Appearance wish);
    void SetTimeZone(string? name);
    void SetLanguage(string code);
}

/// <summary>Das hinterlegte Router-Konto.</summary>
public interface IRouterSettingsStore
{
    int Version { get; }
    RouterOptions Current { get; }

    /// <summary>
    /// Whether the credentials come from the environment rather than the
    /// interface. Then nobody may change them through the dashboard — whoever
    /// set them did so in a place the interface does not own.
    /// </summary>
    bool FromEnvironment { get; }

    Task SaveAsync(string host, string user, string password, bool readOnly,
        CancellationToken ct = default);
    void Clear();
}

/// <summary>The token the extension and the sensor identify themselves with.</summary>
public interface IExtensionTokenStore
{
    bool Present { get; }
    DateTimeOffset? Created { get; }
    bool Checks(string? presented);
    Task<string> NewAsync(CancellationToken ct = default);
    void Delete();
}
