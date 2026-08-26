using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;

namespace Auspex.Sensor;

/// <summary>An open connection, as the operating system holds it.</summary>
public readonly record struct OpenConnection(
    int Pid,
    IPAddress Local,
    int LocalPort,
    IPAddress Remote,
    int Port,
    int LocalScope = 0,
    int RemoteScope = 0)
{
    /// <summary>
    /// What separates this connection from every other one.
    ///
    /// <para>
    /// The local port belongs in there and carries most of the weight: on one
    /// machine at one moment it is unique. Without it, ten simultaneous
    /// connections from the same browser to the same address would be a
    /// single one, and the sensor would count nine too few.
    /// </para>
    /// </summary>
    public (int, int, string, int) Key => (Pid, LocalPort, Remote.ToString(), Port);
}

/// <summary>
/// Reads the operating system's TCP connection table.
///
/// <para>
/// <strong>TCP only, and that is not convenience.</strong> Windows keeps no
/// remote end for UDP — <c>GetExtendedUdpTable</c> gives the local port and
/// the process id, nothing more. UDP is connectionless; there simply is no
/// table in the kernel saying where a datagram went.
/// </para>
///
/// <para>
/// Which means: <em>QUIC stays invisible.</em> Whatever Chrome, Edge and the
/// Google services move over HTTP/3 does not appear here. Closing that gap
/// needs ETW, and with it administrator rights, a permanent trace and a good
/// deal more machinery. While it is open it belongs named — a list that says
/// nothing about QUIC looks more complete than it is.
/// </para>
///
/// <para>
/// Reading is by polling, not by listening. A connection that opens and
/// closes between two polls is not seen. At two seconds apart that catches
/// short fetches; anything moving a meaningful amount of data stands long
/// enough.
/// </para>
/// </summary>
public static class ConnectionTable
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;

    /// <summary>TCP_TABLE_OWNER_PID_ALL — with process id, every state.</summary>
    private const int TcpTableOwnerPidAll = 5;

    /// <summary>MIB_TCP_STATE_ESTAB.</summary>
    private const int Established = 5;

    private const int NoError = 0;
    private const int NotEnoughMemory = 122; // ERROR_INSUFFICIENT_BUFFER

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, int reserved);

    /// <summary>Whether the sensor can read anything on this system at all.</summary>
    public static bool Available => OperatingSystem.IsWindows();

    /// <summary>
    /// Every established TCP connection, both address families.
    /// </summary>
    public static List<OpenConnection> Read()
    {
        var all = new List<OpenConnection>();
        if (!Available)
        {
            return all;
        }

        Read(AfInet, all);
        Read(AfInet6, all);
        return all;
    }

    private static void Read(int family, List<OpenConnection> destination)
    {
        var size = 0;
        var result = GetExtendedTcpTable(
            IntPtr.Zero, ref size, false, family, TcpTableOwnerPidAll, 0);

        if (result != NotEnoughMemory && result != NoError)
        {
            return;
        }
        if (size <= 0)
        {
            return;
        }

        var store = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedTcpTable(
                store, ref size, false, family, TcpTableOwnerPidAll, 0);
            if (result != NoError)
            {
                return;
            }

            // Read the table raw instead of marshalling structures: the
            // layout is fixed and short, so there is no surprise from
            // alignment or packing.
            //
            // Copy instead of spanning a pointer: that saves unsafe code for
            // a few dozen kilobytes which only come up every two seconds
            // anyway.
            var buffer = new byte[size];
            Marshal.Copy(store, buffer, 0, size);
            Parse(buffer, family, destination);
        }
        finally
        {
            Marshal.FreeHGlobal(store);
        }
    }

    /// <summary>
    /// Parses the raw buffer. Separate and internal so a test can check it
    /// against a hand-built buffer without needing Windows.
    /// </summary>
    internal static void Parse(ReadOnlySpan<byte> buffer, int family, List<OpenConnection> destination)
    {
        if (buffer.Length < 4)
        {
            return;
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        // MIB_TCPROW_OWNER_PID is 24 bytes, MIB_TCP6ROW_OWNER_PID 56.
        var width = family == AfInet ? 24 : 56;
        var start = 4;

        for (var i = 0; i < count; i++)
        {
            var from = start + (i * width);
            if (from + width > buffer.Length)
            {
                return;
            }
            var line = buffer.Slice(from, width);

            if (family == AfInet)
            {
                // dwState, dwLocalAddr, dwLocalPort, dwRemoteAddr,
                // dwRemotePort, dwOwningPid
                if (BinaryPrimitives.ReadInt32LittleEndian(line) != Established)
                {
                    continue;
                }
                var local = new IPAddress(line[4..8].ToArray());
                var localPort = Port(line[8..12]);
                var against = new IPAddress(line[12..16].ToArray());
                var port = Port(line[16..20]);
                var pid = BinaryPrimitives.ReadInt32LittleEndian(line[20..24]);

                Record(destination, new OpenConnection(pid, local, localPort, against, port));
            }
            else
            {
                // ucLocalAddr[16], dwLocalScopeId, dwLocalPort,
                // ucRemoteAddr[16], dwRemoteScopeId, dwRemotePort,
                // dwState, dwOwningPid
                if (BinaryPrimitives.ReadInt32LittleEndian(line[48..52]) != Established)
                {
                    continue;
                }
                var local = new IPAddress(line[0..16].ToArray());
                var localScope = BinaryPrimitives.ReadInt32LittleEndian(line[16..20]);
                var localPort = Port(line[20..24]);
                var against = new IPAddress(line[24..40].ToArray());
                var remoteScope = BinaryPrimitives.ReadInt32LittleEndian(line[40..44]);
                var port = Port(line[44..48]);
                var pid = BinaryPrimitives.ReadInt32LittleEndian(line[52..56]);

                Record(destination, new OpenConnection(
                    pid, local, localPort, against, port, localScope, remoteScope));
            }
        }
    }

    /// <summary>
    /// Takes a row — unless it leads nowhere.
    ///
    /// <para>
    /// Loopback connections stay out. A machine keeps a dozen of them going
    /// at all times: services talking to each other, updaters, game
    /// platforms. They answer the question "where does this device send
    /// things" with "nowhere", and would fill the list without adding to it.
    /// </para>
    ///
    /// <para>
    /// Addresses from the local network do stay in. The router, the NAS, the
    /// dashboard itself — those are destinations, and the fact that they sit
    /// in the house is information, not a reason to leave them out.
    /// </para>
    /// </summary>
    private static void Record(List<OpenConnection> destination, OpenConnection v)
    {
        if (IPAddress.IsLoopback(v.Remote))
        {
            return;
        }
        destination.Add(v);
    }

    /// <summary>
    /// The port sits there as a DWORD, but the number itself is in network
    /// order in the low two bytes. Miss that and port 443 reads as 46853.
    /// </summary>
    private static int Port(ReadOnlySpan<byte> four) => (four[0] << 8) | four[1];
}
