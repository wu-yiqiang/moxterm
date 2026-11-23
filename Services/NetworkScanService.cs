using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace MoxTerm.Services;

/// <summary>最近一次探测中各常见服务是否开放。</summary>
public sealed record ServiceFlags(bool Ssh, bool Rdp, bool Vnc, bool Ftp, bool Telnet, bool Rlogin, bool Http)
{
    /// <summary>按 <see cref="NetworkScanService.ServiceNames"/> 的顺序排列的检测结果。</summary>
    public IReadOnlyList<bool> Values => [Ssh, Rdp, Vnc, Ftp, Telnet, Rlogin, Http];
}

/// <summary>一个在线主机的扫描结果。</summary>
public sealed record HostScanResult(
    IPAddress Address,
    string HostName,
    string Mac,
    long? LatencyMs,
    ServiceFlags Services)
{
    public string IpText => Address.ToString();

    public string HostNameText => string.IsNullOrWhiteSpace(HostName) ? "—" : HostName;

    public string MacText => string.IsNullOrWhiteSpace(Mac) ? "—" : Mac;

    public string LatencyText => LatencyMs is { } latency ? $"{latency} ms" : "—";
}

/// <summary>
/// 局域网主机发现：解析 CIDR / 地址区间，并发发送 ICMP 回显并查询 ARP，
/// 两者任一有响应即判定在线，再反查主机名。在线主机通常不响应 ICMP，
/// 因此 ARP 是本机网段的主要发现手段。
/// </summary>
public static class NetworkScanService
{
    /// <summary>结果列表中展示的常见服务，顺序与各列一致。</summary>
    public static readonly IReadOnlyList<string> ServiceNames = ["SSH", "RDP", "VNC", "FTP", "Telnet", "Rlogin", "HTTP"];

    private static readonly int[] ServicePorts = [22, 3389, 5900, 21, 23, 513, 80];

    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromMilliseconds(400);

    /// <summary>单次扫描的地址上限，避免误填 /8 之类的大范围。</summary>
    private const int MaxHosts = 65536;

    private const int MaxConcurrency = 128;

    private const int PingTimeoutMs = 1000;

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint physAddrLen);

    /// <summary>
    /// 解析扫描范围，支持 <c>192.168.1.0/24</c>、<c>192.168.1.1-192.168.1.254</c>、
    /// <c>192.168.1.1-254</c> 以及单个 IP。
    /// </summary>
    public static bool TryParseRange(string? text, out IReadOnlyList<IPAddress> hosts, out bool tooLarge)
    {
        hosts = Array.Empty<IPAddress>();
        tooLarge = false;

        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return false;
        }

        return value.Contains('/')
            ? TryParseCidr(value, out hosts, out tooLarge)
            : TryParseExplicitRange(value, out hosts, out tooLarge);
    }

    private static bool TryParseCidr(string value, out IReadOnlyList<IPAddress> hosts, out bool tooLarge)
    {
        hosts = Array.Empty<IPAddress>();
        tooLarge = false;

        var parts = value.Split('/', 2);
        if (!TryParseIPv4(parts[0], out var baseAddress)
            || !int.TryParse(parts[1].Trim(), out var prefix)
            || prefix is < 0 or > 32)
        {
            return false;
        }

        var start = ToUInt32(baseAddress) & Mask(prefix);
        var count = prefix == 32 ? 1L : 1L << (32 - prefix);

        if (count > MaxHosts)
        {
            tooLarge = true;
            return false;
        }

        // /31 与 /32 没有网络地址和广播地址的概念，全部保留
        var first = prefix <= 30 ? 1L : 0L;
        var last = prefix <= 30 ? count - 2 : count - 1;

        var list = new List<IPAddress>((int)Math.Max(0, last - first + 1));
        for (var offset = first; offset <= last; offset++)
        {
            list.Add(FromUInt32(start + (uint)offset));
        }

        hosts = list;
        return true;
    }

    private static bool TryParseExplicitRange(string value, out IReadOnlyList<IPAddress> hosts, out bool tooLarge)
    {
        hosts = Array.Empty<IPAddress>();
        tooLarge = false;

        var dash = value.IndexOf('-');
        if (dash < 0)
        {
            if (!TryParseIPv4(value, out var single))
            {
                return false;
            }

            hosts = new[] { single };
            return true;
        }

        if (!TryParseIPv4(value[..dash], out var from))
        {
            return false;
        }

        var toText = value[(dash + 1)..].Trim();
        IPAddress to;

        if (toText.Contains('.'))
        {
            if (!TryParseIPv4(toText, out to))
            {
                return false;
            }
        }
        else
        {
            // 省略前缀的简写：192.168.1.1-254 只指定末段
            if (!int.TryParse(toText, out var lastOctet) || lastOctet is < 0 or > 255)
            {
                return false;
            }

            var bytes = from.GetAddressBytes();
            bytes[3] = (byte)lastOctet;
            to = new IPAddress(bytes);
        }

        var start = ToUInt32(from);
        var end = ToUInt32(to);

        if (start > end)
        {
            return false;
        }

        if (end - start + 1 > MaxHosts)
        {
            tooLarge = true;
            return false;
        }

        var list = new List<IPAddress>((int)(end - start + 1));
        for (var address = start; address <= end; address++)
        {
            list.Add(FromUInt32(address));
        }

        hosts = list;
        return true;
    }

    private static bool TryParseIPv4(string text, out IPAddress address)
    {
        if (IPAddress.TryParse(text.Trim(), out var parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            address = parsed;
            return true;
        }

        address = IPAddress.None;
        return false;
    }

    private static uint Mask(int prefix) => prefix == 0 ? 0 : 0xFFFFFFFFu << (32 - prefix);

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value) => new(new[]
    {
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value,
    });

    /// <summary>
    /// 扫描指定地址列表。在线主机经回调返回，每完成一个地址回调一次；
    /// 回调在后台线程触发，调用方负责切回 UI 线程。
    /// </summary>
    public static Task ScanAsync(
        IReadOnlyList<IPAddress> hosts,
        Action<HostScanResult> onAlive,
        Action onScanned,
        CancellationToken cancellationToken)
    {
        return Parallel.ForEachAsync(
            hosts,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxConcurrency,
                CancellationToken = cancellationToken,
            },
            async (address, token) =>
            {
                var result = await ProbeAsync(address, token);
                if (result is not null)
                {
                    onAlive(result);
                }

                onScanned();
            });
    }

    /// <summary>ICMP 与 ARP 并行探测；两者都失败视为不在线。</summary>
    private static async Task<HostScanResult?> ProbeAsync(IPAddress address, CancellationToken cancellationToken)
    {
        var pingTask = PingAsync(address);
        var macTask = Task.Run(() => TryGetMac(address), cancellationToken);

        await Task.WhenAll(pingTask, macTask);

        var latency = pingTask.Result;
        var mac = macTask.Result;

        if (latency is null && mac is null)
        {
            return null;
        }

        var hostName = TryGetHostNameAsync(address, cancellationToken);
        var services = DetectServicesAsync(address, cancellationToken);

        await Task.WhenAll(hostName, services);

        return new HostScanResult(address, hostName.Result, mac ?? string.Empty, latency, services.Result);
    }

    /// <summary>并发探测各常见服务端口是否开放，任一失败按未开放处理。</summary>
    private static async Task<ServiceFlags> DetectServicesAsync(IPAddress address, CancellationToken cancellationToken)
    {
        var checks = ServicePorts
            .Select(port => IsPortOpenAsync(address, port, cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(checks);
        return new ServiceFlags(results[0], results[1], results[2], results[3], results[4], results[5], results[6]);
    }

    private static async Task<bool> IsPortOpenAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient(address.AddressFamily);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ServiceTimeout);

        try
        {
            await client.ConnectAsync(address, port, timeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<long?> PingAsync(IPAddress address)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, PingTimeoutMs);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>查询 ARP 表获取 MAC；仅对本机网段有效，远端地址会快速失败。</summary>
    private static string? TryGetMac(IPAddress address)
    {
        try
        {
            var destination = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
            var mac = new byte[6];
            uint length = (uint)mac.Length;

            if (SendARP(destination, 0, mac, ref length) != 0 || length == 0)
            {
                return null;
            }

            return string.Join(':', mac.Take((int)length).Select(value => value.ToString("X2")));
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> TryGetHostNameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(address)
                .WaitAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
            return entry.HostName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
