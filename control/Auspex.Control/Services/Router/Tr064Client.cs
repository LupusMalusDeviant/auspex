using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace Auspex.Control.Services.Router;
using Auspex.Control.Services.Localization;

/// <summary>
/// Speaks TR-064 with the router. Two routes, deliberately separate:
///
/// The device description and part of the read actions are open — a
/// Fritz!Box hands its device list out without a sign-in. Everything else
/// answers 401 and demands an account. For the authenticated part it goes
/// over TLS: digest authentication protects the password but not the
/// content, and that carries wireless keys and an inventory of the network.
/// </summary>
public class Tr064Client : IDisposable
{
    private readonly IRouterSettingsStore _store;
    private readonly ILogger<Tr064Client> _log;
    private readonly HttpClient _open;
    private readonly SemaphoreSlim _catalogueLock = new(1, 1);
    private readonly Lock _clientLock = new();

    private HttpClient? _signedInCache;
    private int _clientVersion = -1;
    private RouterCatalog? _catalogue;
    private DateTimeOffset _catalogUntil;
    private int _catalogVersion = -1;

    public Tr064Client(IRouterSettingsStore store, ILogger<Tr064Client> log)
    {
        _store = store;
        _log = log;
        _open = new HttpClient { Timeout = store.Current.Timeout };
    }

    private RouterOptions _opt => _store.Current;

    /// <summary>
    /// The authenticated client, built from the credentials currently in
    /// force. If they are changed through the interface, the store counts its
    /// version up and the client is rebuilt - otherwise Auspex would keep
    /// speaking with the old password until the next restart.
    /// </summary>
    private HttpClient SignedIn
    {
        get
        {
            lock (_clientLock)
            {
                if (_signedInCache is not null && _clientVersion == _store.Version)
                {
                    return _signedInCache;
                }

                var opt = _opt;
                var handler = new HttpClientHandler();
                if (opt.Configured)
                {
                    // .NET handles digest itself: the first call gets a 401 with a
                    // nonce, the second goes out authenticated. So the content
                    // has to be replayable - StringContent is.
                    var access = new CredentialCache();
                    access.Add(new Uri($"https://{opt.Host}:{opt.TlsPort}"), "Digest",
                        new NetworkCredential(opt.User, opt.Password));
                    handler.Credentials = access;
                    handler.PreAuthenticate = true;
                }
                if (opt.AcceptSelfSignedCertificate)
                {
                    handler.ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }

                _signedInCache?.Dispose();
                _signedInCache = new HttpClient(handler) { Timeout = opt.Timeout };
                _clientVersion = _store.Version;
                return _signedInCache;
            }
        }
    }

    private Uri PlainBasis => new($"http://{_opt.Host}:{_opt.Port}");
    private Uri TlsBasis => new($"https://{_opt.Host}:{_opt.TlsPort}");

    public bool Configured => _opt.Configured;
    public bool ReadOnly => _opt.ReadOnly;

    /// <summary>
    /// Reads the catalogue from the device and holds it until its validity
    /// expires. The description itself is open — so the catalogue stands even
    /// without an account, which makes it a usable preview.
    /// </summary>
    public async Task<RouterCatalog> GetCatalogAsync(CancellationToken ct = default)
    {
        if (Valid())
        {
            return _catalogue!;
        }

        await _catalogueLock.WaitAsync(ct);
        try
        {
            if (Valid())
            {
                return _catalogue!;
            }

            var catalogue = await EntdeckeAsync(ct);
            _catalogue = catalogue;
            // Holding an incomplete catalogue for half a day would mean
            // preserving a disturbance. The next call should be allowed to
            // try again.
            _catalogUntil = DateTimeOffset.UtcNow
                + (catalogue.IsComplete ? _opt.CatalogTtl : TimeSpan.FromMinutes(2));
            _catalogVersion = _store.Version;
            _log.LogInformation(
                "Router discovered: {Model}, {Services} services, {Actions} actions{Rest}",
                catalogue.Model, catalogue.Services.Count, catalogue.ActionCount,
                catalogue.IsComplete ? "" : $" - {catalogue.Incomplete.Count} Dienste fehlen");
            return catalogue;
        }
        finally
        {
            _catalogueLock.Release();
        }
    }

    // A catalogue belonging to different credentials is worthless: it may
    // come from an entirely different device.
    private bool Valid() =>
        _catalogue is not null
        && _catalogVersion == _store.Version
        && DateTimeOffset.UtcNow < _catalogUntil;

    private async Task<RouterCatalog> EntdeckeAsync(CancellationToken ct)
    {
        var description = await LadeXmlAsync(new Uri(PlainBasis, "/tr64desc.xml"), ct);
        XNamespace ns = "urn:dslforum-org:device-1-0";

        var modell = description.Descendants(ns + "modelName").FirstOrDefault()?.Value ?? "unbekannt";
        var name = description.Descendants(ns + "friendlyName").FirstOrDefault()?.Value ?? modell;
        var version = description.Descendants(ns + "Display").FirstOrDefault()?.Value
            ?? description.Descendants(ns + "softwareVersion").FirstOrDefault()?.Value;

        var services = new List<RouterServiceInfo>();
        var gaps = new List<string>();
        foreach (var s in description.Descendants(ns + "service"))
        {
            var type = s.Element(ns + "serviceType")?.Value ?? "";
            var scpd = s.Element(ns + "SCPDURL")?.Value ?? "";
            var control = s.Element(ns + "controlURL")?.Value ?? "";
            if (type.Length == 0 || scpd.Length == 0 || control.Length == 0)
            {
                continue;
            }

            // urn:dslforum-org:service:Hosts:1 -> Hosts
            var parts = type.Split(':');
            var shortName = parts.Length >= 2 ? parts[^2] : type;

            IReadOnlyList<RouterAction> actions;
            try
            {
                actions = await LoadActionsAsync(new Uri(PlainBasis, scpd), ct);
            }
            catch (Exception ex)
            {
                // A service whose description is missing must not take the
                // discovery of all the others down with it - but it must not
                // vanish without trace either. The gap travels into the
                // catalogue and from there into the interface.
                _log.LogWarning(ex, "The SCPD of {Service} cannot be read", shortName);
                gaps.Add(shortName);
                actions = [];
            }

            services.Add(new RouterServiceInfo(shortName, type, control, scpd, actions));
        }

        if (gaps.Count > 0)
        {
            _log.LogWarning(
                "Discovery incomplete: {Count} services missing ({Names})",
                gaps.Count, string.Join(", ", gaps));
        }

        return new RouterCatalog(modell, name, version, services, gaps);
    }

    private async Task<IReadOnlyList<RouterAction>> LoadActionsAsync(Uri url, CancellationToken ct)
    {
        var scpd = await LadeXmlAsync(url, ct);
        XNamespace ns = "urn:dslforum-org:service-1-0";

        // The details about type, permitted values and bounds are not on the
        // action but on the state variable the parameter refers to. Only
        // brought together do they make an input field.
        var variablen = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in scpd.Descendants(ns + "stateVariable"))
        {
            var vn = v.Element(ns + "name")?.Value;
            if (vn is not null)
            {
                variablen[vn] = v;
            }
        }

        var actions = new List<RouterAction>();
        foreach (var a in scpd.Descendants(ns + "action"))
        {
            var an = a.Element(ns + "name")?.Value;
            if (an is null)
            {
                continue;
            }

            var args = new List<RouterArgument>();
            foreach (var arg in a.Descendants(ns + "argument"))
            {
                var argName = arg.Element(ns + "name")?.Value ?? "";
                var direction = arg.Element(ns + "direction")?.Value ?? "in";
                var source = arg.Element(ns + "relatedStateVariable")?.Value ?? "";

                var type = "string";
                var allowed = new List<string>();
                string? min = null, max = null, fallback = null;
                if (variablen.TryGetValue(source, out var v))
                {
                    type = v.Element(ns + "dataType")?.Value ?? "string";
                    fallback = v.Element(ns + "defaultValue")?.Value;
                    foreach (var w in v.Descendants(ns + "allowedValue"))
                    {
                        allowed.Add(w.Value);
                    }
                    var range = v.Element(ns + "allowedValueRange");
                    if (range is not null)
                    {
                        min = range.Element(ns + "minimum")?.Value;
                        max = range.Element(ns + "maximum")?.Value;
                    }
                }

                args.Add(new RouterArgument(argName, direction, source, type, allowed, min, max, fallback));
            }

            actions.Add(new RouterAction(an, args));
        }

        return actions;
    }

    /// <summary>
    /// Fetches a description file, with a retry.
    ///
    /// A Fritz!Box does not deliver its close to 40 SCPD files in one go:
    /// asking for them in quick succession means some do not arrive at all.
    /// So there is a short wait between attempts - on the second run the file
    /// is there as a rule.
    /// </summary>
    private async Task<XDocument> LadeXmlAsync(Uri url, CancellationToken ct)
    {
        Exception? previous = null;
        for (var versuch = 0; versuch < 3; versuch++)
        {
            if (versuch > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * versuch), ct);
            }

            try
            {
                using var reply = await _open.GetAsync(url, ct);
                reply.EnsureSuccessStatusCode();
                return XDocument.Parse(await reply.Content.ReadAsStringAsync(ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                previous = ex;
            }
        }

        throw previous ?? new InvalidOperationException($"{url} cannot be read");
    }

    /// <summary>
    /// Invokes an action. The values come in as name-value pairs and go back
    /// as name-value pairs — the interface needs to know nothing about SOAP.
    /// </summary>
    public async Task<RouterResult> InvokeAsync(
        RouterServiceInfo service,
        RouterAction action,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken ct = default)
    {
        if (!action.IsReadOnly && _opt.ReadOnly)
        {
            return RouterResult.Failed(Strings.Current.ReadOnlyBlocked);
        }

        // It is tried without an account anyway: part of the read actions are
        // open - a Fritz!Box's device list for instance. What needs an
        // account the router says itself with a 401, and that is more honest
        // information than a block in our code that would have to guess which
        // action is open.

        var body = new StringBuilder();
        body.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        body.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" ");
        body.Append("s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>");
        body.Append($"<u:{action.Name} xmlns:u=\"{service.ServiceType}\">");
        foreach (var arg in action.In)
        {
            values.TryGetValue(arg.Name, out var value);
            body.Append($"<{arg.Name}>{Escape(value ?? "")}</{arg.Name}>");
        }
        body.Append($"</u:{action.Name}></s:Body></s:Envelope>");

        var soap = body.ToString();
        var actionHeader = $"{service.ServiceType}#{action.Name}";

        HttpStatusCode status;
        string text;
        try
        {
            (status, text) = await SendeAsync(
                _opt.Configured ? SignedIn : _open,
                _opt.Configured ? TlsBasis : PlainBasis,
                service, soap, actionHeader, ct);

            // A wrongly stored account must not break what would work without
            // one. On a rejected sign-in a read action is therefore tried
            // again over the open route - so the device list stays available
            // even when the password is wrong.
            if (status == HttpStatusCode.Unauthorized && _opt.Configured && action.IsReadOnly)
            {
                var (openStatus, openText) = await SendeAsync(
                    _open, PlainBasis, service, soap, actionHeader, ct);
                if (openStatus == HttpStatusCode.OK)
                {
                    _log.LogDebug(
                        "{Action} answered over the open route - the sign-in was rejected",
                        action.Name);
                    (status, text) = (openStatus, openText);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "The router call {Action} failed", action.Name);
            return RouterResult.Failed(Strings.Current.RouterUnreachable(ex.Message));
        }

        {
            if (status == HttpStatusCode.Unauthorized)
            {
                return RouterResult.Failed(_opt.Configured
                    ? Strings.Current.SignInRefused
                    : Strings.Current.ActionNeedsAccount);
            }

            if (status != HttpStatusCode.OK)
            {
                var (code, plainText) = ReadError(text);
                if (code is not null)
                {
                    return RouterResult.Failed(Strings.Current.RouterReportsError(code, plainText ?? ""));
                }
                return RouterResult.Failed(Strings.Current.RouterAnswersHttp((int)status));
            }
        }

        return RouterResult.Success(ReadOutput(text, action));
    }

    private async Task<(HttpStatusCode Status, string Text)> SendeAsync(
        HttpClient client, Uri basis, RouterServiceInfo service,
        string soap, string actionHeader, CancellationToken ct)
    {
        using var query = new HttpRequestMessage(
            HttpMethod.Post, new Uri(basis, service.ControlUrl))
        {
            Content = new StringContent(soap, Encoding.UTF8),
        };
        query.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
        query.Headers.TryAddWithoutValidation("SoapAction", actionHeader);

        using var reply = await client.SendAsync(query, ct);
        return (reply.StatusCode, await reply.Content.ReadAsStringAsync(ct));
    }

    private static (string? Code, string Text) ReadError(string soap)
    {
        try
        {
            var d = XDocument.Parse(soap);
            var code = d.Descendants().FirstOrDefault(e => e.Name.LocalName == "errorCode")?.Value;
            var text = d.Descendants().FirstOrDefault(e => e.Name.LocalName == "errorDescription")?.Value;
            return (code, text ?? Strings.Current.WithoutFurtherDetail);
        }
        catch
        {
            return (null, "");
        }
    }

    private static Dictionary<string, string> ReadOutput(string soap, RouterAction action)
    {
        var outbound = new Dictionary<string, string>();
        try
        {
            var d = XDocument.Parse(soap);
            foreach (var arg in action.Out)
            {
                var e = d.Descendants().FirstOrDefault(x => x.Name.LocalName == arg.Name);
                if (e is not null)
                {
                    outbound[arg.Name] = e.Value;
                }
            }
        }
        catch
        {
            // An answer that is not XML simply yields nothing - the caller sees
            // an empty result rather than an exception.
        }
        return outbound;
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&apos;");

    public void Dispose()
    {
        _open.Dispose();
        _signedInCache?.Dispose();
        _catalogueLock.Dispose();
        GC.SuppressFinalize(this);
    }
}

public record RouterResult(bool Ok, IReadOnlyDictionary<string, string> Values, string? Error)
{
    public static RouterResult Success(IReadOnlyDictionary<string, string> values) =>
        new(true, values, null);

    public static RouterResult Failed(string report) =>
        new(false, new Dictionary<string, string>(), report);
}
