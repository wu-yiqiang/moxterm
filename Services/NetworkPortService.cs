using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace MoxTerm.Services;

/// <summary>本机的一个 TCP / UDP 端口条目。</summary>
public sealed record PortEntry(
    string Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State,
    int Pid,
    string ProcessName)
{
    public string LocalText => LocalPort == 0 ? LocalAddress : $"{LocalAddress}:{LocalPort}";

    public string RemoteText => RemotePort == 0 ? "—" : $"{RemoteAddress}:{RemotePort}";

    public string StateText => string.IsNullOrEmpty(State) ? "—" : State;

    public string ProcessText => string.IsNullOrWhiteSpace(ProcessName) ? "—" : ProcessName;
}

/// <summary>
/// 通过 iphlpapi 的 GetExtendedTcpTable / GetExtendedUdpTable 读取本机 IPv4 的
/// TCP 监听与连接、UDP 端点，并按 PID 关联进程名。
/// </summary>
public static class NetworkPortService
{
    private const int AfInet = 2;

    private const int TcpTableOwnerPidAll = 5;

    private const int UdpTableOwnerPid = 1;

    private const uint ErrorInsufficientBuffer = 122;

    private delegate uint GetTableDelegate(
        IntPtr table,
        ref int size,
        bool order,
        int family,
        int tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRow
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint Pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRow
    {
        public uint LocalAddress;
        public uint LocalPort;
        public uint Pid;
    }

    /// <summary>读取全部 TCP / UDP 条目，按本地端口升序排列。</summary>
    public static IReadOnlyList<PortEntry> GetPorts()
    {
        var processes = new Dictionary<int, string>();
        var entries = new List<PortEntry>();

        ReadTcp(entries, processes);
        ReadUdp(entries, processes);

        entries.Sort((left, right) =>
        {
            var byPort = left.LocalPort.CompareTo(right.LocalPort);
            return byPort != 0 ? byPort : string.CompareOrdinal(left.Protocol, right.Protocol);
        });

        return entries;
    }

    private static void ReadTcp(List<PortEntry> entries, Dictionary<int, string> processes)
    {
        var buffer = QueryTable(GetExtendedTcpTable, TcpTableOwnerPidAll);

        try
        {
            var count = Marshal.ReadInt32(buffer);
            var pointer = IntPtr.Add(buffer, sizeof(int));
            var size = Marshal.SizeOf<MibTcpRow>();

            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<MibTcpRow>(pointer);
                pointer = IntPtr.Add(pointer, size);

                var pid = (int)row.Pid;
                entries.Add(new PortEntry(
                    "TCP",
                    FormatAddress(row.LocalAddress),
                    SwapPort(row.LocalPort),
                    FormatAddress(row.RemoteAddress),
                    SwapPort(row.RemotePort),
                    StateName(row.State),
                    pid,
                    ProcessNameOf(pid, processes)));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ReadUdp(List<PortEntry> entries, Dictionary<int, string> processes)
    {
        var buffer = QueryTable(GetExtendedUdpTable, UdpTableOwnerPid);

        try
        {
            var count = Marshal.ReadInt32(buffer);
            var pointer = IntPtr.Add(buffer, sizeof(int));
            var size = Marshal.SizeOf<MibUdpRow>();

            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<MibUdpRow>(pointer);
                pointer = IntPtr.Add(pointer, size);

                var pid = (int)row.Pid;
                entries.Add(new PortEntry(
                    "UDP",
                    FormatAddress(row.LocalAddress),
                    SwapPort(row.LocalPort),
                    string.Empty,
                    0,
                    string.Empty,
                    pid,
                    ProcessNameOf(pid, processes)));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>先探测所需缓冲区大小，再读取表数据；调用方负责释放内存。</summary>
    private static IntPtr QueryTable(GetTableDelegate query, int tableClass)
    {
        var size = 0;
        var result = query(IntPtr.Zero, ref size, true, AfInet, tableClass, 0);

        if (result != ErrorInsufficientBuffer && result != 0)
        {
            throw new InvalidOperationException($"读取端口表失败（错误码 {result}）。");
        }

        if (size == 0)
        {
            // 没有条目时给一个空表，避免零长度分配
            size = sizeof(int);
        }

        var buffer = Marshal.AllocHGlobal(size);
        result = query(buffer, ref size, true, AfInet, tableClass, 0);

        if (result != 0)
        {
            Marshal.FreeHGlobal(buffer);
            throw new InvalidOperationException($"读取端口表失败（错误码 {result}）。");
        }

        return buffer;
    }

    private static string ProcessNameOf(int pid, Dictionary<int, string> cache)
    {
        if (cache.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        string name;
        try
        {
            name = Process.GetProcessById(pid).ProcessName;
        }
        catch
        {
            name = string.Empty;
        }

        cache[pid] = name;
        return name;
    }

    /// <summary>表中的端口为网络字节序，低 16 位需要交换。</summary>
    private static int SwapPort(uint port) =>
        (int)(((port & 0xFF) << 8) | ((port >> 8) & 0xFF));

    private static string FormatAddress(uint address)
    {
        // 表中地址已是网络字节序，按内存字节直接构造即可
        var bytes = BitConverter.GetBytes(address);
        return new IPAddress(bytes).ToString();
    }

    private static string StateName(uint state) => state switch
    {
        1 => "CLOSED",
        2 => "LISTEN",
        3 => "SYN_SENT",
        4 => "SYN_RECEIVED",
        5 => "ESTABLISHED",
        6 => "FIN_WAIT_1",
        7 => "FIN_WAIT_2",
        8 => "CLOSE_WAIT",
        9 => "CLOSING",
        10 => "LAST_ACK",
        11 => "TIME_WAIT",
        12 => "DELETING",
        _ => string.Empty,
    };
}
