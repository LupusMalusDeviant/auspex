using Auspex.Control.Services;

namespace Auspex.Control.Tests;

/// <summary>
/// Grouping can go wrong in two ways, and the second one is the dangerous
/// one: group too little and the log stays unreadable — group too much and a
/// difference disappears that nobody will ever set eyes on again.
/// </summary>
public class QueryGroupingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 24, 15, 28, 13, TimeSpan.Zero);

    private static QueryLogEntry Entry(
        string name = "beispiel.example",
        string type = "A",
        string client = "192.168.1.20",
        string? clientName = "Arbeitsrechner",
        string action = "blocked",
        string? rule = "||beispiel.example^",
        int secondOffset = 0,
        double ms = 0.1) =>
        new(
            Seq: 1,
            Time: Now.AddSeconds(secondOffset),
            Client: client,
            ClientName: clientName,
            Profile: null,
            Name: name,
            Domain: null,
            Type: type,
            Action: action,
            Source: "filter",
            Rule: rule,
            Cname: null,
            RuleKind: null,
            List: "hagezi-multi-pro",
            Schedule: null,
            Upstream: null,
            Rcode: "NXDOMAIN",
            Validated: false,
            Answers: null,
            Millis: ms,
            Error: null);

    /// <summary>The everyday case: one call, three record types, one row.</summary>
    [Fact]
    public void Three_record_types_become_one_row()
    {
        var g = Assert.Single(QueryGrouping.Group([
            Entry(type: "A"),
            Entry(type: "AAAA"),
            Entry(type: "HTTPS"),
        ]));

        Assert.Equal(3, g.Count);
        Assert.Equal(["A", "AAAA", "HTTPS"], g.Types);
    }

    /// <summary>
    /// Alphabetically AAAA would come before A. The order follows the one a
    /// client asks the record types in — otherwise the same combination
    /// looks different from what is expected every time.
    /// </summary>
    [Fact]
    public void The_record_types_are_in_query_order()
    {
        var g = Assert.Single(QueryGrouping.Group([
            Entry(type: "HTTPS"),
            Entry(type: "AAAA"),
            Entry(type: "A"),
        ]));

        Assert.Equal(["A", "AAAA", "HTTPS"], g.Types);
    }

    /// <summary>
    /// The most important test. If the A query got through and the HTTPS
    /// query did not, a shared row would hide exactly the difference that is
    /// interesting.
    /// </summary>
    [Fact]
    public void Different_decisions_stay_separate()
    {
        var groups = QueryGrouping.Group([
            Entry(type: "A", action: "blocked"),
            Entry(type: "HTTPS", action: "allowed", rule: null),
        ]);

        Assert.Equal(2, groups.Count);
    }

    /// <summary>
    /// The same decision, but through a different rule: that is a difference
    /// you want to see as well.
    /// </summary>
    [Fact]
    public void Different_rules_stay_separate()
    {
        var groups = QueryGrouping.Group([
            Entry(type: "A", rule: "||beispiel.example^"),
            Entry(type: "AAAA", rule: "||example^"),
        ]);

        Assert.Equal(2, groups.Count);
    }

    /// <summary>
    /// The test has always been called this but used to check something
    /// else: it varied only the address and left both entries the same
    /// device name. As long as grouping went by address that did not show —
    /// since grouping follows the device, it does.
    /// </summary>
    [Fact]
    public void Different_devices_stay_separate()
    {
        var groups = QueryGrouping.Group([
            Entry(client: "192.168.1.20", clientName: "Arbeitsrechner"),
            Entry(client: "192.168.1.21", clientName: "Fernseher Wohnzimmer"),
        ]);

        Assert.Equal(2, groups.Count);
    }

    /// <summary>
    /// One second is the boundary. Two calls for the same name in
    /// consecutive seconds are two events — exactly the pattern a steady
    /// talker is recognised by.
    /// </summary>
    [Fact]
    public void Different_seconds_stay_separate()
    {
        var groups = QueryGrouping.Group([
            Entry(secondOffset: 0),
            Entry(secondOffset: 1),
        ]);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void Different_names_stay_separate()
    {
        var groups = QueryGrouping.Group([
            Entry(name: "eins.example"),
            Entry(name: "zwei.example"),
        ]);

        Assert.Equal(2, groups.Count);
    }

    /// <summary>
    /// The log arrives newest first, and that is how it should stay.
    /// Grouping that re-sorts would be useless for a running stream.
    /// </summary>
    [Fact]
    public void The_order_is_preserved()
    {
        var groups = QueryGrouping.Group([
            Entry(name: "drei.example", secondOffset: 2),
            Entry(name: "zwei.example", secondOffset: 1),
            Entry(name: "eins.example", secondOffset: 0),
        ]);

        Assert.Equal(
            ["drei.example", "zwei.example", "eins.example"],
            groups.Select(g => g.Vertreter.Name));
    }

    /// <summary>
    /// The slowest answer counts, not the first. Whoever is looking for
    /// outliers does not want a fast cache hit hiding a slow upstream answer
    /// in the same group.
    /// </summary>
    [Fact]
    public void The_slowest_answer_is_shown()
    {
        var g = Assert.Single(QueryGrouping.Group([
            Entry(type: "A", ms: 0.1),
            Entry(type: "AAAA", ms: 42.7),
            Entry(type: "HTTPS", ms: 0.2),
        ]));

        Assert.Equal(42.7, g.MaxMs);
    }

    /// <summary>
    /// A list's tone has to be the same across process boundaries. With
    /// <c>string.GetHashCode</c> it would not be — in .NET the value is
    /// random per process, and every list would have a different colour
    /// after a restart. Exactly this mistake has happened in the watch
    /// service once already.
    /// </summary>
    [Theory]
    [InlineData("hagezi-multi-pro")]
    [InlineData("stevenblack")]
    [InlineData("oisd-big")]
    public void The_same_list_name_always_gives_the_same_tone(string list)
    {
        var tone = QueryGrouping.ListTone(list);
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(tone, QueryGrouping.ListTone(list));
        }
    }

    [Theory]
    [InlineData("hagezi-multi-pro", "HAGEZI-Multi-Pro")]
    [InlineData("oisd", "  oisd  ")]
    public void Spelling_and_whitespace_do_not_change_the_tone(string a, string b)
        => Assert.Equal(QueryGrouping.ListTone(a), QueryGrouping.ListTone(b));

    [Fact]
    public void The_tone_is_always_in_the_valid_range()
    {
        foreach (var n in new[] { "a", "hagezi-multi-pro", "sehr-langer-listenname-mit-vielen-zeichen",
                                  "üäöß", "1", new string('x', 500) })
        {
            var tone = QueryGrouping.ListTone(n);
            Assert.InRange(tone, 1, 6);
        }
    }

    /// <summary>Without a list there is nothing to colour.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_list_no_tone(string? list)
        => Assert.Equal(0, QueryGrouping.ListTone(list));

    /// <summary>
    /// For the lists that really are in use together, the tones should not
    /// repeat if it can be helped - otherwise the colour contributes
    /// nothing.
    /// </summary>
    [Fact]
    public void Common_lists_spread_across_the_tones()
    {
        string[] lists = ["hagezi-multi-pro", "stevenblack", "oisd-big",
                           "adguard-base", "easylist", "eigene-regeln"];
        var tones = lists.Select(QueryGrouping.ListTone).Distinct().Count();
        Assert.True(tones >= 4, $"nur {tones} verschiedene Toene fuer {lists.Length} Listen");
    }

    [Fact]
    public void An_empty_list_yields_no_groups()
    {
        Assert.Empty(QueryGrouping.Group([]));
    }

    /// <summary>
    /// Without a name the address stays. A device without a name must not
    /// end up as an empty entry in the picker.
    /// </summary>
    [Fact]
    public void Without_a_name_the_address_appears()
    {
        Assert.Equal("192.168.1.99",
            QueryGrouping.Device(Entry(client: "192.168.1.99", clientName: null)));
        Assert.Equal("192.168.1.99",
            QueryGrouping.Device(Entry(client: "192.168.1.99", clientName: "")));
    }

    [Fact]
    public void With_a_name_the_name_appears()
    {
        Assert.Equal("Arbeitsrechner",
            QueryGrouping.Device(Entry(client: "192.168.1.20", clientName: "Arbeitsrechner")));
    }

    /// <summary>
    /// The same device under IPv4 and IPv6 is ONE row.
    ///
    /// The opposite used to stand here, justified with "two different
    /// queries from two addresses". That was wrongly reasoned: a modern
    /// client asks over both families at once, and the log therefore held
    /// two almost identical rows for a single call — exactly the repetition
    /// the ruler is built against. It also contradicted the principle the
    /// whole identity detection rests on.
    /// </summary>
    [Fact]
    public void A_device_with_two_addresses_is_one_row()
    {
        var v4 = Entry(client: "192.168.1.20", clientName: "Arbeitsrechner");
        var v6 = Entry(client: "fd00:1234:5678:0:1:2:3:4", clientName: "Arbeitsrechner", type: "AAAA");

        Assert.Equal(QueryGrouping.Device(v4), QueryGrouping.Device(v6));
        var g = Assert.Single(QueryGrouping.Group([v4, v6]));
        Assert.Equal(2, g.Count);
        Assert.Equal(["A", "AAAA"], g.Types);
    }

    /// <summary>
    /// But only if both came out the same. If the query over IPv6 got
    /// through and the one over IPv4 did not, a shared row would hide
    /// exactly the difference that is interesting.
    /// </summary>
    [Fact]
    public void Two_addresses_with_different_outcomes_stay_separate()
    {
        var v4 = Entry(client: "192.168.1.20", clientName: "Arbeitsrechner", action: "blocked");
        var v6 = Entry(client: "fd00:1234:5678:0:1:2:3:4", clientName: "Arbeitsrechner",
                         type: "AAAA", action: "allowed", rule: null);

        Assert.Equal(2, QueryGrouping.Group([v4, v6]).Count);
    }

    /// <summary>
    /// Without a name the address stays the key — two nameless devices must
    /// not collapse into one just because neither has a name.
    /// </summary>
    [Fact]
    public void Two_nameless_devices_stay_separate()
    {
        var a = Entry(client: "192.168.1.90", clientName: null);
        var b = Entry(client: "192.168.1.91", clientName: null);

        Assert.Equal(2, QueryGrouping.Group([a, b]).Count);
    }
}
