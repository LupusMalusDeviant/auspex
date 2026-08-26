using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Auspex.Control.Services.Router;
using Auspex.Control.Services.Localization;

/// <summary>
/// The second channel: the Fritz!Box web interface.
///
/// TR-064 cannot set the local DNS server — across both device descriptions
/// there is no action for it, only reading ones. The web interface can, and
/// it is reachable with the same credentials. The price: this is an
/// undocumented interface. The sign-in is described by AVM themselves and is
/// stable; the page and field names behind it are not, and can move with a
/// firmware update.
///
/// So this route stays restricted to the necessary and reports clearly when
/// it fails to recognise something, rather than guessing.
/// </summary>
public partial class FritzWebClient(IRouterSettingsStore store, ILogger<FritzWebClient> log)
{
    [GeneratedRegex(@"<input[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InputField();

    [GeneratedRegex(@"(\w+)\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex Attribut();

    [GeneratedRegex(@"""twofactor""\s*:\s*""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex Zweifaktor();

    [GeneratedRegex(@"<form[^>]*action\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex FormTarget();

    private HttpClient Build()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private Uri BaseUrl => new($"http://{store.Current.Host}");

    /// <summary>
    /// Signs in and returns the session id.
    ///
    /// Two-stage PBKDF2-SHA256 over the challenge, the way AVM describe it.
    /// Older boxes answer with an MD5 scheme; that is deliberately not
    /// supported here — whoever runs firmware that old should get a clear
    /// refusal rather than a weak stopgap.
    /// </summary>
    private async Task<string?> SignInAsync(HttpClient client, CancellationToken ct)
    {
        var opt = store.Current;
        if (!opt.Configured)
        {
            return null;
        }

        var xml = await client.GetStringAsync(new Uri(BaseUrl, "/login_sid.lua?version=2"), ct);
        var challenge = Between(xml, "<Challenge>", "</Challenge>");
        if (challenge is null)
        {
            return null;
        }

        var parts = challenge.Split('$');
        if (parts.Length != 5 || parts[0] != "2")
        {
            log.LogWarning(
                "The Fritz!Box does not offer the expected sign-in method (challenge: {Kind})",
                parts.Length > 0 ? parts[0] : "leer");
            return null;
        }

        var h1 = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(opt.Password), Convert.FromHexString(parts[2]),
            int.Parse(parts[1]), HashAlgorithmName.SHA256, 32);
        var h2 = Rfc2898DeriveBytes.Pbkdf2(
            h1, Convert.FromHexString(parts[4]),
            int.Parse(parts[3]), HashAlgorithmName.SHA256, 32);

        var reply = $"{parts[4]}${Convert.ToHexString(h2).ToLowerInvariant()}";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = opt.User,
            ["response"] = reply,
        });

        using var a = await client.PostAsync(new Uri(BaseUrl, "/login_sid.lua?version=2"), content, ct);
        var sid = Between(await a.Content.ReadAsStringAsync(ct), "<SID>", "</SID>");
        if (sid is null || sid.All(c => c == '0'))
        {
            log.LogWarning("The Fritz!Box web interface rejects the sign-in");
            return null;
        }

        return sid;
    }

    private async Task SignOutAsync(HttpClient client, string sid, CancellationToken ct)
    {
        try
        {
            // An open session occupies one of the few slots the box hands out.
            // Whoever leaves them lying around eventually locks themselves
            // out of the web interface.
            await client.GetStringAsync(new Uri(BaseUrl, $"/login_sid.lua?logout=1&sid={sid}"), ct);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Signing out of the web interface failed");
        }
    }

    /// <summary>
    /// Reads the home network's IPv4 settings — among them the local DNS
    /// server the box hands out to every device over DHCP.
    /// </summary>
    public async Task<Ipv4Settings?> GetIpv4Async(CancellationToken ct = default)
    {
        using var client = Build();
        var sid = await SignInAsync(client, ct);
        if (sid is null)
        {
            return null;
        }

        try
        {
            var html = await GetPageAsync(client, sid, ct);
            return Read(html);
        }
        finally
        {
            await SignOutAsync(client, sid, ct);
        }
    }

    /// <summary>
    /// Sets the local DNS server.
    ///
    /// The form is read in full first and sent back unchanged — except for
    /// the four DNS server fields. Sending individual values only would be
    /// dangerous: the same form carries the box's address, subnet mask, DHCP
    /// range and lease time, and whatever does not come along can be reset to
    /// default by the box. A reset DHCP range only shows when devices stop
    /// getting addresses.
    /// </summary>
    public async Task<(bool Ok, string ReportItem)> SetLokalerDnsAsync(
        string ipv4, CancellationToken ct = default)
    {
        if (store.Current.ReadOnly)
        {
            return (false, Strings.Current.ReadOnlyBlocked);
        }

        var parts = ipv4.Split('.');
        if (parts.Length != 4 || parts.Any(t => !byte.TryParse(t, out _)))
        {
            return (false, Strings.Current.NoIpv4Address(ipv4));
        }

        if (!await AnswersOnPort53Async(ipv4, ct))
        {
            return (false, Strings.Current.DnsNotAnswering(ipv4));
        }

        using var client = Build();
        var sid = await SignInAsync(client, ct);
        if (sid is null)
        {
            return (false, Strings.Current.WebSignInRefused);
        }

        try
        {
            var html = await GetPageAsync(client, sid, ct);
            var before = Read(html);
            if (before is null)
            {
                return (false, Strings.Current.FieldNamesChanged);
            }

            // Take everything over unchanged, replace only the four DNS fields.
            var fields = new Dictionary<string, string>(before.AllFields)
            {
                ["Dns_all0"] = parts[0],
                ["Dns_all1"] = parts[1],
                ["Dns_all2"] = parts[2],
                ["Dns_all3"] = parts[3],
                ["sid"] = sid,
                // The name of the submit button. Without it the box sees a form
                // with no intent and does nothing.
                ["apply"] = "",
            };

            using var a = await client.PostAsync(
                new Uri(BaseUrl, before.Destination), new FormUrlEncodedContent(fields), ct);
            if (a.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Found))
            {
                return (false, $"Die Fritz!Box antwortet mit HTTP {(int)a.StatusCode}.");
            }

            var reply = await a.Content.ReadAsStringAsync(ct);

            // The box considers network settings security-relevant: it accepts
            // the change but only carries it out after a confirmation on the
            // device. Without recognising this it looks like a silent
            // failure - the call succeeds, the value stays put, and nobody
            // knows why.
            if (SecondFactor(reply) is { } hint)
            {
                return (false, hint);
            }

            // Read it back rather than believing it: the box accepts a form even
            // when it discards a value.
            var after = Read(await GetPageAsync(client, sid, ct));
            if (after is null)
            {
                return (false, Strings.Current.SentButUnreadable);
            }

            if (after.LocalDns == ipv4)
            {
                log.LogInformation("The Fritz!Box local DNS server has been set to {Address}", ipv4);
                return (true, Strings.Current.DnsSet(ipv4));
            }

            return (false, Strings.Current.DnsNotApplied(after.LocalDns));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Setting the local DNS server failed");
            return (false, Strings.Current.Failed(ex.Message));
        }
        finally
        {
            await SignOutAsync(client, sid, ct);
        }
    }

    /// <summary>
    /// Is there a DNS server answering at this address at all?
    ///
    /// The most important guard on this page. Whoever sets the local DNS
    /// server to an address where nobody is listening takes name resolution
    /// away from the whole house — so thoroughly that even the interface you
    /// would undo it with becomes unreachable. That is exactly the risk while
    /// Auspex is still sitting on a test port.
    /// </summary>
    public static async Task<bool> AnswersOnPort53Async(
        string ipv4, CancellationToken ct = default)
    {
        if (!System.Net.IPAddress.TryParse(ipv4, out var address))
        {
            return false;
        }

        // A real query, not an open connection: a listening port is no proof
        // that DNS is spoken there.
        using var udp = new System.Net.Sockets.UdpClient();
        try
        {
            udp.Connect(address, 53);

            // The smallest query imaginable: an A record for the root name.
            byte[] question =
            [
                0x12, 0x34,             // Kennung
                0x01, 0x00,             // Standardabfrage, Rekursion erwuenscht
                0x00, 0x01,             // one question
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00,                   // Wurzelname
                0x00, 0x02,             // Typ NS
                0x00, 0x01,             // Klasse IN
            ];
            await udp.SendAsync(question, ct);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            var reply = await udp.ReceiveAsync(deadline.Token);

            // Dieselbe Kennung zurueck heisst: da spricht wirklich DNS.
            return reply.Buffer.Length >= 2
                && reply.Buffer[0] == 0x12 && reply.Buffer[1] == 0x34;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> GetPageAsync(HttpClient client, string sid, CancellationToken ct)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["xhr"] = "1",
            ["sid"] = sid,
            ["page"] = "boxnet",
            ["lang"] = "de",
        });
        using var a = await client.PostAsync(new Uri(BaseUrl, "/data.lua"), content, ct);
        return await a.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Pulls every form field out of the page. Without an HTML library,
    /// because this is a single page of consistent shape — and because an
    /// extra dependency for twenty lines is not worth the trade.
    /// </summary>
    private static Ipv4Settings? Read(string html)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match m in InputField().Matches(html))
        {
            var attr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match a in Attribut().Matches(m.Value))
            {
                attr[a.Groups[1].Value] = a.Groups[2].Value;
            }

            if (!attr.TryGetValue("name", out var name) || name.Length == 0)
            {
                continue;
            }

            var kind = attr.GetValueOrDefault("type", "text");
            if (kind.Equals("checkbox", StringComparison.OrdinalIgnoreCase))
            {
                // An unticked checkbox is not sent in a form at all - it has to be
                // exactly the same here, otherwise sending it back switches on
                // things that were off.
                if (m.Value.Contains("checked", StringComparison.OrdinalIgnoreCase))
                {
                    fields[name] = attr.GetValueOrDefault("value", "on");
                }
                continue;
            }

            fields[name] = attr.GetValueOrDefault("value", "");
        }

        // Without these fields it is not the page we expect. Better to do
        // nothing then than to submit a form on the off chance.
        string[] required = ["Dns_all0", "Dns_all1", "Dns_all2", "Dns_all3", "Ip_all0"];
        if (required.Any(p => !fields.ContainsKey(p)))
        {
            return null;
        }

        // Where the form goes is written in the form. Hard-coding the address
        // would be the next thing to break with the next firmware - and it
        // already was the first: the post originally went to data.lua, where
        // the box accepts the call and silently discards it.
        var destination = FormTarget().Match(html) is { Success: true } m2
            ? m2.Groups[1].Value
            : "";
        if (destination.Length == 0)
        {
            return null;
        }

        return new Ipv4Settings(
            LocalDns: Vier(fields, "Dns_all"),
            BoxAddress: Vier(fields, "Ip_all"),
            SubnetMask: Vier(fields, "Netmask_all"),
            DhcpOn: fields.ContainsKey("Dhcp_all"),
            DhcpFrom: Vier(fields, "Start_all"),
            DhcpTo: Vier(fields, "End_all"),
            LeaseDays: fields.GetValueOrDefault("lease_time", ""),
            Destination: destination,
            AllFields: fields);
    }

    private static string Vier(IReadOnlyDictionary<string, string> f, string prefix) =>
        string.Join('.', Enumerable.Range(0, 4).Select(i => f.GetValueOrDefault($"{prefix}{i}", "?")));

    /// <summary>
    /// Recognises whether the box is demanding a two-factor confirmation
    /// instead of making the change, and turns that into what to do next.
    ///
    /// It considers network settings security-relevant: it accepts the change
    /// and puts it on hold until somebody confirms on the device. Without
    /// recognising this it looks like a silent failure — the call succeeds,
    /// the value stays put, and nobody knows why.
    /// </summary>
    private static string? SecondFactor(string reply)
    {
        var m = Zweifaktor().Match(reply);
        if (!m.Success)
        {
            return null;
        }

        var raw = m.Groups[1].Value;

        // "starterror" means: the box did not even want to start the
        // confirmation. Usually because a request is still outstanding or
        // because it was tried too many times in a row.
        if (raw.StartsWith("starterror", StringComparison.OrdinalIgnoreCase))
        {
            var code = raw.Split(';').ElementAtOrDefault(1);
            return Strings.Current.ConfirmationRefused(
                code is { Length: > 0 } ? $" ({code})" : "");
        }

        var paths = new List<string>();
        if (raw.Contains("button", StringComparison.OrdinalIgnoreCase))
        {
            paths.Add(Strings.Current.ConfirmationButton);
        }
        if (raw.Contains("googleauth", StringComparison.OrdinalIgnoreCase))
        {
            paths.Add(Strings.Current.ConfirmationApp);
        }
        if (raw.Contains("dtmf", StringComparison.OrdinalIgnoreCase))
        {
            paths.Add(Strings.Current.ConfirmationPhone);
        }

        var kind = paths.Count > 0
            ? string.Join(Strings.Current.ConfirmationOr, paths)
            : Strings.Current.ConfirmationGeneric;

        return Strings.Current.ConfirmationNeeded(kind);
    }

    private static string? Between(string text, string from, string until)
    {
        var a = text.IndexOf(from, StringComparison.Ordinal);
        if (a < 0)
        {
            return null;
        }
        a += from.Length;
        var b = text.IndexOf(until, a, StringComparison.Ordinal);
        return b < 0 ? null : text[a..b];
    }
}

/// <summary>The home network's IPv4 settings.</summary>
public record Ipv4Settings(
    string LocalDns,
    string BoxAddress,
    string SubnetMask,
    bool DhcpOn,
    string DhcpFrom,
    string DhcpTo,
    string LeaseDays,
    string Destination,
    IReadOnlyDictionary<string, string> AllFields)
{
    /// <summary>
    /// Whether the box hands out itself as the DNS server — the factory state.
    /// </summary>
    public bool PointsAtTheBox => LocalDns == BoxAddress;
}
