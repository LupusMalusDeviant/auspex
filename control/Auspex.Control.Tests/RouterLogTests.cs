using System.Globalization;
using Auspex.Control.Services.Router;

namespace Auspex.Control.Tests;

/// <summary>
/// The log arrives as a single string with no line breaks. Split it wrongly
/// and messages disappear or stick together — and neither is noticed,
/// because the wall was unreadable before that anyway.
/// </summary>
public class RouterLogTests
{
    // Echter Ausschnitt aus einer FRITZ!Box 5690 Pro.
    private const string Real =
        "06.08.26 18:57:17 WLAN-Übertragungsqualität durch reduzierte Kanalbandbreite erhöht (2,4 GHz). "
        + "01.08.26 03:19:12 Internetverbindung IPv6 wurde erfolgreich hergestellt. "
        + "01.08.26 03:18:56 PPPoE-Fehler: Unbekannter Fehler. PPPCHANNEL_connect failed "
        + "01.08.26 03:15:51 DSL antwortet nicht (Keine DSL-Synchronisierung).";

    [Fact]
    public void The_wall_of_text_becomes_individual_entries()
    {
        var e = RouterLog.Parse(Real);
        Assert.Equal(4, e.Count);
    }

    [Fact]
    public void Date_and_time_are_kept_separate()
    {
        var e = RouterLog.Parse(Real);
        Assert.Equal("06.08.26", e[0].Date);
        Assert.Equal("18:57:17", e[0].Time);
        Assert.Equal(new DateTime(2026, 8, 6, 18, 57, 17), e[0].Timestamp);
    }

    [Fact]
    public void The_timestamp_does_not_stay_in_the_message_text()
    {
        var e = RouterLog.Parse(Real);
        Assert.DoesNotContain("06.08.26", e[0].Text);
        Assert.StartsWith("WLAN-Übertragungsqualität", e[0].Text);
    }

    [Fact]
    public void A_message_with_two_sentences_stays_one_entry()
    {
        // "PPPoE error: ... PPPCHANNEL_connect failed" is one message, not a
        // second entry - splitting happens only at the timestamp.
        var e = RouterLog.Parse(Real);
        var pppoe = e.Single(x => x.Text.StartsWith("PPPoE-Fehler"));
        Assert.Contains("PPPCHANNEL_connect failed", pppoe.Text);
    }

    [Theory]
    [InlineData("PPPoE-Fehler: Unbekannter Fehler", "fehler")]
    [InlineData("DSL antwortet nicht (Keine DSL-Synchronisierung)", "fehler")]
    [InlineData("WLAN-Übertragungsqualität durch reduzierte Kanalbandbreite erhöht", "wlan")]
    [InlineData("Internetverbindung IPv6 wurde erfolgreich hergestellt", "internet")]
    [InlineData("Anmeldung des Benutzers auspex an der FRITZ!Box-Benutzeroberfläche", "anmeldung")]
    public void Messages_are_roughly_classified(string text, string erwartet)
    {
        var e = RouterLog.Parse($"01.01.26 12:00:00 {text}");
        Assert.Single(e);
        Assert.Equal(erwartet, e[0].Kategorie);
    }

    [Fact]
    public void An_error_beats_the_range()
    {
        // "DSL is not answering" contains "DSL" as well - being classified as
        // an error is more useful here, otherwise the fault hides under
        // "internet".
        var e = RouterLog.Parse("01.01.26 12:00:00 DSL antwortet nicht.");
        Assert.True(e[0].IsError);
    }

    [Fact]
    public void An_empty_log_yields_no_entries()
    {
        Assert.Empty(RouterLog.Parse(null));
        Assert.Empty(RouterLog.Parse(""));
        Assert.Empty(RouterLog.Parse("   "));
    }

    [Fact]
    public void Text_without_a_timestamp_is_discarded_rather_than_disturbing()
    {
        Assert.Empty(RouterLog.Parse("something without a date"));
    }
}

/// <summary>
/// The search happens in German, everything is named in English. Without
/// the mapping "sperren" finds nothing, and the catalogue is useless for
/// everybody who does not know the TR-064 identifiers by heart.
/// </summary>
public class RouterSearchTests
{
    private static readonly RouterServiceInfo Filter = new(
        "X_AVM-DE_HostFilter", "urn:x:service:X_AVM-DE_HostFilter:1",
        "/upnp/control/x_hostfilter", "/x.xml", []);

    private static readonly RouterServiceInfo Wlan = new(
        "WLANConfiguration", "urn:x:service:WLANConfiguration:1",
        "/upnp/control/wlanconfig1", "/x.xml", []);

    private static readonly RouterServiceInfo Wan = new(
        "WANIPConnection", "urn:x:service:WANIPConnection:1",
        "/upnp/control/wanipconnection", "/x.xml", []);

    private static RouterAction A(string name) => new(name, []);

    [Theory]
    [InlineData("sperren")]
    [InlineData("sperre")]
    [InlineData("blockieren")]
    [InlineData("kindersicherung")]
    public void German_terms_find_the_block(string query)
    {
        Assert.True(RouterSearch.Matches(Filter, A("DisallowWANAccessByIP"), query));
    }

    [Fact]
    public void Port_mapping_finds_the_PortMapping_actions()
    {
        Assert.True(RouterSearch.Matches(Wan, A("GetGenericPortMappingEntry"), "portfreigabe"));
        Assert.True(RouterSearch.Matches(Wan, A("DeletePortMapping"), "freigabe"));
    }

    [Fact]
    public void GuestNetwork_finds_the_wireless_network()
    {
        Assert.True(RouterSearch.Matches(Wlan, A("SetEnable"), "gast"));
    }

    [Fact]
    public void The_English_name_still_works()
    {
        // Whoever knows the identifier should be able to type it.
        Assert.True(RouterSearch.Matches(Wlan, A("SetEnable"), "SetEnable"));
        Assert.True(RouterSearch.Matches(Wlan, A("SetEnable"), "enable"));
    }

    [Fact]
    public void Several_words_all_have_to_match()
    {
        Assert.True(RouterSearch.Matches(Filter, A("DisallowWANAccessByIP"), "sperren internetzugang"));
        Assert.False(RouterSearch.Matches(Wlan, A("SetEnable"), "sperren portfreigabe"));
    }

    [Fact]
    public void An_empty_query_finds_everything()
    {
        Assert.True(RouterSearch.Matches(Wlan, A("SetEnable"), ""));
        Assert.True(RouterSearch.Matches(Wlan, A("SetEnable"), "   "));
    }

    [Fact]
    public void Non_matching_input_is_not_found()
    {
        Assert.False(RouterSearch.Matches(Wlan, A("SetEnable"), "telefonbuch"));
    }

    [Theory]
    [InlineData("WLANConfiguration", "funknetz")]
    [InlineData("Hosts", "heimnetz")]
    [InlineData("X_AVM-DE_HostFilter", "heimnetz")]
    [InlineData("WANIPConnection", "internet")]
    [InlineData("X_AVM-DE_Homeauto", "smarthome")]
    [InlineData("DeviceInfo", "system")]
    [InlineData("X_VoIP", "telefonie")]
    public void Services_land_in_the_right_area(string service, string range)
    {
        var s = new RouterServiceInfo(service, $"urn:x:service:{service}:1", "/x", "/x.xml", []);
        Assert.Equal(range, RouterSearch.Area(s));
    }

    [Fact]
    public void The_home_network_comes_before_telephony()
    {
        // The order is not a matter of taste: whoever opens the catalogue is
        // almost always looking for something on the home network and almost
        // never for a telephone number.
        Assert.True(RouterSearch.BereichsRang("heimnetz") < RouterSearch.BereichsRang("telefonie"));
    }
}

/// <summary>
/// TR-064 delivers bands as "2400" and encryption as "11iandWPA3". Both are
/// correct and unreadable to anybody — in a table "2400" stands there like a
/// measurement even though it is a category.
/// </summary>
public class RouterWlanDisplayTests
{
    private static RouterWlan W(string band = "2400", string security = "11i") =>
        new("/upnp/control/wlanconfig1", "Krypta", true, band, "13", security, false);

    /// <summary>
    /// Sets the language for the duration of one test.
    ///
    /// <para>
    /// Without this the test hung off the machine's culture: on a German
    /// computer “2,4 GHz” came out, on the build server “2.4 GHz”, and the
    /// test was green or red depending on where it ran. What the display
    /// text is hangs off the language — so the test has to name it too.
    /// </para>
    /// </summary>
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _before = CultureInfo.CurrentUICulture;

        public CultureScope(string culture)
        {
            var k = new CultureInfo(culture);
            CultureInfo.CurrentCulture = k;
            CultureInfo.CurrentUICulture = k;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _before;
            CultureInfo.CurrentUICulture = _before;
        }
    }

    [Theory]
    [InlineData("2400", "2,4 GHz", "2.4 GHz")]
    [InlineData("5000", "5 GHz", "5 GHz")]
    [InlineData("6000", "6 GHz", "6 GHz")]
    public void Bands_become_readable(string raw, string deutsch, string englisch)
    {
        using (new CultureScope("de-DE"))
        {
            Assert.Equal(deutsch, W(band: raw).BandLesbar);
        }
        using (new CultureScope("en-GB"))
        {
            Assert.Equal(englisch, W(band: raw).BandLesbar);
        }
    }

    [Fact]
    public void The_encryption_text_follows_the_language()
    {
        // "WPA2" is called that everywhere - the "and" in between is not.
        using (new CultureScope("de-DE"))
        {
            Assert.Equal("WPA2 und WPA3", W(security: "11iandWPA3").SecurityReadable);
            Assert.Equal("offen", W(security: "None").SecurityReadable);
        }
        using (new CultureScope("en-GB"))
        {
            Assert.Equal("WPA2 and WPA3", W(security: "11iandWPA3").SecurityReadable);
            Assert.Equal("open", W(security: "None").SecurityReadable);
        }
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    public void An_unknown_band_is_left_out_rather_than_shown(string raw)
    {
        // The guest network reports "unknown". "Band: unknown" in the table is
        // worse than no row at all.
        Assert.Null(W(band: raw).BandLesbar);
    }

    [Theory]
    [InlineData("11i", "WPA2")]
    [InlineData("11iandWPA3", "WPA2 und WPA3")]
    [InlineData("WPA3", "WPA3")]
    [InlineData("WPAand11i", "WPA und WPA2")]
    [InlineData("None", "offen")]
    [InlineData("Basic", "WEP (unsicher)")]
    public void Encryption_becomes_readable(string raw, string erwartet)
    {
        Assert.Equal(erwartet, W(security: raw).SecurityReadable);
    }

    [Theory]
    [InlineData("None", true)]
    [InlineData("Basic", true)]
    [InlineData("11i", false)]
    [InlineData("11iandWPA3", false)]
    public void Weak_encryption_is_recognised(string raw, bool erwartet)
    {
        Assert.Equal(erwartet, W(security: raw).SecurityWeak);
    }

    [Fact]
    public void An_unknown_beacon_type_is_passed_through_rather_than_swallowed()
    {
        // A future firmware may report something that is not listed here -
        // then the raw value is better than an empty field.
        Assert.Equal("WPA4undWasAuchImmer", W(security: "WPA4undWasAuchImmer").SecurityReadable);
    }

    [Fact]
    public void The_display_name_carries_the_band()
    {
        // Otherwise three networks called "Krypta" stand underneath each
        // other.
        using (new CultureScope("de-DE"))
        {
            Assert.Equal("Krypta · 2,4 GHz", W().DisplayName);
            Assert.Equal("Krypta", W(band: "unknown").DisplayName);
        }
        using (new CultureScope("en-GB"))
        {
            Assert.Equal("Krypta · 2.4 GHz", W().DisplayName);
        }
    }

    [Fact]
    public void The_instance_comes_from_the_control_URL()
    {
        Assert.Equal("wlanconfig1", W().Instance);
    }
}
