using System.Net;
using System.Runtime.InteropServices;

namespace IPWatcherPro.Infrastructure;

/// <summary>
/// Safe wrappers around iphlpapi.dll functions for TCP/UDP table enumeration and routing.
/// </summary>
public static class NativeMethods
{
    // ── P/Invoke Signatures ───────────────────────────────────────────────

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int dwOutBufLen,
        bool bOrder,
        int ulAf,
        int tcpTableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref int dwOutBufLen,
        bool bOrder,
        int ulAf,
        int udpTableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetBestRoute(
        uint dwDestAddr,
        uint dwSourceAddr,
        out MIB_IPFORWARDROW pBestRoute);

    // ── Structs ───────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_UDPROW_OWNER_PID
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_IPFORWARDROW
    {
        public uint dwForwardDest;
        public uint dwForwardMask;
        public uint dwForwardPolicy;
        public uint dwForwardNextHop;
        public uint dwForwardIfIndex;
        public uint dwForwardType;
        public uint dwForwardProto;
        public uint dwForwardAge;
        public uint dwForwardNextHopAS;
        public uint dwForwardMetric1;
        public uint dwForwardMetric2;
        public uint dwForwardMetric3;
        public uint dwForwardMetric4;
        public uint dwForwardMetric5;
    }

    // ── Constants ─────────────────────────────────────────────────────────

    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int UDP_TABLE_OWNER_PID = 1;
    private const uint NO_ERROR = 0;
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;

    // ── Public Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Retrieves all TCP connections with owning PID.
    /// </summary>
    public static List<MIB_TCPROW_OWNER_PID> GetTcpTableOwnerPid()
    {
        int bufferSize = 0;
        // First call to get required buffer size
        uint result = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);

        if (result != ERROR_INSUFFICIENT_BUFFER && result != NO_ERROR)
            throw new Exception($"GetExtendedTcpTable failed with error {result}");

        IntPtr buffer = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal(bufferSize);
            result = GetExtendedTcpTable(buffer, ref bufferSize, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);

            if (result != NO_ERROR)
                throw new Exception($"GetExtendedTcpTable failed with error {result}");

            // First 4 bytes are the number of entries
            int count = Marshal.ReadInt32(buffer);
            var rows = new List<MIB_TCPROW_OWNER_PID>(count);

            int offset = sizeof(uint);
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < count; i++)
            {
                IntPtr rowPtr = IntPtr.Add(buffer, offset + (i * rowSize));
                MIB_TCPROW_OWNER_PID row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                rows.Add(row);
            }
            return rows;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Retrieves all UDP endpoints with owning PID.
    /// </summary>
    public static List<MIB_UDPROW_OWNER_PID> GetUdpTableOwnerPid()
    {
        int bufferSize = 0;
        uint result = GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, false, AF_INET, UDP_TABLE_OWNER_PID, 0);

        if (result != ERROR_INSUFFICIENT_BUFFER && result != NO_ERROR)
            throw new Exception($"GetExtendedUdpTable failed with error {result}");

        IntPtr buffer = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal(bufferSize);
            result = GetExtendedUdpTable(buffer, ref bufferSize, false, AF_INET, UDP_TABLE_OWNER_PID, 0);

            if (result != NO_ERROR)
                throw new Exception($"GetExtendedUdpTable failed with error {result}");

            int count = Marshal.ReadInt32(buffer);
            var rows = new List<MIB_UDPROW_OWNER_PID>(count);

            int offset = sizeof(uint);
            int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();

            for (int i = 0; i < count; i++)
            {
                IntPtr rowPtr = IntPtr.Add(buffer, offset + (i * rowSize));
                MIB_UDPROW_OWNER_PID row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                rows.Add(row);
            }
            return rows;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Gets the best route to a destination IP.
    /// </summary>
    public static MIB_IPFORWARDROW? GetBestRouteTo(uint destIp)
    {
        MIB_IPFORWARDROW row;
        uint result = GetBestRoute(destIp, 0, out row);
        if (result == NO_ERROR)
            return row;
        return null;
    }

    // ── Conversion Helpers ────────────────────────────────────────────────

    /// <summary>
    /// Converts a WinAPI network-byte-order uint to an IP string.
    /// </summary>
    public static string NetworkUInt32ToIpString(uint ip)
    {
        byte[] bytes = new byte[4];
        bytes[0] = (byte)(ip >> 24);
        bytes[1] = (byte)(ip >> 16);
        bytes[2] = (byte)(ip >> 8);
        bytes[3] = (byte)ip;
        return new IPAddress(bytes).ToString();
    }

    /// <summary>
    /// Converts an IP string to a WinAPI network-byte-order uint.
    /// </summary>
    public static uint IpStringToNetworkUInt32(string ip)
    {
        if (IPAddress.TryParse(ip, out IPAddress? addr))
        {
            byte[] bytes = addr.GetAddressBytes();
            return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        }
        return 0;
    }

    /// <summary>
    /// Converts a WinAPI network-byte-order port to host order.
    /// </summary>
    public static ushort NetworkPortToHost(uint port)
    {
        return (ushort)((port >> 8) | (port << 8));
    }
}