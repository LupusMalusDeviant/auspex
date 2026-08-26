using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Auspex.Control.Services;

namespace Auspex.Control.Tests;

/// <summary>
/// Saving a profile replaces the stored one whole. So a field the editor's
/// working copy forgets is not left alone — it is deleted, silently, on the
/// next save.
///
/// That happened twice before anybody noticed: the copy carried neither
/// <c>Macs</c> nor <c>Filtering</c>. Editing a profile bound to a MAC unbound
/// it, and a profile with filtering switched off had it switched back on. Both
/// with a green confirmation message.
///
/// This walks the properties instead of listing them, so a field added
/// tomorrow is covered without anybody remembering this file exists.
/// </summary>
public class ProfileCopyTests
{
    /// <summary>Every property the copy has to carry, found by reflection.</summary>
    private static IEnumerable<PropertyInfo> Carried() =>
        typeof(ManagedClient)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            // MatchText is a convenience over Match, not a field of its own.
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null);

    private static ManagedClient Filled() => new()
    {
        Name = "kids",
        Match = ["192.168.1.50"],
        Macs = ["aa:bb:cc:dd:ee:ff"],
        Policy = "enforce",
        Filtering = false,
        BlockRules = ["||tracker.example^"],
        AllowRules = ["@@||school.example^"],
        BlockServices = ["tiktok"],
        SafeSearch = ["google", "youtube-strict"],
        Schedules = [new ManagedSchedule { Name = "night", SafeSearch = ["bing"] }],
    };

    [Fact]
    public void The_working_copy_carries_every_field()
    {
        var original = Filled();
        var copy = original.Copy();

        foreach (var property in Carried())
        {
            var before = property.GetValue(original);
            var after = property.GetValue(copy);

            // Lists compare by content; the reference has to differ, or the
            // copy is not a copy and abandoning an edit would already have
            // changed what is on screen.
            if (before is System.Collections.IEnumerable and not string)
            {
                Assert.Equal(
                    JsonSerializer.Serialize(before),
                    JsonSerializer.Serialize(after));
                Assert.NotSame(before, after);
                continue;
            }
            Assert.Equal(before, after);
        }
    }

    /// <summary>
    /// The guard that makes the test above worth anything: a property set to
    /// its default value would compare equal even if the copy dropped it.
    /// </summary>
    [Fact]
    public void The_test_fixture_sets_every_field_to_something_visible()
    {
        var filled = Filled();
        var empty = new ManagedClient();

        foreach (var property in Carried())
        {
            Assert.NotEqual(
                JsonSerializer.Serialize(property.GetValue(empty)),
                JsonSerializer.Serialize(property.GetValue(filled)));
        }
    }
}

/// <summary>
/// The names on the wire, checked against the resolver's own. Go rejects
/// unknown fields, so a mismatch here is not a field that arrives empty but a
/// request that is refused whole — and the message ("unknown field
/// safe_search") points at the resolver, which is not where the fault is.
/// </summary>
public class SafeSearchContractTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void A_profile_sends_safe_search_under_the_name_the_resolver_reads()
    {
        var json = JsonSerializer.Serialize(
            new ManagedClient { Name = "kids", SafeSearch = ["google"] }, Options);

        Assert.Contains("\"safe_search\":[\"google\"]", json);
    }

    [Fact]
    public void A_schedule_sends_safe_search_under_the_same_name()
    {
        var json = JsonSerializer.Serialize(
            new ManagedSchedule { Name = "night", SafeSearch = ["youtube-strict"] }, Options);

        Assert.Contains("\"safe_search\":[\"youtube-strict\"]", json);
    }

    /// <summary>
    /// And the other direction: what the resolver hands out has to arrive as
    /// a provider with a key and a name. Without the key the checkbox saves
    /// nothing; without the name the interface shows an empty row.
    /// </summary>
    [Fact]
    public void The_catalogue_is_read_back_from_the_resolvers_shape()
    {
        var providers = JsonSerializer.Deserialize<List<SafeSearchProvider>>(
            """[{"key":"google","name":"Google"},{"key":"bing","name":"Bing"}]""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(providers);
        Assert.Equal(2, providers!.Count);
        Assert.Equal("google", providers[0].Key);
        Assert.Equal("Google", providers[0].Name);
    }
}
