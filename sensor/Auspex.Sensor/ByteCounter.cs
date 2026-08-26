using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Auspex.Sensor;

/// <summary>
/// Counts how much has flowed over a connection.
///
/// <para>
/// Windows only hands this out through TCP ESTATS, and those have to be
/// <em>switched on per connection</em> before anything is counted. That
/// needs administrator rights. Without them there is no number — and no
/// number is better than an invented one.
/// </para>
///
/// <para>
/// What comes out is a <strong>lower bound</strong>, for two reasons.
/// First, counting starts only once the sensor has seen the connection;
/// whatever ran in the seconds before that is missing. Second, it does not
/// see connections that open and close between two polls at all. "At least
/// this much" is the correct reading.
/// </para>
///
/// <para>
/// ESTATS counters are <em>totals per connection</em>, not increments.
/// Adding them up on every poll counts a connection that lives through ten
/// polls ten times over. So this class remembers the last reading per
/// connection and hands out only the difference.
/// </para>
/// </summary>
public sealed class ByteCounter
{
    /// <summary>
    /// <c>TcpConnectionEstatsData</c> — the <em>second</em> value in
    /// <c>TCP_ESTATS_TYPE</c>, not the first.
    ///
    /// <para>
    /// This said 0, with "TcpConnectionEstatsData" written next to it. But 0
    /// is <c>TcpConnectionEstatsSynOpts</c>, and SynOpts has no read-write
    /// structure at all — there is nothing to switch on. Windows duly
    /// rejected every call with 1784 (<c>ERROR_INVALID_USER_BUFFER</c>): the
    /// buffer we held out simply was not one for this type.
    /// </para>
    ///
    /// <para>
    /// Measured before changing it: with type 0 the enable failed identically
    /// at <em>every</em> buffer size (1, 2, 4, 8) — so size was never the
    /// cause. Version 1 answered <c>ERROR_NOT_SUPPORTED</c>, which also
    /// settles that version 0 is right and was not the problem either.
    /// </para>
    /// </summary>
    internal const int EstatsData = 1;

    private const int NoError = 0;
    private const int AccessDenied = 5;

    /// <summary>
    /// Size of TCP_ESTATS_DATA_ROD_v0. If it is wrong Windows rejects the
    /// call — nothing is read incorrectly.
    /// </summary>
    private const int RodSize = 96;

    private const int RwSize = 1; // TCP_ESTATS_DATA_RW_v0: one BOOLEAN

    [DllImport("iphlpapi.dll")]
    private static extern uint SetPerTcpConnectionEStats(
        byte[] row, int estatsType, byte[] rw, uint rwVersion, uint rwSize, uint offset);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetPerTcpConnectionEStats(
        byte[] row, int estatsType,
        byte[]? rw, uint rwVersion, uint rwSize,
        byte[]? ros, uint rosVersion, uint rosSize,
        byte[] rod, uint rodVersion, uint rodSize);

    [DllImport("iphlpapi.dll")]
    private static extern uint SetPerTcp6ConnectionEStats(
        byte[] row, int estatsType, byte[] rw, uint rwVersion, uint rwSize, uint offset);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetPerTcp6ConnectionEStats(
        byte[] row, int estatsType,
        byte[]? rw, uint rwVersion, uint rwSize,
        byte[]? ros, uint rosVersion, uint rosSize,
        byte[] rod, uint rodVersion, uint rodSize);

    /// <summary>Last reading per connection, for the difference.</summary>
    private readonly Dictionary<(int, int, string, int), (ulong Out, ulong In)> _snapshot = [];

    /// <summary>Connections for which counting is already switched on.</summary>
    private readonly HashSet<(int, int, string, int)> _enabled = [];

    /// <summary>
    /// Whether a value ever came out at all.
    ///
    /// <para>
    /// If none has after a good many polls, something is wrong — and that
    /// belongs said out loud. A column that stays empty otherwise looks like
    /// a deliberate choice.
    /// </para>
    /// </summary>
    private bool _everRead;
    private int _invain;

    private ByteCounter()
    {
    }

    /// <summary>
    /// Tries to set counting up.
    ///
    /// <para>
    /// Don't ask about rights, try it: what matters is whether it works, not
    /// which group you are in.
    /// </para>
    ///
    /// <para>
    /// And the failure has to <em>speak</em>. It used to be enough here that
    /// access was not denied — every other error produced a counter that
    /// looked like it was working and never delivered a number. The empty
    /// column then looked exactly like "started without administrator
    /// rights", and the actual reason was written nowhere. Now at least one
    /// enable has to succeed, and whatever else came back gets named.
    /// </para>
    /// </summary>
    /// <param name="reason">Why it does not work, when it does not.</param>
    public static ByteCounter? TryCreate(out string reason)
    {
        reason = "";
        if (!OperatingSystem.IsWindows())
        {
            reason = "not Windows";
            return null;
        }

        var open = ConnectionTable.Read();
        if (open.Count == 0)
        {
            reason = "no open connection right now to check this against";
            return null;
        }

        var counter = new ByteCounter();
        uint previous = 0;

        foreach (var v in open.Take(8))
        {
            var result = counter.Enable(v);
            if (result == NoError)
            {
                return counter;
            }
            previous = result;
        }

        reason = previous switch
        {
            AccessDenied => "Administratorrechte fehlen",
            87 => "invalid parameter (error 87) - the connection row does not match",
            1168 => "connection not found (error 1168) - the connection row does not match",
            _ => $"Windows error {previous}",
        };
        return null;
    }

    /// <summary>
    /// Reads the increments since the last poll.
    /// </summary>
    public Dictionary<(int, int, string, int), (long Out, long In)> Read(
        IReadOnlyList<OpenConnection> open)
    {
        var delta = new Dictionary<(int, int, string, int), (long, long)>();
        var seen = new HashSet<(int, int, string, int)>();

        foreach (var v in open)
        {
            var key = v.Key;
            seen.Add(key);

            if (_enabled.Add(key))
            {
                Enable(v);
                // Nothing to count on the first pass: enabling resets the
                // counter to zero, and whatever ran before that is gone
                // anyway.
                continue;
            }

            if (Values(v) is not { } now)
            {
                // Say it once, not on every poll - otherwise the console is
                    // full and the statement is no longer one.
                if (!_everRead && ++_invain == 50)
                {
                    Console.Error.WriteLine(
                        "The byte counters return nothing even though the enable worked. "
                        + "The column stays empty.");
                }
                continue;
            }

            _everRead = true;

            if (_snapshot.TryGetValue(key, out var before))
            {
                // Upwards only: after a connection restarts the counters can
                // be smaller, and a negative number would be worse than a
                // skipped one.
                var outbound = now.Out > before.Out ? now.Out - before.Out : 0;
                var inbound = now.In > before.In ? now.In - before.In : 0;
                if (outbound > 0 || inbound > 0)
                {
                    delta[key] = ((long)outbound, (long)inbound);
                }
            }

            _snapshot[key] = now;
        }

        // What is closed needs no room. Without this the memory grows
        // with every connection the machine has ever opened.
        foreach (var gone in _snapshot.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _snapshot.Remove(gone);
            _enabled.Remove(gone);
        }

        return delta;
    }

    /// <summary>Switches counting on for one connection.</summary>
    private uint Enable(OpenConnection v)
    {
        var rw = new byte[RwSize];
        rw[0] = 1; // EnableCollection

        return v.Remote.AddressFamily == AddressFamily.InterNetwork
            ? SetPerTcpConnectionEStats(Row4(v), EstatsData, rw, 0, RwSize, 0)
            : SetPerTcp6ConnectionEStats(Row6(v), EstatsData, rw, 0, RwSize, 0);
    }

    /// <summary>Reads the totals of one connection.</summary>
    private static (ulong Out, ulong In)? Values(OpenConnection v)
    {
        var rod = new byte[RodSize];

        var result = v.Remote.AddressFamily == AddressFamily.InterNetwork
            ? GetPerTcpConnectionEStats(Row4(v), EstatsData, null, 0, 0, null, 0, 0,
                rod, 0, RodSize)
            : GetPerTcp6ConnectionEStats(Row6(v), EstatsData, null, 0, 0, null, 0, 0,
                rod, 0, RodSize);

        if (result != NoError)
        {
            return null;
        }

        // TCP_ESTATS_DATA_ROD_v0: DataBytesOut, DataSegsOut, DataBytesIn, ...
        var outbound = BinaryPrimitives.ReadUInt64LittleEndian(rod.AsSpan(0, 8));
        var inbound = BinaryPrimitives.ReadUInt64LittleEndian(rod.AsSpan(16, 8));
        return (outbound, inbound);
    }

    /// <summary>
    /// MIB_TCPROW: dwState, dwLocalAddr, dwLocalPort, dwRemoteAddr,
    /// dwRemotePort.
    ///
    /// <para>
    /// <strong>All four fields have to be right.</strong> This used to carry
    /// a local address of zero, with a comment saying Windows would find the
    /// connection by ports and remote end anyway. That was an assumption, and
    /// it was wrong: a connection is named by the complete four-tuple, and
    /// with a zero Windows does not find it. The call failed, the byte column
    /// stayed empty, and because an empty column looks like "not counted", it
    /// went unnoticed.
    /// </para>
    /// </summary>
    private static byte[] Row4(OpenConnection v)
    {
        var line = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(line.AsSpan(0, 4), 5); // ESTAB
        v.Local.GetAddressBytes().CopyTo(line.AsSpan(4, 4));
        WritePort(line.AsSpan(8, 4), v.LocalPort);
        v.Remote.GetAddressBytes().CopyTo(line.AsSpan(12, 4));
        WritePort(line.AsSpan(16, 4), v.Port);
        return line;
    }

    /// <summary>
    /// MIB_TCP6ROW: State, LocalAddr[16], dwLocalScopeId, dwLocalPort,
    /// RemoteAddr[16], dwRemoteScopeId, dwRemotePort.
    ///
    /// <para>
    /// The scope ids belong in there too: with link-local addresses they are
    /// the only thing separating two connections that would otherwise look
    /// identical.
    /// </para>
    /// </summary>
    private static byte[] Row6(OpenConnection v)
    {
        var line = new byte[52];
        BinaryPrimitives.WriteInt32LittleEndian(line.AsSpan(0, 4), 5);
        v.Local.GetAddressBytes().CopyTo(line.AsSpan(4, 16));
        BinaryPrimitives.WriteInt32LittleEndian(line.AsSpan(20, 4), v.LocalScope);
        WritePort(line.AsSpan(24, 4), v.LocalPort);
        v.Remote.GetAddressBytes().CopyTo(line.AsSpan(28, 16));
        BinaryPrimitives.WriteInt32LittleEndian(line.AsSpan(44, 4), v.RemoteScope);
        WritePort(line.AsSpan(48, 4), v.Port);
        return line;
    }

    /// <summary>
    /// The port sits in network order in the low two bytes of a DWORD — the
    /// same quirk as when reading the table.
    /// </summary>
    private static void WritePort(Span<byte> four, int port)
    {
        four[0] = (byte)((port >> 8) & 0xFF);
        four[1] = (byte)(port & 0xFF);
        four[2] = 0;
        four[3] = 0;
    }
}
