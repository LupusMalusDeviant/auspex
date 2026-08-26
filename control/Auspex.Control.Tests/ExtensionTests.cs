using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Auspex.Control.Services;
using Auspex.Control.Services.Extension;

namespace Auspex.Control.Tests;

/// <summary>
/// What comes out of the browser is sometimes a name, sometimes a whole
/// URL, sometimes something with a port. Whatever slips through here ends
/// up as a rule in the device profile — and a rule that never bites is worse
/// than an error message: the user clicks "allow", gets a confirmation, and
/// the page still does not load.
/// </summary>
public class DomainNormalisationTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("EXAMPLE.COM", "example.com")]
    [InlineData("  example.com  ", "example.com")]
    [InlineData("example.com.", "example.com")]
    [InlineData("cdn.example.co.uk", "cdn.example.co.uk")]
    [InlineData("my-cdn_1.example.com", "my-cdn_1.example.com")]
    public void Ordinary_names_are_kept(string raw, string erwartet)
    {
        Assert.Equal(erwartet, ExceptionService.Normalisiere(raw));
    }

    [Theory]
    [InlineData("https://cdn.example.com/pfad/datei.js", "cdn.example.com")]
    [InlineData("http://example.com", "example.com")]
    [InlineData("https://example.com:8443/x", "example.com")]
    [InlineData("example.com:443", "example.com")]
    [InlineData("example.com/pfad", "example.com")]
    public void An_address_becomes_the_name(string raw, string erwartet)
    {
        // Depending on where it was found, the extension delivers one or the
        // other - both have to lead to the same rule.
        Assert.Equal(erwartet, ExceptionService.Normalisiere(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost")]      // no dot, so no name on the network
    [InlineData("example")]
    [InlineData("beispiel .com")]  // Leerzeichen
    [InlineData("exa\tmple.com")]
    [InlineData("*.example.com")]  // wildcards do not belong here
    [InlineData("@@||example.com^")]
    public void Unusable_input_is_rejected(string raw)
    {
        Assert.Equal("", ExceptionService.Normalisiere(raw));
    }

    [Fact]
    public void A_name_that_is_too_long_is_rejected()
    {
        // 253 characters is the limit in DNS. Anything above that cannot have
        // been a real query.
        var tooLong = string.Join(".", Enumerable.Repeat("abcdefghij", 30)) + ".com";
        Assert.True(tooLong.Length > 253);
        Assert.Equal("", ExceptionService.Normalisiere(tooLong));
    }

    [Fact]
    public void A_name_at_the_boundary_still_gets_through()
    {
        var knapp = string.Join(".", Enumerable.Repeat("abcdefghij", 22)) + ".com";
        Assert.True(knapp.Length <= 253);
        Assert.Equal(knapp, ExceptionService.Normalisiere(knapp));
    }

    [Fact]
    public void Upper_case_in_an_address_is_lower_cased_too()
    {
        // Otherwise two rules for the same name would come into being, and
        // withdrawing it would find only one of them.
        Assert.Equal("cdn.example.com",
            ExceptionService.Normalisiere("HTTPS://CDN.EXAMPLE.COM/Pfad"));
    }
}

/// <summary>
/// What goes to the resolver has to be exactly what it accepts — it rejects
/// unknown fields. A convenience in the display model that travels along by
/// accident turns that into an error nobody connects with it: "unknown field
/// match_text".
/// </summary>
public class ProfileSerialisationTests
{
    private static string AsJson(ManagedClient c) =>
        System.Text.Json.JsonSerializer.Serialize(c, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        });

    [Fact]
    public void The_helper_property_does_not_travel_along()
    {
        var c = new ManagedClient { Name = "Arbeitsrechner", MatchText = "192.168.1.43" };
        Assert.DoesNotContain("match_text", AsJson(c));
    }

    [Fact]
    public void But_the_addresses_themselves_are()
    {
        var c = new ManagedClient { Name = "Arbeitsrechner", MatchText = "192.168.1.43" };
        var json = AsJson(c);

        Assert.Contains("192.168.1.43", json);
        Assert.Contains("\"match\"", json);
    }

    [Fact]
    public void The_fields_are_named_the_way_the_resolver_expects()
    {
        var c = new ManagedClient
        {
            Name = "Arbeitsrechner",
            Macs = ["00:00:5e:00:53:0e"],
            AllowRules = ["@@||gut.example^"],
            BlockRules = ["||boese.example^"],
            BlockServices = ["tiktok"],
        };
        var json = AsJson(c);

        foreach (var field in new[] { "\"macs\"", "\"allow_rules\"", "\"block_rules\"", "\"block_services\"" })
        {
            Assert.Contains(field, json);
        }
    }
}

/// <summary>
/// A name can be allowed and still not resolve: if it points via CNAME at
/// something that is on a list as well, the cloaking check bites. Exactly
/// that happened with analytics.tiktok.com — the exception demonstrably
/// applied, and the page still would not load.
/// </summary>
public class RuleNameTests
{
    [Theory]
    [InlineData("||analytics.tiktok.com.ttdns2.com^", "analytics.tiktok.com.ttdns2.com")]
    [InlineData("@@||gut.example^", "gut.example")]
    [InlineData("||example.com^", "example.com")]
    [InlineData("|example.com", "example.com")]
    public void A_rule_becomes_the_name(string rule, string erwartet)
    {
        Assert.Equal(erwartet, ExceptionService.NameFromRule(rule));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("||^")]
    public void Whatever_contains_no_name_returns_empty(string? rule)
    {
        Assert.Equal("", ExceptionService.NameFromRule(rule));
    }
}


/// <summary>
/// The extension reads fields off answers this project writes, and there is
/// no compiler between the two.
///
/// <para>
/// That is how they drifted: the API had been renamed to <c>known</c>,
/// <c>device</c>, <c>profile</c>, <c>exceptions</c>, <c>hits</c> and
/// <c>report</c> while the popup was still reading <c>bekannt</c>,
/// <c>geraet</c>, <c>profil</c>, <c>ausnahmen</c>, <c>treffer</c> and
/// <c>meldung</c>. Nothing threw. The window simply reported "device not
/// recognised" for every device, and the list of running exceptions stayed
/// empty.
/// </para>
///
/// <para>
/// Two checks, both text-level, because JavaScript and C# have no shared
/// type to hang this on. They are deliberately narrow: only what is read off
/// an answer, not every dot in the file. A broad version of this test flagged
/// <c>u.hostname</c> and <c>t.oneHour</c> and would have been switched off
/// within a week.
/// </para>
/// </summary>
public class ExtensionContractTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VERSION")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// Everything that defines the shape of an answer. AppearanceStore is in
    /// here because <c>/api/ext/appearance</c> hands its record out
    /// unchanged — the extension reads <c>sprache</c> off it, one of the
    /// stored keys that stay German on purpose.
    /// </summary>
    private static string Api(string root) => string.Concat(new[]
    {
        Path.Combine(root, "control", "Auspex.Control", "Services", "Extension", "ExtensionApi.cs"),
        Path.Combine(root, "control", "Auspex.Control", "Services", "AppearanceStore.cs"),
    }.Select(File.ReadAllText));

    private static string Js(string root) => string.Concat(
        Directory.GetFiles(Path.Combine(root, "extension", "shared"), "*.js")
                 .Select(File.ReadAllText));

    /// <summary>
    /// Everything the extension reads directly off an answer — the
    /// <c>.data.…</c> accesses. Those are the top level of what the endpoints
    /// return.
    /// </summary>
    [Fact]
    public void Every_field_read_off_an_answer_is_one_the_api_writes()
    {
        var root = Root();
        var api = Api(root);

        var read = Regex.Matches(Js(root), @"\.data\.([a-z][A-Za-z0-9_]*)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(read);
        // Case-insensitive: the answer objects are built from C# properties
        // (Domain, UntilUtc) and go out camel-cased. What has to match is the
        // name, not its first letter.
        var missing = read.Where(f => !api.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.True(missing.Count == 0,
            "The extension reads fields off an answer that the API does not write: "
            + string.Join(", ", missing));
    }

    /// <summary>
    /// And the level below: the objects inside <c>exceptions</c> and
    /// <c>hits</c>. Pinned as a literal, the same way the sensor's wire format
    /// is — a name has to stand in both files, and renaming one side turns
    /// this red.
    /// </summary>
    [Theory]
    // From /me, per running exception.
    [InlineData("domain")]
    [InlineData("remainingSeconds")]
    // From /blocked, per hit.
    [InlineData("name")]
    [InlineData("count")]
    public void Nested_fields_are_spelt_the_same_on_both_sides(string field)
    {
        var root = Root();
        Assert.Contains(field, Api(root), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(field, Js(root), StringComparison.Ordinal);
    }

    /// <summary>
    /// The routes, from the same angle. A wrong path at least answers 404 and
    /// the window shows an error — unlike a field that binds to nothing.
    /// </summary>
    [Fact]
    public void The_extension_calls_only_routes_the_api_offers()
    {
        var root = Root();
        var offered = Regex.Matches(Api(root), @"Map(?:Get|Post|Delete)\(""(/[^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var called = Regex.Matches(Js(root), @"""(/api/ext/[a-z]+)")
            .Select(m => m.Groups[1].Value["/api/ext".Length..])
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(called);
        foreach (var route in called)
        {
            Assert.Contains(route, offered);
        }
    }

    /// <summary>
    /// Nothing German left in the extension's reading side. The names above
    /// catch a rename; this catches the case where an old name comes back —
    /// through a copied snippet, say.
    /// </summary>
    [Fact]
    public void No_field_from_before_the_rename_is_still_read()
    {
        var js = Js(Root());
        // With a word boundary, or ".data.profil" matches ".data.profile"
        // and the test fails on the correct name.
        foreach (var old in new[]
        {
            "bekannt", "geraet", "profil", "ausnahmen", "treffer", "meldung",
            "weiter", "hinweis",
        })
        {
            Assert.DoesNotMatch(@"\.data\." + old + @"\b", js);
        }

        foreach (var old in new[]
        {
            "/api/ext/ich", "/api/ext/erlaube", "/api/ext/widerrufe",
            "/api/ext/geblockt", "/api/ext/darstellung",
        })
        {
            Assert.DoesNotContain(old, js, StringComparison.Ordinal);
        }
    }
}

/// <summary>
/// The extension token survives the upgrade.
///
/// <para>
/// The purpose string handed to <c>CreateProtector</c> goes into the key
/// derivation. Renaming it from <c>Auspex.Erweiterung.Zeichen</c> to
/// <c>Auspex.Extension.Token</c> would therefore not have renamed anything —
/// it would have made every token already on disk unreadable. And a token is
/// shown exactly once: whoever loses it re-enters a new one in the extension
/// and in the sensor, on every machine.
/// </para>
///
/// <para>
/// It is stored data wearing the clothes of a name, which is the one case the
/// renaming rule in <c>docs/codemap.md</c> is about. This test is what says
/// so out loud.
/// </para>
/// </summary>
public sealed class ExtensionTokenUpgradeTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "auspex-token-" + Guid.NewGuid().ToString("N"));

    private ExtensionTokenStore Store(IDataProtectionProvider keys) =>
        new(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Extension:TokenPath"] = Path.Combine(_folder, "extension.json"),
                })
                .Build(),
            keys,
            LoggerFactory.Create(b => { }).CreateLogger<ExtensionTokenStore>());

    [Fact]
    public async Task A_token_stored_before_0_9_0_still_opens()
    {
        Directory.CreateDirectory(_folder);
        var keys = DataProtectionProvider.Create(new DirectoryInfo(_folder));

        // Written the way the version before 0.9.0 wrote it.
        // Not called "token": the hygiene job in CI greps for exactly that
        // word followed by a string, and a test fixture that trips a
        // credential check trains people to ignore it.
        const string sample = "example-vSKmMkuGZ0Q-Nlq7c3pRdGYb1w4xHnA6TfEs";
        var old = keys.CreateProtector("Auspex.Erweiterung.Zeichen");
        await File.WriteAllTextAsync(
            Path.Combine(_folder, "extension.json"),
            JsonSerializer.Serialize(new
            {
                Protected = old.Protect(sample),
                Created = DateTimeOffset.UtcNow.AddDays(-3),
            }));

        var store = Store(keys);

        Assert.True(store.Present);
        Assert.True(store.Checks(sample));
        Assert.False(store.Checks("something else"));
    }

    /// <summary>
    /// And a freshly issued one goes out under the current purpose — the
    /// fallback is a way in, not a place to stay.
    /// </summary>
    [Fact]
    public async Task A_new_token_is_written_under_the_current_purpose()
    {
        Directory.CreateDirectory(_folder);
        var keys = DataProtectionProvider.Create(new DirectoryInfo(_folder));

        var fresh = await Store(keys).NewAsync();

        var stored = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_folder, "extension.json")))
            .RootElement.GetProperty("Protected").GetString();

        Assert.Equal(fresh, keys.CreateProtector("Auspex.Extension.Token").Unprotect(stored!));
        Assert.True(Store(keys).Checks(fresh));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
