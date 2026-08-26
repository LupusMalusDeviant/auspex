using System.Net;
using Auspex.Control.Data;
using Auspex.Control.Services;
using Auspex.Control.Services.Geo;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Auspex.Control.Tests;

/// <summary>
/// Determining origin.
///
/// <para>
/// The dangerous error here is not the missing answer but the
/// <em>wrong</em> one. “8.8.8.8 belongs to Telekom” looks exactly as
/// plausible as the right answer, and nobody checks the arithmetic. So the
/// tests above all check the edges: the last value of a range, the first
/// one after it, and the gap in between.
/// </para>
/// </summary>
public class AddressSpaceTests
{
    [Theory]
    // As text "9.9.9.9" would be greater than "89.0.0.1" - exactly the
    // mistake a range search on strings makes.
    [InlineData("9.9.9.9", "89.0.0.1")]
    [InlineData("1.0.0.0", "1.0.0.1")]
    [InlineData("192.168.1.9", "192.168.1.10")]
    // IPv4 is embedded and therefore sits before every real IPv6 range.
    [InlineData("255.255.255.255", "2000::")]
    [InlineData("2001:db8::1", "2001:db8::2")]
    public void Addresses_sort_numerically(string smaller, string greater)
    {
        Assert.True(AddressSpace.AsNumber(smaller) < AddressSpace.AsNumber(greater),
            $"{smaller} should come before {greater}");
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.43")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("100.64.0.1")]           // CGNAT
    [InlineData("fd71:7881:a5f2::1")]    // Unique Local
    [InlineData("fe80::1")]              // Link-local
    [InlineData("::1")]
    public void Local_addresses_are_not_looked_up(string ip)
    {
        // Not merely thrift: information about our own router would be empty
        // at best and invented at worst.
        Assert.True(AddressSpace.IsPrivate(ip), $"{ip} ist privat");
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.32.0.1")]           // knapp ausserhalb von 172.16/12
    [InlineData("172.15.255.255")]       // knapp davor
    [InlineData("2001:4860:4860::8888")]
    public void Foreign_addresses_are_looked_up(string ip)
    {
        Assert.False(AddressSpace.IsPrivate(ip), $"{ip} is not private");
    }

    [Fact]
    public void The_same_address_appears_only_once_in_the_table()
    {
        // Without normalising these would be two destinations, and every
        // count would be wrong by exactly that difference.
        Assert.Equal(AddressSpace.Normalise("2001:0db8:0000::1"), AddressSpace.Normalise("2001:db8::1"));
    }

    [Theory]
    [InlineData("meine-domain.example")]   // CNAME-Ziel
    [InlineData("v=spf1 include:x -all")]  // TXT
    [InlineData("")]
    public void Whatever_is_not_an_address_drops_out(string value)
    {
        // The resolver puts everything the answer contained into answers.
        Assert.False(AddressSpace.IsAddress(value));
        Assert.Null(AddressSpace.Normalise(value));
    }
}

public class NetworkRangesTests
{
    /// <summary>
    /// A range database in a throwaway file.
    ///
    /// <para>
    /// The cleanup empties the connection pool. Microsoft.Data.Sqlite keeps
    /// connections open to reuse them — exactly right in production, but
    /// here the reason Windows refuses to delete the file even though
    /// nobody is working with it any more.
    /// </para>
    /// </summary>
    private sealed class AtTime : IDisposable
    {
        public NetworkRanges Ranges { get; }
        private readonly string _path;

        public AtTime()
        {
            _path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");
            Ranges = new NetworkRanges(_path,
                LoggerFactory.Create(b => { }).CreateLogger<NetworkRanges>());
            Ranges.Prepare();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { /* Wegwerfdatei */ }
        }
    }

    private static UInt128 Z(string ip) => AddressSpace.AsNumber(ip)!.Value;

    [Fact]
    public void A_range_hits_inside_and_not_beside()
    {
        using var f = new AtTime();
        var n = f.Ranges;
        n.Import([
            (Z("8.8.8.0"), Z("8.8.8.255"), 15169, "US", "GOOGLE"),
            (Z("1.1.1.0"), Z("1.1.1.255"), 13335, "US", "CLOUDFLARENET"),
        ]);

        Assert.Equal(15169, n.Lookup(Z("8.8.8.8"))?.Asn);
        // Die Raender gehoeren dazu.
        Assert.Equal(15169, n.Lookup(Z("8.8.8.0"))?.Asn);
        Assert.Equal(15169, n.Lookup(Z("8.8.8.255"))?.Asn);
        // One step beside it, no longer.
        Assert.Null(n.Lookup(Z("8.8.9.0")));
        // And the gap before it belongs to nobody - this is where the old
        // mistake showed: taking the last range before the address without
        // checking whether it reaches far enough.
        Assert.Null(n.Lookup(Z("5.5.5.5")));

        Assert.Equal("CLOUDFLARENET", n.Lookup(Z("1.1.1.1"))?.Operator);
    }

    [Fact]
    public void IPv4_and_IPv6_live_in_the_same_table()
    {
        using var f = new AtTime();
        var n = f.Ranges;
        n.Import([
            (Z("8.8.8.0"), Z("8.8.8.255"), 15169, "US", "GOOGLE"),
            (Z("2001:4860::"), Z("2001:4860:ffff:ffff:ffff:ffff:ffff:ffff"), 15169, "US", "GOOGLE"),
            (Z("2a00::"), Z("2a00:ffff:ffff:ffff:ffff:ffff:ffff:ffff"), 3320, "DE", "DTAG"),
        ]);

        Assert.Equal(15169, n.Lookup(Z("2001:4860:4860::8888"))?.Asn);
        Assert.Equal("DE", n.Lookup(Z("2a00:1234::1"))?.Country);
        // The IPv4 range must not be hidden by the IPv6 rows - that is
        // exactly what embedding IPv4 is for.
        Assert.Equal(15169, n.Lookup(Z("8.8.8.8"))?.Asn);
    }

    /// <summary>
    /// The error that would almost have made the city lookup useless.
    ///
    /// <para>
    /// IPv4 is embedded as <c>::ffff:a.b.c.d</c> and therefore lies
    /// numerically <em>inside</em> low IPv6 ranges. A range beginning at
    /// <c>::</c> and reaching far enough encloses every IPv4 address — and
    /// would ascribe an operator to it that has nothing to do with it.
    /// </para>
    /// </summary>
    [Fact]
    public void An_IPv6_range_does_not_claim_an_IPv4_address()
    {
        using var f = new AtTime();
        var n = f.Ranges;
        n.Import([
            // Numerically encloses every embedded IPv4 address.
            (Z("::"), Z("1fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff"), 64512, "ZZ", "IRGENDWER"),
            (Z("8.8.8.0"), Z("8.8.8.255"), 15169, "US", "GOOGLE"),
        ]);

        Assert.Equal("GOOGLE", n.Lookup(Z("8.8.8.8"))?.Operator);
        // And an IPv4 address without a range of its own gets no answer at
        // all - not the one from the IPv6 catch-all.
        Assert.Null(n.Lookup(Z("9.9.9.9")));
        // The catch-all still applies to genuine IPv6.
        Assert.Equal(64512, n.Lookup(Z("100::1"))?.Asn);
    }

    [Fact]
    public void Not_routed_is_not_information()
    {
        using var f = new AtTime();
        var n = f.Ranges;
        // The source fills gaps with AS 0 / "Not routed". Showing that as an
        // operator would be worse than nothing.
        n.Import([(Z("5.0.0.0"), Z("5.255.255.255"), 0, null, "Not routed")]);
        Assert.Null(n.Lookup(Z("5.5.5.5")));
    }

    /// <summary>
    /// A file from an older version is thrown away, not carried on with.
    ///
    /// <para>
    /// Exactly this happened on the running installation:
    /// <c>CREATE TABLE IF NOT EXISTS</c> left the existing table untouched,
    /// and every lookup afterwards ran into “no such column: v6”. The
    /// service caught the error and carried on — all that was visible was
    /// that nothing was being filled in any more.
    /// </para>
    /// </summary>
    [Fact]
    public void An_old_schema_is_thrown_away()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");
        try
        {
            // This is what the file looked like before the change: without a
            // v6 column.
            using (var c = new SqliteConnection($"Data Source={path}"))
            {
                c.Open();
                using var b = c.CreateCommand();
                b.CommandText = """
                    CREATE TABLE bereiche (von BLOB, bis BLOB, asn INTEGER,
                                           land TEXT, betreiber TEXT);
                    CREATE TABLE stand (quelle TEXT PRIMARY KEY, geholt TEXT, zeilen INTEGER);
                    INSERT INTO stand VALUES ('asn', '2026-01-01T00:00:00Z', 999);
                    """;
                b.ExecuteNonQuery();
            }

            var n = new NetworkRanges(path,
                LoggerFactory.Create(b => { }).CreateLogger<NetworkRanges>());
            n.Prepare();

            // The old state is gone - so the service fetches the data again
            // instead of searching a table that no longer exists in that
            // shape.
            Assert.Equal(0, n.State().Rows);
            Assert.Null(n.State().Fetched);

            // And the lookup works again instead of throwing.
            n.Import([(Z("8.8.8.0"), Z("8.8.8.255"), 15169, "US", "GOOGLE")]);
            Assert.Equal("GOOGLE", n.Lookup(Z("8.8.8.8"))?.Operator);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void Re_importing_replaces_the_old_state()
    {
        using var f = new AtTime();
        var n = f.Ranges;
        n.Import([(Z("8.8.8.0"), Z("8.8.8.255"), 15169, "US", "GOOGLE")]);
        // A range that no longer appears has been reassigned. Leaving it
        // there would mean giving an answer that is out of date.
        n.Import([(Z("1.1.1.0"), Z("1.1.1.255"), 13335, "US", "CLOUDFLARENET")]);

        Assert.Null(n.Lookup(Z("8.8.8.8")));
        Assert.Equal(13335, n.Lookup(Z("1.1.1.1"))?.Asn);
        Assert.Equal(1, n.State().Rows);
    }

    /// <summary>
    /// Before the first import there is not even a directory, and that is the
    /// normal case: the origin sources are opt-in.
    ///
    /// <para>
    /// SQLite creates a missing file on opening — but only when the folder
    /// around it exists. Otherwise the answer is
    /// <c>SQLite Error 14: unable to open database file</c>, and the settings
    /// page, whose whole job is to say that this part is missing, came down
    /// over it with a 500. An earlier version of this check only covered the
    /// missing table, which is one step later.
    /// </para>
    /// </summary>
    [Fact]
    public void Without_a_file_the_reading_paths_answer_instead_of_throwing()
    {
        var missing = Path.Combine(
            Path.GetTempPath(), "auspex-" + Guid.NewGuid().ToString("N"), "geo", "ranges.db");
        var n = new NetworkRanges(missing,
            LoggerFactory.Create(b => { }).CreateLogger<NetworkRanges>());

        Assert.Equal((null, 0L), n.State());
        Assert.Null(n.Lookup(Z("8.8.8.8")));
        Assert.Empty(n.Lookup([Z("8.8.8.8"), Z("1.1.1.1")]));

        // And nothing was laid down on the way: a read does not create a
        // folder.
        Assert.False(Directory.Exists(Path.GetDirectoryName(missing)));
    }
}

public class CityLookupTests
{
    [Fact]
    public void The_range_search_finds_the_right_place()
    {
        UInt128[] sorted = [10, 20, 30, 40];

        Assert.Equal(0, CityLookup.FirstFrom(sorted, 5));
        Assert.Equal(0, CityLookup.FirstFrom(sorted, 10));   // genau darauf
        Assert.Equal(1, CityLookup.FirstFrom(sorted, 11));
        Assert.Equal(3, CityLookup.FirstFrom(sorted, 40));
        Assert.Equal(4, CityLookup.FirstFrom(sorted, 41));   // past the end
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("2001:db8::1", false)]
    [InlineData("::1", false)]
    public void Embedded_IPv4_is_recognised_as_such(string ip, bool isV4)
    {
        // This decides which list a row is checked against - and with it
        // whether the city is kept or overwritten.
        Assert.Equal(isV4, CityLookup.IsEmbeddedV4(AddressSpace.AsNumber(ip)!.Value));
    }

    [Theory]
    // continent, country, region, city, latitude, longitude
    [InlineData("OC,AU,Queensland,\"South Brisbane\",-27.4,153.0", "AU", "South Brisbane")]
    [InlineData("EU,DE,Hessen,Frankfurt am Main,50.1,8.6", "DE", "Frankfurt am Main")]
    // A comma INSIDE the field - hence the quotes, and hence you must not
    // bluntly split at every comma.
    [InlineData("NA,US,\"District of Columbia\",\"Washington, D.C.\",38.9,-77.0", "US", "Washington, D.C.")]
    // ZZ means unknown in the source.
    [InlineData("ZZ,ZZ,,,0,0", null, null)]
    public void Country_and_city_are_extracted_correctly(
        string rest, string? country, string? city)
    {
        var place = CityLookup.PlaceFrom(rest);
        Assert.Equal(country, place.Country);
        Assert.Equal(city, place.City);
    }
}

public class AnycastTests
{
    [Theory]
    [InlineData("CLOUDFLARENET")]
    [InlineData("GOOGLE")]
    [InlineData("AKAMAI-AS")]
    [InlineData("AMAZON-02")]
    [InlineData("FASTLY")]
    public void Large_distribution_networks_count_as_unsafe(string carrier)
    {
        // Not because the value is wrong, but because it names the place of
        // ONE node - usually the nearest one. A map would turn that into a
        // company headquarters.
        Assert.True(GeoService.LooksAnycast(new Destination { Operator = carrier }));
    }

    [Theory]
    [InlineData("DTAG Internet service provider operations")]
    [InlineData("Vodafone GmbH")]
    // A data-centre operator, not a distribution network: an address at
    // Hetzner really is in Falkenstein. Sowing doubt where the value is right
    // devalues the marker where it is needed.
    [InlineData("HETZNER-AS")]
    [InlineData("OVH SAS")]
    [InlineData(null)]
    public void An_ordinary_provider_is_not(string? carrier)
    {
        Assert.False(GeoService.LooksAnycast(new Destination { Operator = carrier }));
    }
}

public class DestinationCaptureTests
{
    private static QueryLogEntry E(string name, string domain, params string[] answers) =>
        new(1, new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero), "192.168.1.20",
            "PC", null, name, domain, "A", "allowed", "upstream", null, null, null, null,
            null, null, "NOERROR", false, answers, 1.0, null);

    [Fact]
    public async Task Only_real_addresses_land_as_a_destination()
    {
        using var f = new TestDb();

        await DestinationCapture.RecordAsync(f.Db, [
            // A CNAME chain: the resolver stores the target as text.
            E("bilder.example", "example", "cdn.anbieter.example", "93.184.216.34"),
            E("text.example", "example", "v=spf1 -all"),
        ], default);
        await f.Db.SaveChangesAsync();

        var destinations = f.Db.Destinations.Select(z => z.Ip).ToList();
        Assert.Equal(["93.184.216.34"], destinations);
    }

    [Fact]
    public async Task The_same_mapping_is_counted_rather_than_duplicated()
    {
        using var f = new TestDb();

        // Zweimal im selben Stapel ...
        await DestinationCapture.RecordAsync(f.Db, [
            E("a.example", "example", "1.2.3.4"),
            E("a.example", "example", "1.2.3.4"),
        ], default);
        await f.Db.SaveChangesAsync();

        // ... and once more in the next one.
        await DestinationCapture.RecordAsync(f.Db, [E("a.example", "example", "1.2.3.4")], default);
        await f.Db.SaveChangesAsync();

        var a = Assert.Single(f.Db.Resolutions);
        Assert.Equal(3, a.Count);
        Assert.Single(f.Db.Destinations);
    }

    [Fact]
    public async Task Local_addresses_are_recorded_as_private()
    {
        using var f = new TestDb();

        await DestinationCapture.RecordAsync(f.Db, [
            E("fritz.box", "fritz.box", "192.168.1.1"),
            E("fremd.example", "example", "8.8.8.8"),
        ], default);
        await f.Db.SaveChangesAsync();

        Assert.True(f.Db.Destinations.Single(z => z.Ip == "192.168.1.1").IsPrivate);
        Assert.False(f.Db.Destinations.Single(z => z.Ip == "8.8.8.8").IsPrivate);
    }

    [Fact]
    public async Task A_name_with_several_addresses_yields_several_mappings()
    {
        using var f = new TestDb();

        // Exactly the normal case with large providers: one answer, eight
        // addresses.
        await DestinationCapture.RecordAsync(f.Db, [
            E("www.example", "example", "1.2.3.4", "1.2.3.5", "1.2.3.6"),
        ], default);
        await f.Db.SaveChangesAsync();

        Assert.Equal(3, f.Db.Resolutions.Count());
        Assert.Equal(3, f.Db.Destinations.Count());
    }
}
