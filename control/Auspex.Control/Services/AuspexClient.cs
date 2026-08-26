using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Auspex.Control.Services;

/// <summary>
/// Access to the Go data plane's control API.
/// </summary>
public sealed class AuspexClient(HttpClient http, IConfiguration config, ILogger<AuspexClient> log)
    : IAuspexClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public string BaseAddress => http.BaseAddress?.ToString() ?? "(not configured)";

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        var token = config["Auspex:Token"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return request;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, CancellationToken ct)
    {
        try
        {
            using var response = await http.SendAsync(Request(method, path), ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The data plane runs independently — if it is gone, the dashboard
            // should show that and not come down with an exception.
            log.LogWarning(ex, "Auspex is unreachable: {Method} {Path}", method, path);
            return default;
        }
    }

    public Task<AuspexStatus?> GetStatusAsync(CancellationToken ct = default)
        => SendAsync<AuspexStatus>(HttpMethod.Get, "/api/v1/status", ct);

    public async Task<IReadOnlyList<QueryLogEntry>> GetQueryLogAsync(int limit = 100, CancellationToken ct = default)
        => await SendAsync<List<QueryLogEntry>>(HttpMethod.Get, $"/api/v1/querylog?limit={limit}", ct) ?? [];

    /// <summary>
    /// Fetches every entry after the cursor. If <c>Boot</c> changes, the
    /// resolver has restarted and the cursor belongs reset.
    /// </summary>
    public Task<QueryLogBatch?> GetQueryLogStreamAsync(long since, int limit, CancellationToken ct = default)
        => SendAsync<QueryLogBatch>(HttpMethod.Get, $"/api/v1/querylog/stream?since={since}&limit={limit}", ct);

    public Task<Explanation?> ExplainAsync(string domain, string? client = null, CancellationToken ct = default)
    {
        var path = $"/api/v1/explain?domain={Uri.EscapeDataString(domain)}";
        if (!string.IsNullOrWhiteSpace(client))
        {
            path += $"&client={Uri.EscapeDataString(client)}";
        }
        return SendAsync<Explanation>(HttpMethod.Get, path, ct);
    }

    public async Task<bool> ReloadAsync(bool force, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.SendAsync(
                Request(HttpMethod.Post, $"/api/v1/reload?force={(force ? "true" : "false")}"), ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Reload fehlgeschlagen");
            return false;
        }
    }

    public async Task<IReadOnlyList<LearnStats>> GetLearnAsync(CancellationToken ct = default)
        => await SendAsync<List<LearnStats>>(HttpMethod.Get, "/api/v1/learn", ct) ?? [];

    public async Task<IReadOnlyList<LearnEntry>> GetLearnEntriesAsync(string profile, CancellationToken ct = default)
        => await SendAsync<List<LearnEntry>>(HttpMethod.Get, $"/api/v1/learn/{Uri.EscapeDataString(profile)}", ct) ?? [];

    public Task<Allowlist?> GetAllowlistAsync(string profile, string granularity = "domain", CancellationToken ct = default)
        => SendAsync<Allowlist>(HttpMethod.Get,
            $"/api/v1/learn/{Uri.EscapeDataString(profile)}/allowlist?granularity={granularity}", ct);

    /// <summary>Spielt Beobachtungen aus einer Sicherung zurueck.</summary>
    public async Task<int> ImportLearnAsync(string profile, string entriesJson, CancellationToken ct = default)
    {
        try
        {
            var request = Request(HttpMethod.Post, $"/api/v1/learn/{Uri.EscapeDataString(profile)}/import");
            request.Content = new StringContent(entriesJson, System.Text.Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return 0;

            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>(JsonOptions, ct);
            return body is not null && body.TryGetValue("imported", out var n) ? n.GetInt32() : 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "The learned state could not be restored");
            return 0;
        }
    }

    public async Task<bool> ResetLearnAsync(string profile, CancellationToken ct = default)
        => await PostAsync($"/api/v1/learn/{Uri.EscapeDataString(profile)}/reset", ct);

    public async Task<bool> ForgetAsync(string profile, string name, CancellationToken ct = default)
        => await PostAsync(
            $"/api/v1/learn/{Uri.EscapeDataString(profile)}/forget?name={Uri.EscapeDataString(name)}", ct);

    private async Task<bool> PostAsync(string path, CancellationToken ct)
    {
        try
        {
            using var response = await http.SendAsync(Request(HttpMethod.Post, path), ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "POST {Path} fehlgeschlagen", path);
            return false;
        }
    }

    /// <summary>
    /// Asks the resolver which device is behind an address.
    ///
    /// It keeps the neighbour table and the device list anyway; building the
    /// same mapping here a second time would mean maintaining two truths
    /// that can drift apart.
    /// </summary>
    /// <summary>
    /// Throws away the cached answers for a name.
    ///
    /// Needed after every rule change: whoever allows a domain and reloads
    /// the page would otherwise keep getting the cached NXDOMAIN. The
    /// exception would be set and still only take effect once the negative
    /// TTL expired — indistinguishable from "it does not work" for whoever
    /// just clicked.
    /// </summary>
    public async Task<bool> ForgetNameAsync(string name, CancellationToken ct = default)
    {
        try
        {
            using var reply = await http.PostAsync(
                $"/api/v1/cache/forget?name={Uri.EscapeDataString(name)}", null, ct);
            return reply.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // Not bad enough to make the allowance fail: the rule stands, it
            // just takes effect a few minutes later.
            log.LogDebug(ex, "The cache for {Name} was not cleared", name);
            return false;
        }
    }

    public Task<WhoEntry?> WhoAsync(string ip, CancellationToken ct = default)
        => SendAsync<WhoEntry>(HttpMethod.Get, $"/api/v1/who?ip={Uri.EscapeDataString(ip)}", ct);

    public async Task<IReadOnlyList<ManagedClient>> GetClientsAsync(CancellationToken ct = default)
        => await SendAsync<List<ManagedClient>>(HttpMethod.Get, "/api/v1/clients", ct) ?? [];

    public async Task<IReadOnlyList<ServiceEntry>> GetServicesAsync(CancellationToken ct = default)
        => await SendAsync<List<ServiceEntry>>(HttpMethod.Get, "/api/v1/services", ct) ?? [];

    /// <summary>The SafeSearch catalogue, so the interface can offer it.</summary>
    public async Task<IReadOnlyList<SafeSearchProvider>> GetSafeSearchAsync(CancellationToken ct = default)
        => await SendAsync<List<SafeSearchProvider>>(HttpMethod.Get, "/api/v1/safesearch", ct) ?? [];

    /// <summary>Creates a profile or replaces it. Error text comes back.</summary>
    public async Task<string?> PutClientAsync(ManagedClient client, CancellationToken ct = default)
    {
        try
        {
            var request = Request(HttpMethod.Post, "/api/v1/clients");
            request.Content = JsonContent.Create(client, options: JsonOptions);
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)response.StatusCode}" : body;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "The profile could not be saved");
            return "The resolver is unreachable";
        }
    }

    public async Task<bool> RemoveClientAsync(string name, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.SendAsync(
                Request(HttpMethod.Delete, $"/api/v1/clients/{Uri.EscapeDataString(name)}"), ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "The profile could not be removed");
            return false;
        }
    }

    public Task<ListsResponse?> GetListsAsync(CancellationToken ct = default)
        => SendAsync<ListsResponse>(HttpMethod.Get, "/api/v1/lists", ct);

    public async Task<bool> AddListAsync(ManagedList list, CancellationToken ct = default)
    {
        try
        {
            var request = Request(HttpMethod.Post, "/api/v1/lists");
            request.Content = JsonContent.Create(list, options: JsonOptions);
            using var response = await http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "The list could not be added");
            return false;
        }
    }

    public Task<bool> SetListEnabledAsync(string name, bool enabled, CancellationToken ct = default)
        => PostAsync($"/api/v1/lists/{Uri.EscapeDataString(name)}/enabled?value={(enabled ? "true" : "false")}", ct);

    public async Task<bool> RemoveListAsync(string name, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.SendAsync(
                Request(HttpMethod.Delete, $"/api/v1/lists/{Uri.EscapeDataString(name)}"), ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "The list could not be removed");
            return false;
        }
    }

    /// <summary>Has the resolver resolve names in advance.</summary>
    public async Task<bool> WarmCacheAsync(IReadOnlyList<string> names, CancellationToken ct = default)
    {
        if (names.Count == 0) return true;
        try
        {
            var request = Request(HttpMethod.Post, "/api/v1/cache/warm");
            request.Content = JsonContent.Create(new { names }, options: JsonOptions);
            using var response = await http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Warming the cache failed");
            return false;
        }
    }

    public async Task<bool> PurgeCacheAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await http.SendAsync(Request(HttpMethod.Post, "/api/v1/cache/purge"), ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Cache-Purge fehlgeschlagen");
            return false;
        }
    }
}
