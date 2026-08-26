namespace Auspex.Control.Services.Router;
using Auspex.Control.Services.Localization;

/// <summary>
/// The convenient side of the router connection.
///
/// The catalogue can do everything, but 468 actions are not a way to operate
/// anything. Here are the few operations you actually need day to day, with
/// names that say something and without SOAP vocabulary. Everything else
/// stays reachable through the catalogue — this facade takes nothing away, it
/// merely lays a short road beside the long one.
/// </summary>
public class RouterAdmin(Tr064Client client, ILogger<RouterAdmin> log) : IRouterAdmin
{
    private const string HostsService = "Hosts";
    private const string FilterService = "X_AVM-DE_HostFilter";
    private const string InfoService = "DeviceInfo";

    public bool Configured => client.Configured;
    public bool ReadOnly => client.ReadOnly;

    public Task<RouterCatalog> GetCatalogAsync(CancellationToken ct = default) =>
        client.GetCatalogAsync(ct);

    /// <summary>
    /// Every device the router knows about, with the MAC as a stable id.
    ///
    /// Works without an account: a Fritz!Box hands this list out openly. So
    /// the most valuable part - an inventory with a stable id instead of
    /// changing IPs - is usable before any credentials are stored.
    /// </summary>
    public async Task<IReadOnlyList<RouterDevice>> GetDevicesAsync(CancellationToken ct = default)
    {
        var catalogue = await client.GetCatalogAsync(ct);
        var service = catalogue.FindService(HostsService);
        var counts = service?.FindAction("GetHostNumberOfEntries");
        var read = service?.FindAction("GetGenericHostEntry");
        if (service is null || counts is null || read is null)
        {
            log.LogWarning("The router offers no hosts service - the device list is not available");
            return [];
        }

        var count = await client.InvokeAsync(service, counts, EmptyArgs, ct);
        if (!count.Ok
            || !int.TryParse(count.Values.GetValueOrDefault("NewHostNumberOfEntries"), out var n))
        {
            return [];
        }

        var devices = new List<RouterDevice>(n);
        for (var i = 0; i < n; i++)
        {
            var e = await client.InvokeAsync(
                service, read, new Dictionary<string, string?> { ["NewIndex"] = i.ToString() }, ct);
            if (!e.Ok)
            {
                // An index running into nothing does not end the list: the count
                // can change between two calls.
                continue;
            }

            var v = e.Values;
            var mac = v.GetValueOrDefault("NewMACAddress", "");
            if (mac.Length == 0)
            {
                continue;
            }

            devices.Add(new RouterDevice(
                Mac: mac,
                Ip: v.GetValueOrDefault("NewIPAddress", ""),
                Name: v.GetValueOrDefault("NewHostName", ""),
                Online: v.GetValueOrDefault("NewActive") == "1",
                Interface: v.GetValueOrDefault("NewInterfaceType", ""),
                AddressSource: v.GetValueOrDefault("NewAddressSource", "")));
        }

        return devices;
    }

    /// <summary>
    /// The device list's change counter, or null if the router does not keep
    /// one.
    ///
    /// The list itself costs one SOAP call per device — with thirty devices
    /// on a five-minute cadence that would be close to nine thousand calls a
    /// day, and the Fritz!Box throttles rapid polling anyway. The counter
    /// answers the same question with one call, as long as nothing has
    /// happened.
    /// </summary>
    public async Task<string?> GetHostChangeCounterAsync(CancellationToken ct = default)
    {
        var catalogue = await client.GetCatalogAsync(ct);
        var service = catalogue.FindService(HostsService);
        var action = service?.FindAction("X_AVM-DE_GetChangeCounter");
        if (service is null || action is null)
        {
            return null;
        }

        var reply = await client.InvokeAsync(service, action, EmptyArgs, ct);
        if (!reply.Ok)
        {
            return null;
        }

        // The name of the return value differs between firmware versions;
        // all that matters is that the value changes.
        return reply.Values.Values.FirstOrDefault();
    }

    /// <summary>
    /// Grant or cut a device's internet access.
    ///
    /// This is the point where Auspex turns from an observer into an actor:
    /// DNS can refuse to resolve a name, but a device stays on the network.
    /// The router can actually lock it out. Requires an account.
    /// </summary>
    public async Task<RouterResult> SetInternetAccessAsync(
        string ipv4, bool allowed, CancellationToken ct = default)
    {
        var catalogue = await client.GetCatalogAsync(ct);
        var service = catalogue.FindService(FilterService);
        var action = service?.FindAction("DisallowWANAccessByIP");
        if (service is null || action is null)
        {
            return RouterResult.Failed(Strings.Current.ServiceMissing(FilterService));
        }

        return await client.InvokeAsync(service, action, new Dictionary<string, string?>
        {
            ["NewIPv4Address"] = ipv4,
            // The action is called "Disallow": true blocks. This is exactly
            // where you get it wrong, so the facade inverts it once and the
            // caller thinks in terms of "allowed".
            ["NewDisallow"] = allowed ? "0" : "1",
        }, ct);
    }

    /// <summary>
    /// Whether a device is currently allowed onto the internet.
    ///
    /// Set <paramref name="afterChange"/> when it was switched immediately
    /// beforehand: the Fritz!Box then answers "unknown" for a few seconds
    /// before committing to the new state. Without waiting, the interface
    /// reads back a "don't know" right after the click and looks as though
    /// the switch did not work.
    /// </summary>
    public async Task<bool?> GetInternetAccessAsync(
        string ipv4, bool afterChange = false, CancellationToken ct = default)
    {
        var catalogue = await client.GetCatalogAsync(ct);
        var service = catalogue.FindService(FilterService);
        var action = service?.FindAction("GetWANAccessByIP");
        if (service is null || action is null)
        {
            return null;
        }

        var versuche = afterChange ? 5 : 1;
        for (var i = 0; i < versuche; i++)
        {
            if (i > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }

            var e = await client.InvokeAsync(service, action,
                new Dictionary<string, string?> { ["NewIPv4Address"] = ipv4 }, ct);
            if (!e.Ok)
            {
                return null;
            }

            // "granted" means allowed, "denied" blocked, anything else (above
            // all "unknown") means: not decided yet.
            switch (e.Values.GetValueOrDefault("NewWANAccess"))
            {
                case "granted": return true;
                case "denied": return false;
            }
        }

        return null;
    }

    /// <summary>The router's event log, split into entries.</summary>
    public async Task<RouterList<RouterLogEntry>> GetLogAsync(CancellationToken ct = default)
    {
        var catalogue = await client.GetCatalogAsync(ct);
        var service = catalogue.FindService(InfoService);
        var action = service?.FindAction("GetDeviceLog") ?? service?.FindAction("GetInfo");
        if (service is null || action is null)
        {
            return RouterList<RouterLogEntry>.Unknown(Strings.Current.ServiceEventLog);
        }

        var e = await client.InvokeAsync(service, action, EmptyArgs, ct);
        if (!e.Ok)
        {
            return RouterList<RouterLogEntry>.Failure(e.Error ?? Strings.Current.RouterNotAnswering);
        }

        // Depending on firmware the log sits under DeviceLog in GetInfo or has
        // an action of its own. Both routes end in the same field.
        var raw = e.Values.GetValueOrDefault("NewDeviceLog")
            ?? e.Values.FirstOrDefault(x => x.Key.Contains("Log", StringComparison.OrdinalIgnoreCase)).Value;
        return RouterList<RouterLogEntry>.From(RouterLog.Parse(raw));
    }

    /// <summary>
    /// The router's wireless networks. A Fritz!Box runs four instances of the
    /// service: 2.4 GHz, 5 GHz, further bands and the guest network. Which is
    /// which is written nowhere - it can only be told from what they report
    /// about themselves. So it is read rather than guessed.
    /// </summary>
    public async Task<RouterList<RouterWlan>> GetWlansAsync(CancellationToken ct = default)
    {
        var catalogue = await client.GetCatalogAsync(ct);
        var networks = new List<RouterWlan>();
        // If ALL instances fail, it was not because there are no networks -
        // then the question did not get through at all.
        string? error = null;
        var versucht = 0;

        foreach (var service in catalogue.Services.Where(d =>
            d.Name.Equals("WLANConfiguration", StringComparison.OrdinalIgnoreCase)))
        {
            var info = service.FindAction("GetInfo");
            if (info is null)
            {
                continue;
            }

            versucht++;
            var e = await client.InvokeAsync(service, info, EmptyArgs, ct);
            if (!e.Ok)
            {
                error ??= e.Error;
                continue;
            }

            var v = e.Values;
            var ssid = v.GetValueOrDefault("NewSSID", "");

            // The guest network does not report itself as one. It can be told
            // from the access point type, failing that from the name.
            var gast = v.GetValueOrDefault("NewX_AVM-DE_APType") == "guest"
                || ssid.Contains("gast", StringComparison.OrdinalIgnoreCase)
                || ssid.Contains("guest", StringComparison.OrdinalIgnoreCase);

            networks.Add(new RouterWlan(
                ControlUrl: service.ControlUrl,
                Ssid: ssid,
                Enabled: v.GetValueOrDefault("NewEnable") == "1",
                Band: v.GetValueOrDefault("NewX_AVM-DE_FrequencyBand", ""),
                Channel: v.GetValueOrDefault("NewChannel", ""),
                Security: v.GetValueOrDefault("NewBeaconType", ""),
                IsGuest: gast));
        }

        // Not a single service answered even though there are some: then the
        // list is not empty but unknown.
        if (networks.Count == 0 && versucht > 0 && error is not null)
        {
            return RouterList<RouterWlan>.Failure(error);
        }

        return RouterList<RouterWlan>.From(networks);
    }

    /// <summary>Switch a wireless network on or off.</summary>
    public async Task<RouterResult> SetWlanAsync(
        string controlUrl, bool on, CancellationToken ct = default)
    {
        var catalogue = await client.GetCatalogAsync(ct);
        var service = catalogue.Services.FirstOrDefault(d => d.ControlUrl == controlUrl);
        var action = service?.FindAction("SetEnable");
        if (service is null || action is null)
        {
            return RouterResult.Failed(Strings.Current.WlanNotSwitchable);
        }

        return await client.InvokeAsync(service, action,
            new Dictionary<string, string?> { ["NewEnable"] = on ? "1" : "0" }, ct);
    }

    /// <summary>
    /// The port mappings. TR-064 only hands them out individually by index;
    /// the count sits in an action of its own.
    /// </summary>
    public async Task<RouterList<RouterPortMapping>> GetPortMappingsAsync(
        CancellationToken ct = default)
    {
        var catalogue = await client.GetCatalogAsync(ct);
        var service = catalogue.FindService("WANIPConnection")
            ?? catalogue.FindService("WANPPPConnection");
        var counts = service?.FindAction("GetPortMappingNumberOfEntries");
        var read = service?.FindAction("GetGenericPortMappingEntry");
        if (service is null || counts is null || read is null)
        {
            return RouterList<RouterPortMapping>.Unknown(Strings.Current.ServicePortMappings);
        }

        var count = await client.InvokeAsync(service, counts, EmptyArgs, ct);
        if (!count.Ok)
        {
            return RouterList<RouterPortMapping>.Failure(
                count.Error ?? Strings.Current.RouterNotAnswering);
        }

        if (!int.TryParse(count.Values.GetValueOrDefault("NewPortMappingNumberOfEntries"), out var n))
        {
            return RouterList<RouterPortMapping>.Failure(Strings.Current.NoCount);
        }

        var list = new List<RouterPortMapping>(n);
        for (var i = 0; i < n; i++)
        {
            var e = await client.InvokeAsync(service, read,
                new Dictionary<string, string?> { ["NewPortMappingIndex"] = i.ToString() }, ct);
            if (!e.Ok)
            {
                continue;
            }

            var v = e.Values;
            list.Add(new RouterPortMapping(
                Description: v.GetValueOrDefault("NewPortMappingDescription", ""),
                Protocol: v.GetValueOrDefault("NewProtocol", ""),
                ExternalPort: v.GetValueOrDefault("NewExternalPort", ""),
                InternalPort: v.GetValueOrDefault("NewInternalPort", ""),
                InternalClient: v.GetValueOrDefault("NewInternalClient", ""),
                Enabled: v.GetValueOrDefault("NewEnabled") == "1",
                RemoteHost: v.GetValueOrDefault("NewRemoteHost", "")));
        }

        return RouterList<RouterPortMapping>.From(list);
    }

    /// <summary>Remove a port mapping.</summary>
    public async Task<RouterResult> DeletePortMappingAsync(
        RouterPortMapping m, CancellationToken ct = default)
    {
        var catalogue = await client.GetCatalogAsync(ct);
        var service = catalogue.FindService("WANIPConnection")
            ?? catalogue.FindService("WANPPPConnection");
        var action = service?.FindAction("DeletePortMapping");
        if (service is null || action is null)
        {
            return RouterResult.Failed(Strings.Current.MappingsNotChangeable);
        }

        return await client.InvokeAsync(service, action, new Dictionary<string, string?>
        {
            ["NewRemoteHost"] = m.RemoteHost,
            ["NewExternalPort"] = m.ExternalPort,
            ["NewProtocol"] = m.Protocol,
        }, ct);
    }

    /// <summary>Basic router details for the overview.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetInfoAsync(CancellationToken ct = default)
    {
        var catalogue = await client.GetCatalogAsync(ct);
        var service = catalogue.FindService(InfoService);
        var action = service?.FindAction("GetInfo");
        if (service is null || action is null)
        {
            return new Dictionary<string, string>();
        }

        var e = await client.InvokeAsync(service, action, EmptyArgs, ct);
        return e.Ok ? e.Values : new Dictionary<string, string>();
    }

    /// <summary>
    /// Any action from the catalogue. This is the route for everything that
    /// has no method of its own up here - that is, for by far the largest
    /// part of what the device can do.
    /// </summary>
    public async Task<RouterResult> InvokeAsync(
        string serviceName,
        string controlUrl,
        string actionName,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken ct = default)
    {
        var catalogue = await client.GetCatalogAsync(ct);
        var service = catalogue.Services.FirstOrDefault(s =>
            s.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase)
            && s.ControlUrl == controlUrl);
        var action = service?.FindAction(actionName);
        if (service is null || action is null)
        {
            return RouterResult.Failed(Strings.Current.ActionDoesNotExist(serviceName, actionName));
        }

        if (!action.IsReadOnly)
        {
            // Changing calls leave a trace. Whoever asks later why the wireless
            // is off should find it here.
            log.LogInformation(
                "Router action carried out: {Service}#{Action}", service.DisplayName, action.Name);
        }

        return await client.InvokeAsync(service, action, values, ct);
    }

    private static readonly Dictionary<string, string?> EmptyArgs = [];
}

public record RouterDevice(
    string Mac,
    string Ip,
    string Name,
    bool Online,
    string Interface,
    string AddressSource)
{
    /// <summary>
    /// Whether the MAC is randomly assigned (bit 1 of the first byte). Phones
    /// roll a private address per wireless network. It is stable as long as
    /// the device knows the network - but after "forget network" the same
    /// device turns up as a new one. Whoever builds an allowance on the MAC
    /// has to know that, or their own phone ends up in the waiting room.
    /// </summary>
    public bool HasRandomMac =>
        Mac.Length >= 2
        && int.TryParse(Mac[..2], System.Globalization.NumberStyles.HexNumber, null, out var b)
        && (b & 0b10) != 0;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Mac : Name;
}
