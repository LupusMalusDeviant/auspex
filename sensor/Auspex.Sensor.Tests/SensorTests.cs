using System.Buffers.Binary;
using System.Text.Json;
using System.Net;
using Auspex.Sensor;

namespace Auspex.Sensor.Tests;

/// <summary>
/// Reading the connection table.
///
/// <para>
/// Checked against a hand-built buffer, not against Windows. Two reasons:
/// the tests then run everywhere, including on the Linux build server — and
/// a parsing mistake is exactly the kind you do not see on a real system. A
/// misread port number produces a plausible figure, and nobody works out
/// that 46853 in network order means 443.
/// </para>
/// </summary>
public class ConnectionTableTests
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int Established = 5;

    /// <summary>Builds a MIB_TCPTABLE_OWNER_PID with IPv4 rows.</summary>
    private static byte[] Table4(params (int State, string Local, int LocalPort,
        string Remote, int Port, int Pid)[] lines)
    {
        var buffer = new byte[4 + (lines.Length * 24)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, lines.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            var z = buffer.AsSpan(4 + (i * 24), 24);
            var (state, local, localPort, against, port, pid) = lines[i];

            BinaryPrimitives.WriteInt32LittleEndian(z[..4], state);
            IPAddress.Parse(local).GetAddressBytes().CopyTo(z[4..8]);
            Port(z[8..12], localPort);
            IPAddress.Parse(against).GetAddressBytes().CopyTo(z[12..16]);
            Port(z[16..20], port);
            BinaryPrimitives.WriteInt32LittleEndian(z[20..24], pid);
        }
        return buffer;
    }

    /// <summary>Builds a MIB_TCP6TABLE_OWNER_PID.</summary>
    private static byte[] Table6(params (int State, string Local, int LocalPort,
        string Remote, int Port, int Pid)[] lines)
    {
        var buffer = new byte[4 + (lines.Length * 56)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, lines.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            var z = buffer.AsSpan(4 + (i * 56), 56);
            var (state, local, localPort, against, port, pid) = lines[i];

            IPAddress.Parse(local).GetAddressBytes().CopyTo(z[..16]);
            Port(z[20..24], localPort);
            IPAddress.Parse(against).GetAddressBytes().CopyTo(z[24..40]);
            Port(z[44..48], port);
            BinaryPrimitives.WriteInt32LittleEndian(z[48..52], state);
            BinaryPrimitives.WriteInt32LittleEndian(z[52..56], pid);
        }
        return buffer;
    }

    /// <summary>Network order in the low two bytes of a DWORD.</summary>
    private static void Port(Span<byte> four, int port)
    {
        four[0] = (byte)((port >> 8) & 0xFF);
        four[1] = (byte)(port & 0xFF);
    }

    [Fact]
    public void An_IPv4_row_is_read_correctly()
    {
        var destination = new List<OpenConnection>();
        ConnectionTable.Parse(
            Table4((Established, "192.168.1.43", 51234, "140.82.121.5", 443, 4711)),
            AfInet, destination);

        var v = Assert.Single(destination);
        Assert.Equal(4711, v.Pid);
        Assert.Equal(51234, v.LocalPort);
        Assert.Equal("140.82.121.5", v.Remote.ToString());
        // The port is the value where network order takes its revenge.
        Assert.Equal(443, v.Port);
    }

    [Fact]
    public void An_IPv6_row_is_read_correctly()
    {
        var destination = new List<OpenConnection>();
        ConnectionTable.Parse(
            Table6((Established, "2001:db8::1", 51234, "2607:6bc0::10", 443, 4711)),
            AfInet6, destination);

        var v = Assert.Single(destination);
        Assert.Equal(4711, v.Pid);
        Assert.Equal("2607:6bc0::10", v.Remote.ToString());
        Assert.Equal(443, v.Port);
    }

    [Fact]
    public void The_local_address_is_kept()
    {
        // It was discarded while reading, so it was missing from the row that
        // the byte counters use to name a connection. Windows did not find
        // it, the call failed, the column stayed empty - and an empty column
        // looks like "not counted".
        var destination = new List<OpenConnection>();
        ConnectionTable.Parse(
            Table4((Established, "192.168.1.43", 51234, "140.82.121.5", 443, 4711)),
            AfInet, destination);

        Assert.Equal("192.168.1.43", Assert.Single(destination).Local.ToString());
    }

    [Fact]
    public void The_local_address_is_kept_for_IPv6_too()
    {
        var destination = new List<OpenConnection>();
        ConnectionTable.Parse(
            Table6((Established, "2001:db8::1", 51234, "2607:6bc0::10", 443, 4711)),
            AfInet6, destination);

        Assert.Equal("2001:db8::1", Assert.Single(destination).Local.ToString());
    }

    [Fact]
    public void Only_established_connections_count()
    {
        var destination = new List<OpenConnection>();
        ConnectionTable.Parse(
            Table4(
                (Established, "192.168.1.43", 1, "1.2.3.4", 443, 1),
                (2 /* LISTEN */, "0.0.0.0", 445, "0.0.0.0", 0, 2),
                (4 /* SYN_SENT */, "192.168.1.43", 3, "5.6.7.8", 443, 3)),
            AfInet, destination);

        // A listening port is not a connection going out, and a setup that has
        // only begun is not one yet either.
        var v = Assert.Single(destination);
        Assert.Equal("1.2.3.4", v.Remote.ToString());
    }

    [Fact]
    public void Loopback_stays_out()
    {
        var destination = new List<OpenConnection>();
        ConnectionTable.Parse(
            Table4(
                (Established, "127.0.0.1", 1, "127.0.0.1", 5000, 1),
                // The local network stays in: the router is a destination.
                (Established, "192.168.1.43", 2, "192.168.1.1", 53, 2)),
            AfInet, destination);

        var v = Assert.Single(destination);
        Assert.Equal("192.168.1.1", v.Remote.ToString());
    }

    [Fact]
    public void A_truncated_buffer_does_not_throw()
    {
        // If this ever happened it would be a bug in the size arithmetic - but
        // a crash in the sensor would be the worse answer to it.
        var full = Table4((Established, "192.168.1.43", 1, "1.2.3.4", 443, 1));
        var destination = new List<OpenConnection>();

        ConnectionTable.Parse(full.AsSpan(0, 12), AfInet, destination);
        Assert.Empty(destination);
    }
}

/// <summary>
/// Counting.
///
/// <para>
/// The connection table is a snapshot. Read as a stream of events, it counts
/// every standing connection again on every poll — and reports a thousand
/// connections after an hour where there was one.
/// </para>
/// </summary>
public class LedgerTests
{
    private static readonly Dictionary<int, string> Names = new() { [1] = "vivaldi", [2] = "steam" };

    private static OpenConnection V(int pid, int localPort, string against, int port) =>
        new(pid, IPAddress.Parse("192.168.1.43"), localPort, IPAddress.Parse(against), port);

    [Fact]
    public void A_standing_connection_is_counted_once()
    {
        var ledger = new Ledger(TimeProvider.System);
        var open = new List<OpenConnection> { V(1, 5000, "140.82.121.5", 443) };

        // The same snapshot three times.
        ledger.Record(open, Names);
        ledger.Record(open, Names);
        ledger.Record(open, Names);

        var b = Assert.Single(ledger.Collect());
        Assert.Equal(1, b.Count);
        Assert.Equal("vivaldi", b.Process);
    }

    [Fact]
    public void Every_new_setup_counts()
    {
        var ledger = new Ledger(TimeProvider.System);

        // Different local ports means: different connections to the same target.
        ledger.Record([V(1, 5000, "1.2.3.4", 443)], Names);
        ledger.Record([V(1, 5000, "1.2.3.4", 443), V(1, 5001, "1.2.3.4", 443)], Names);
        ledger.Record([V(1, 5002, "1.2.3.4", 443)], Names);

        var b = Assert.Single(ledger.Collect());
        Assert.Equal(3, b.Count);
    }

    [Fact]
    public void Two_programs_stay_separate()
    {
        var ledger = new Ledger(TimeProvider.System);
        ledger.Record([
            V(1, 5000, "1.2.3.4", 443),
            V(2, 5001, "1.2.3.4", 443),
        ], Names);

        var all = ledger.Collect();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, b => b.Process == "vivaldi");
        Assert.Contains(all, b => b.Process == "steam");
    }

    [Fact]
    public void Without_a_program_name_nothing_is_reported()
    {
        var ledger = new Ledger(TimeProvider.System);
        // "Process 4711 is talking to Google" does not answer the question.
        ledger.Record([V(99, 5000, "1.2.3.4", 443)], Names);
        Assert.Empty(ledger.Collect());
    }

    [Fact]
    public void Collecting_starts_over()
    {
        var ledger = new Ledger(TimeProvider.System);
        ledger.Record([V(1, 5000, "1.2.3.4", 443)], Names);
        Assert.Single(ledger.Collect());

        // The same connection is still there - but nothing new has happened,
        // so there is nothing to report.
        ledger.Record([V(1, 5000, "1.2.3.4", 443)], Names);
        Assert.Empty(ledger.Collect());
    }

    [Fact]
    public void Bytes_are_added_up_when_they_are_there()
    {
        var ledger = new Ledger(TimeProvider.System);
        var v = V(1, 5000, "1.2.3.4", 443);
        var delta = new Dictionary<(int, int, string, int), (long, long)>
        {
            [v.Key] = (100, 200),
        };

        ledger.Record([v], Names, delta);
        ledger.Record([v], Names, delta);

        var b = Assert.Single(ledger.Collect());
        Assert.Equal(200, b.BytesOut);
        Assert.Equal(400, b.BytesIn);
    }

    [Fact]
    public void Without_byte_counters_the_column_stays_empty()
    {
        var ledger = new Ledger(TimeProvider.System);
        ledger.Record([V(1, 5000, "1.2.3.4", 443)], Names);

        var b = Assert.Single(ledger.Collect());
        // Null means "not counted", not "nothing flowed" - a zero would look
        // like a measurement.
        Assert.Null(b.BytesOut);
        Assert.Null(b.BytesIn);
    }
}

/// <summary>
/// The settings.
///
/// <para>
/// These tests exist because of a fault that breaks nothing and shifts
/// everything: an initialiser like <c>= true</c> on the field does not
/// survive deserialisation when the key is missing from the file. The sensor
/// then considered itself switched off where it should have been counting —
/// and would have reported fifteen times as often as documented, with
/// nothing anywhere saying so.
/// </para>
/// </summary>
public class SettingsTests
{
    [Fact]
    public void The_defaults_hold_even_with_no_entry()
    {
        var e = Settings.FromText("""
            { "basis": "http://192.168.1.61:5390", "zeichen": "abc" }
            """);

        Assert.Equal(2, e.PollSeconds);
        Assert.Equal(30, e.ReportSeconds);
        Assert.True(e.Bytes);
        Assert.False(e.Verbose);
        Assert.True(e.Complete);
    }

    [Fact]
    public void What_is_written_applies()
    {
        var e = Settings.FromText("""
            {
              "basis": "http://192.168.1.61:5390/",
              "zeichen": "abc",
              "abfrageSekunden": 5,
              "meldungSekunden": 60,
              "bytes": false,
              "laut": true
            }
            """);

        Assert.Equal(5, e.PollSeconds);
        Assert.Equal(60, e.ReportSeconds);
        Assert.False(e.Bytes);
        Assert.True(e.Verbose);
        // The trailing slash has to go, otherwise the address later becomes
        // ".../api/ext/verbindungen" with a doubled separator.
        Assert.Equal("http://192.168.1.61:5390", e.BaseUrl);
    }

    [Fact]
    public void Without_a_file_the_essentials_are_missing()
    {
        var e = Settings.FromText(null);
        Assert.False(e.Complete);
        // And the defaults still hold.
        Assert.Equal(2, e.PollSeconds);
        Assert.True(e.Bytes);
    }

    [Fact]
    public void Broken_JSON_does_not_throw()
    {
        var e = Settings.FromText("{ this is not JSON");
        Assert.False(e.Complete);
    }
}

public class ByteCounterTests
{
    /// <summary>
    /// The value itself is the whole statement, which is why it is nailed
    /// down here: TCP_ESTATS_TYPE starts counting at SynOpts, Data is the
    /// second entry. With a 0 here Windows rejected every enable with 1784 -
    /// SynOpts has no read-write structure - and the byte column stayed
    /// empty, without anything about the call looking wrong.
    /// </summary>
    [Fact]
    public void The_ESTATS_type_is_Data_and_not_SynOpts()
    {
        Assert.Equal(1, ByteCounter.EstatsData);
    }
}

/// <summary>
/// The keys in sensor.json were German up to version 0.9.
///
/// <para>
/// A file already sitting on a machine only knows the old names. If they
/// were no longer read, the sensor would find neither address nor token
/// after an update - and would report "address and token are missing" for
/// something that is right there.
/// </para>
/// </summary>
public class LegacyKeyTests
{
    [Fact]
    public void The_new_keys_apply()
    {
        var e = Settings.FromText("""
            { "base": "http://d.example:5390", "token": "abc" }
            """);
        Assert.Equal("http://d.example:5390", e.BaseUrl);
        Assert.Equal("abc", e.Token);
        Assert.True(e.Complete);
    }

    [Fact]
    public void The_old_keys_still_apply()
    {
        var e = Settings.FromText("""
            { "basis": "http://d.example:5390", "zeichen": "abc" }
            """);
        Assert.Equal("http://d.example:5390", e.BaseUrl);
        Assert.Equal("abc", e.Token);
        Assert.True(e.Complete);
    }

    [Fact]
    public void When_both_are_there_the_new_one_wins()
    {
        var e = Settings.FromText("""
            {
              "base": "http://neu.example:5390", "basis": "http://alt.example:5390",
              "token": "neu", "zeichen": "alt"
            }
            """);
        Assert.Equal("http://neu.example:5390", e.BaseUrl);
        Assert.Equal("neu", e.Token);
    }
}

/// <summary>
/// The other half of the contract with the control plane.
///
/// <para>
/// The control plane pins the same JSON in
/// <c>SensorApiTests.WhatTheSensorSends</c> and binds it into its own record.
/// Here we check that this is really what goes out. Rename a field on one
/// side and exactly one of the two tests goes red — which is more than
/// happened last time: up to 0.9.0 the sensor sent <c>verbindungen</c> and
/// <c>prozess</c> to an endpoint that had long been binding <c>Connections</c>
/// and <c>Process</c>, and every unit test on both sides stayed green.
/// </para>
/// </summary>
public class WireFormatTests
{
    [Fact]
    public void The_wire_format_is_what_the_control_plane_binds()
    {
        var batch = new ReportBatch([
            new ReportItem(
                Process: "chrome",
                Destination: "140.82.121.4",
                Port: 443,
                Protocol: "tcp",
                Count: 3,
                First: new DateTimeOffset(2026, 8, 25, 15, 0, 0, TimeSpan.Zero),
                Last: new DateTimeOffset(2026, 8, 25, 15, 0, 30, TimeSpan.Zero),
                BytesOut: 1234,
                BytesIn: 5678),
        ]);

        var json = JsonSerializer.Serialize(batch, SensorJson.Default.ReportBatch);

        // The field names, one by one. Not a comparison of the whole string:
        // the order and the date format are the serialiser's business, the
        // names are the contract.
        foreach (var field in new[]
        {
            "\"connections\"", "\"process\"", "\"destination\"", "\"port\"",
            "\"protocol\"", "\"count\"", "\"first\"", "\"last\"",
            "\"bytesOut\"", "\"bytesIn\"",
        })
        {
            Assert.Contains(field, json);
        }

        // And nothing German left over from before.
        foreach (var old in new[] { "verbindungen", "prozess", "ziel", "protokoll", "anzahl" })
        {
            Assert.DoesNotContain(old, json);
        }
    }

    /// <summary>
    /// The answer travels the other way, and the sensor reads two fields out
    /// of it. It only prints them, so a mismatch would not break anything —
    /// but "First report arrived: 0 relations" after a successful report is
    /// exactly the kind of wrong number that sends somebody looking in the
    /// wrong place.
    /// </summary>
    [Fact]
    public void The_answer_is_read_under_the_names_the_endpoint_writes()
    {
        var reply = JsonSerializer.Deserialize(
            """{"accepted":7,"device":"Arbeitsrechner"}""",
            SensorJson.Default.ReportReply);

        Assert.Equal(7, reply!.Applied);
        Assert.Equal("Arbeitsrechner", reply.Device);
    }
}
