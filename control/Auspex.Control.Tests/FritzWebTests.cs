using System.Reflection;
using Auspex.Control.Services.Router;

namespace Auspex.Control.Tests;

/// <summary>
/// Parsing the IPv4 form is the most dangerous place in the whole project:
/// the same form carries the box's address, the subnet mask, the DHCP range
/// and the lease time. Read it wrongly and send it back, and the home
/// network is down — in a way that leaves nobody able to reach the
/// interface to undo it.
/// </summary>
public class FritzWebTests
{
    // An excerpt from the real page of a FRITZ!Box 5690 Pro.
    private const string Page = """
        <form method="POST" action="/net/boxnet.lua" name="main_form">
        <input type="checkbox" name="LanBridge" value="1">
        <input type="text" name="Ip_all0" value="192" maxlength="3">
        <input type="text" name="Ip_all1" value="168">
        <input type="text" name="Ip_all2" value="1">
        <input type="text" name="Ip_all3" value="1">
        <input type="text" name="Netmask_all0" value="255">
        <input type="text" name="Netmask_all1" value="255">
        <input type="text" name="Netmask_all2" value="255">
        <input type="text" name="Netmask_all3" value="0">
        <input type="checkbox" name="Dhcp_all" value="1" checked>
        <input type="text" name="Start_all0" value="192">
        <input type="text" name="Start_all1" value="168">
        <input type="text" name="Start_all2" value="1">
        <input type="text" name="Start_all3" value="20">
        <input type="text" name="End_all0" value="192">
        <input type="text" name="End_all1" value="168">
        <input type="text" name="End_all2" value="1">
        <input type="text" name="End_all3" value="200">
        <input type="text" name="lease_time" value="10">
        <label>Lokaler DNS-Server:</label>
        <input type="text" name="Dns_all0" value="192">
        <input type="text" name="Dns_all1" value="168">
        <input type="text" name="Dns_all2" value="1">
        <input type="text" name="Dns_all3" value="1">
        <input type="hidden" name="sid" value="e9b2028262654970">
        <input type="hidden" name="back_to_page" value="netSet">
        </form>
        """;

    // Reading is private and static - which is right, but it still has to
    // be checked.
    private static Ipv4Settings? Read(string html) =>
        (Ipv4Settings?)typeof(FritzWebClient)
            .GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [html]);

    [Fact]
    public void The_local_DNS_server_is_read()
    {
        var e = Read(Page);
        Assert.NotNull(e);
        Assert.Equal("192.168.1.1", e.LocalDns);
    }

    [Fact]
    public void The_remaining_values_all_come_along()
    {
        var e = Read(Page)!;
        Assert.Equal("192.168.1.1", e.BoxAddress);
        Assert.Equal("255.255.255.0", e.SubnetMask);
        Assert.Equal("192.168.1.20", e.DhcpFrom);
        Assert.Equal("192.168.1.200", e.DhcpTo);
        Assert.Equal("10", e.LeaseDays);
    }

    [Fact]
    public void A_ticked_checkbox_is_taken_over()
    {
        var e = Read(Page)!;
        Assert.True(e.DhcpOn);
        Assert.True(e.AllFields.ContainsKey("Dhcp_all"));
    }

    [Fact]
    public void An_unticked_checkbox_is_not_sent_at_all()
    {
        // This is the most dangerous single case: a form does not send
        // unticked checkboxes. If Auspex sent them along, sending the form
        // back would switch on things that were off - a LAN bridge that
        // rebuilds the network, say.
        var e = Read(Page)!;
        Assert.False(e.AllFields.ContainsKey("LanBridge"));
    }

    [Fact]
    public void A_switched_off_DHCP_server_is_recognised()
    {
        var without = Page.Replace("name=\"Dhcp_all\" value=\"1\" checked", "name=\"Dhcp_all\" value=\"1\"");
        var e = Read(without)!;
        Assert.False(e.DhcpOn);
        Assert.False(e.AllFields.ContainsKey("Dhcp_all"));
    }

    [Fact]
    public void Every_field_survives_for_sending_back()
    {
        var e = Read(Page)!;
        // Whatever was read must be able to go back out again.
        Assert.Contains("Ip_all0", e.AllFields.Keys);
        Assert.Contains("Netmask_all3", e.AllFields.Keys);
        Assert.Contains("lease_time", e.AllFields.Keys);
        Assert.Contains("back_to_page", e.AllFields.Keys);
        Assert.Equal("10", e.AllFields["lease_time"]);
    }

    [Fact]
    public void A_foreign_page_is_rejected_rather_than_guessed_at()
    {
        // If a firmware changes the field names, a half-understood form must
        // on no account be submitted.
        Assert.Null(Read("<form><input type=\"text\" name=\"Irgendwas\" value=\"1\"></form>"));
        Assert.Null(Read(""));
        Assert.Null(Read("<html>Anmeldung erforderlich</html>"));
    }

    [Fact]
    public void A_partly_matching_form_is_rejected_as_well()
    {
        // Three of the four DNS fields are not enough: an address nobody
        // meant could be assembled from those.
        var kaputt = Page.Replace("<input type=\"text\" name=\"Dns_all3\" value=\"1\">", "");
        Assert.Null(Read(kaputt));
    }

    [Fact]
    public void Whether_the_box_points_at_itself_is_recognised()
    {
        Assert.True(Read(Page)!.PointsAtTheBox);

        var umgestellt = Page
            .Replace("name=\"Dns_all3\" value=\"1\"", "name=\"Dns_all3\" value=\"61\"");
        var e = Read(umgestellt)!;
        Assert.Equal("192.168.1.61", e.LocalDns);
        Assert.False(e.PointsAtTheBox);
    }

    [Fact]
    public void Upper_case_in_the_attributes_does_not_matter()
    {
        var other = Page.Replace("type=\"text\"", "TYPE=\"text\"").Replace("name=", "NAME=");
        Assert.NotNull(Read(other));
    }

    [Fact]
    public void The_target_comes_from_the_form()
    {
        // Hard-wired it was wrong: the post initially went to data.lua,
        // where the box accepts the call and silently discards it. It only
        // came to light because the value is read back after sending.
        Assert.Equal("/net/boxnet.lua", Read(Page)!.Destination);
    }

    [Fact]
    public void A_form_without_a_target_is_rejected()
    {
        var without = Page.Replace(" action=\"/net/boxnet.lua\"", "");
        Assert.Null(Read(without));
    }

    [Fact]
    public void A_moved_target_is_taken_over_rather_than_ignored()
    {
        // If a firmware moves the address, Auspex follows it - that is what
        // reading it out is for in the first place.
        var other = Page.Replace("/net/boxnet.lua", "/net/ip_settings_v2.lua");
        Assert.Equal("/net/ip_settings_v2.lua", Read(other)!.Destination);
    }
}

/// <summary>
/// The Fritz!Box accepts a change to network settings but only carries it
/// out after a confirmation on the device itself. Without recognising that,
/// it looks like a silent failure: the call succeeds, the value stays as it
/// was, and nobody knows why.
/// </summary>
public class SecondFactorTests
{
    private static string? Erkennen(string reply) =>
        (string?)typeof(FritzWebClient)
            .GetMethod("SecondFactor", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [reply]);

    [Fact]
    public void The_offered_routes_are_named()
    {
        // Wortlaut einer FRITZ!Box 5690 Pro.
        var real = """{"data":{"twofactor":"button,googleauth,dtmf;0510","apply":"twofactor"}}""";
        var t = Erkennen(real);
        Assert.NotNull(t);
        Assert.Contains("Taste an der Box", t);
        Assert.Contains("Authenticator", t);
        Assert.Contains("Telefon", t);
        // Not twice: the opening sentence already mentions the confirmation.
        Assert.DoesNotContain("Bestätigung am Gerät (", t);
    }

    [Fact]
    public void A_refused_request_is_reported_as_such()
    {
        // After several attempts in quick succession the box answers like
        // this. "Did not take the value" would be the least useful of all
        // true statements here.
        var t = Erkennen("""{"data":{"twofactor":"starterror;92","apply":"twofactor"}}""");
        Assert.NotNull(t);
        Assert.Contains("abgewiesen", t);
        Assert.Contains("92", t);
        Assert.Contains("Warte", t);
    }

    [Fact]
    public void An_unknown_route_gets_a_generic_description()
    {
        var t = Erkennen("""{"twofactor":"irgendwasneues"}""");
        Assert.NotNull(t);
        Assert.Contains("Bestätigung am Gerät", t);
    }

    [Fact]
    public void An_ordinary_answer_triggers_nothing()
    {
        Assert.Null(Erkennen("""{"data":{"apply":"ok"}}"""));
        Assert.Null(Erkennen(""));
        Assert.Null(Erkennen("<html>irgendwas</html>"));
    }
}
