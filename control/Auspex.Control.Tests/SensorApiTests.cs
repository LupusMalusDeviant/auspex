using System.Text.Json;
using Auspex.Control.Services.Extension;

namespace Auspex.Control.Tests;

/// <summary>
/// What the sensor reports gets folded together — and that is where you can
/// miscount.
///
/// <para>
/// The sensor sends relationships, not events. The same key can occur
/// several times in one batch and comes back in every further batch. Fold
/// that wrongly and you get either a violated index or a number that starts
/// again at one with every batch.
/// </para>
/// </summary>
public class SensorApiTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 25, 15, 0, 0, TimeSpan.Zero);

    private static ConnectionReport M(
        string process = "vivaldi",
        string destination = "140.82.121.5",
        int port = 443,
        long count = 1,
        int minuten = 0,
        long? outbound = null,
        long? inbound = null) =>
        new(process, destination, port, "tcp", count,
            Now.AddMinutes(minuten), Now.AddMinutes(minuten), outbound, inbound);

    [Fact]
    public async Task A_batch_is_accepted()
    {
        using var f = new TestDb();

        await SensorApi.ApplyAsync(f.Db, "192.168.1.43", "Arbeitsrechner",
            [M(), M(process: "steam", destination: "162.254.198.69", port: 27022)], default);

        Assert.Equal(2, f.Db.Connections.Count());
        var v = f.Db.Connections.Single(x => x.Process == "vivaldi");
        Assert.Equal("Arbeitsrechner", v.Device);
        Assert.Equal(443, v.Port);
        Assert.Equal("tcp", v.Protocol);
    }

    [Fact]
    public async Task The_same_key_in_the_same_batch_is_folded()
    {
        using var f = new TestDb();

        // Two rows with the same key would violate the unique index - they
        // have to collapse BEFORE writing.
        await SensorApi.ApplyAsync(f.Db, "192.168.1.43", "PC",
            [M(count: 3), M(count: 4)], default);

        var v = Assert.Single(f.Db.Connections);
        Assert.Equal(7, v.Count);
    }

    [Fact]
    public async Task Across_batches_it_is_carried_forward()
    {
        using var f = new TestDb();

        await SensorApi.ApplyAsync(f.Db, "192.168.1.43", "PC", [M(count: 2)], default);
        await SensorApi.ApplyAsync(f.Db, "192.168.1.43", "PC",
            [M(count: 5, minuten: 10)], default);

        var v = Assert.Single(f.Db.Connections);
        Assert.Equal(7, v.Count);
        // The period grows at the back, not at the front.
        Assert.Equal(Now.UtcDateTime, v.FirstUtc);
        Assert.Equal(Now.AddMinutes(10).UtcDateTime, v.LastUtc);
    }

    [Fact]
    public async Task Two_devices_stay_separate()
    {
        using var f = new TestDb();

        // The same program on two computers are two relationships. Without
        // the client in the key, one would carry on the other's numbers.
        await SensorApi.ApplyAsync(f.Db, "192.168.1.43", "PC", [M(count: 2)], default);
        await SensorApi.ApplyAsync(f.Db, "192.168.1.50", "Laptop", [M(count: 3)], default);

        Assert.Equal(2, f.Db.Connections.Count());
        Assert.Equal(2, f.Db.Connections.Single(v => v.Device == "PC").Count);
        Assert.Equal(3, f.Db.Connections.Single(v => v.Device == "Laptop").Count);
    }

    [Fact]
    public async Task Unusable_messages_drop_out()
    {
        using var f = new TestDb();

        await SensorApi.ApplyAsync(f.Db, "192.168.1.43", "PC", [
            M(destination: "this-is-not-an-address"),
            M(process: ""),
            M(port: 99999),
            M(destination: "1.2.3.4"),
        ], default);

        var v = Assert.Single(f.Db.Connections);
        Assert.Equal("1.2.3.4", v.Destination);
    }

    [Fact]
    public async Task The_address_is_normalised()
    {
        using var f = new TestDb();

        // Otherwise 2001:0db8::1 and 2001:db8::1 would stand there as two
        // destinations.
        await SensorApi.ApplyAsync(f.Db, "192.168.1.43", "PC",
            [M(destination: "2001:0db8:0000::1"), M(destination: "2001:db8::1")], default);

        var v = Assert.Single(f.Db.Connections);
        Assert.Equal(2, v.Count);
    }

    [Fact]
    public async Task Without_byte_counters_the_column_stays_empty()
    {
        using var f = new TestDb();
        await SensorApi.ApplyAsync(f.Db, "192.168.1.43", "PC", [M()], default);

        // Null means "not counted". A zero would look like a measurement and
        // would claim "this program sends nothing".
        Assert.Null(Assert.Single(f.Db.Connections).BytesOut);
    }

    [Fact]
    public async Task Byte_counters_add_up()
    {
        using var f = new TestDb();

        await SensorApi.ApplyAsync(f.Db, "192.168.1.43", "PC",
            [M(outbound: 100, inbound: 200)], default);
        await SensorApi.ApplyAsync(f.Db, "192.168.1.43", "PC",
            [M(outbound: 50, inbound: 70)], default);

        var v = Assert.Single(f.Db.Connections);
        Assert.Equal(150, v.BytesOut);
        Assert.Equal(270, v.BytesIn);
    }

    [Fact]
    public async Task A_counter_starting_later_loses_nothing()
    {
        using var f = new TestDb();

        // First without privileges, then with: the column must not be smaller
        // afterwards than it was before.
        await SensorApi.ApplyAsync(f.Db, "192.168.1.43", "PC", [M()], default);
        await SensorApi.ApplyAsync(f.Db, "192.168.1.43", "PC",
            [M(outbound: 500, inbound: 900)], default);

        var v = Assert.Single(f.Db.Connections);
        Assert.Equal(500, v.BytesOut);
        Assert.Equal(900, v.BytesIn);
    }

    /// <summary>
    /// The wire format, spelled out.
    ///
    /// <para>
    /// This is the exact JSON the sensor sends — copied from the
    /// <c>JsonPropertyName</c> attributes in <c>Reporter.cs</c>, and pinned in
    /// <c>SensorTests.The_wire_format_is_what_the_control_plane_binds</c> on
    /// the other side. Two halves that meet at a literal: rename one side and
    /// exactly one of the two tests goes red.
    /// </para>
    ///
    /// <para>
    /// Up to 0.9.0 there was no such test, and the two sides had drifted apart
    /// unnoticed: the sensor sent <c>verbindungen</c> and <c>prozess</c> while
    /// this side had already been renamed to <c>Connections</c> and
    /// <c>Process</c>. Nothing bound, <c>batch.Connections</c> arrived as
    /// null, and the endpoint fell over on the first field it read. The unit
    /// tests above were all green throughout — they call
    /// <see cref="SensorApi.ApplyAsync"/> directly and never touch the JSON.
    /// </para>
    /// </summary>
    public const string WireFormat = """
        {
          "connections": [
            {
              "process": "chrome",
              "destination": "140.82.121.4",
              "port": 443,
              "protocol": "tcp",
              "count": 3,
              "first": "2026-08-25T15:00:00+00:00",
              "last": "2026-08-25T15:00:30+00:00",
              "bytesOut": 1234,
              "bytesIn": 5678
            }
          ]
        }
        """;

    [Fact]
    public void What_the_sensor_sends_binds_to_what_the_endpoint_takes()
    {
        // JsonSerializerDefaults.Web is what minimal APIs use to bind a body.
        var batch = JsonSerializer.Deserialize<SensorBatch>(
            WireFormat, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(batch);
        var m = Assert.Single(batch!.Connections);

        // Every field, not just one: a single name that fails to bind leaves a
        // default behind, and a default is not obviously wrong when you look
        // at it. A count of 0 or an empty process name reads like "nothing
        // happened" rather than "nothing arrived".
        Assert.Equal("chrome", m.Process);
        Assert.Equal("140.82.121.4", m.Destination);
        Assert.Equal(443, m.Port);
        Assert.Equal("tcp", m.Protocol);
        Assert.Equal(3, m.Count);
        Assert.Equal(new DateTimeOffset(2026, 8, 25, 15, 0, 0, TimeSpan.Zero), m.First);
        Assert.Equal(new DateTimeOffset(2026, 8, 25, 15, 0, 30, TimeSpan.Zero), m.Last);
        Assert.Equal(1234, m.BytesOut);
        Assert.Equal(5678, m.BytesIn);
    }

    /// <summary>
    /// Null means "not counted" and has to survive the wire as null. A zero
    /// would look like a measurement and would claim the program sends
    /// nothing.
    /// </summary>
    [Fact]
    public void A_missing_byte_count_arrives_as_null_and_not_as_zero()
    {
        const string withoutBytes = """
            {"connections":[{"process":"svchost","destination":"1.1.1.1",
              "port":853,"protocol":"tcp","count":1,
              "first":"2026-08-25T15:00:00+00:00","last":"2026-08-25T15:00:00+00:00"}]}
            """;

        var batch = JsonSerializer.Deserialize<SensorBatch>(
            withoutBytes, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var m = Assert.Single(batch!.Connections);
        Assert.Null(m.BytesOut);
        Assert.Null(m.BytesIn);
    }
}
