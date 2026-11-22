using System.Collections.Concurrent;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace MoxTerm.Services;

/// <summary>一个可用于抓包的网卡。</summary>
public sealed record CaptureAdapter(string Name, string Description);

/// <summary>
/// 已捕获的数据包：解析结果用于列表展示，原始字节保留给十六进制视图与 pcap 导出。
/// </summary>
public sealed class CapturedPacket
{
    public required DateTime Time { get; init; }

    public required byte[] Data { get; init; }

    /// <summary>源地址；带端口时形如 <c>192.168.1.2:54321</c>。</summary>
    public required string Source { get; init; }

    public required string Destination { get; init; }

    public required string Protocol { get; init; }

    /// <summary>协议摘要，例如端口与载荷长度。</summary>
    public required string Info { get; init; }

    /// <summary>抓包链路的链路层类型（pcap 文件头需要，1 表示以太网）。</summary>
    public required int LinkType { get; init; }

    public string TimeText => Time.ToString("HH:mm:ss.fff");

    public string LengthText => Data.Length.ToString();
}

/// <summary>
/// 基于 SharpPcap / Npcap 的抓包服务：打开网卡、应用 BPF 过滤器，把抓到的包放入队列，
/// 由界面按固定间隔批量取出，避免捕获线程直接触碰 UI 集合。
/// </summary>
public sealed class PacketCaptureService : IDisposable
{
    /// <summary>队列上限：界面来不及消费时丢弃新包并计数，防止内存无限增长。</summary>
    private const int MaxQueued = 20000;

    private readonly ConcurrentQueue<CapturedPacket> _queue = new();

    private ILiveDevice? _device;
    private long _dropped;
    private bool _disposed;

    public bool IsRunning { get; private set; }

    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>
    /// 枚举本机网卡。驱动缺失（DllNotFoundException）或初始化失败时返回 false，
    /// 由页面提示安装 Npcap。
    /// </summary>
    public static bool TryGetAdapters(out IReadOnlyList<CaptureAdapter> adapters, out string? error)
    {
        try
        {
            adapters = CaptureDeviceList.Instance
                .Select(device => new CaptureAdapter(
                    device.Name,
                    string.IsNullOrWhiteSpace(device.Description) ? device.Name : device.Description))
                .ToList();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            adapters = Array.Empty<CaptureAdapter>();
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 打开网卡并开始捕获。过滤表达式为空时抓取全部流量；
    /// 语法错误、权限不足等都会以异常形式抛给调用方。
    /// </summary>
    public void Start(CaptureAdapter adapter, string? filter)
    {
        Stop();

        var device = CaptureDeviceList.Instance.FirstOrDefault(item => item.Name == adapter.Name)
            ?? throw new InvalidOperationException($"未找到网卡：{adapter.Description}");

        device.Open(new DeviceConfiguration
        {
            Mode = DeviceModes.Promiscuous,
            ReadTimeout = 500,
        });

        try
        {
            // 过滤器语法错误会在这里抛出 PcapException
            if (!string.IsNullOrWhiteSpace(filter))
            {
                device.Filter = filter.Trim();
            }

            device.OnPacketArrival += OnPacketArrival;
            device.StartCapture();
        }
        catch
        {
            // 打开成功但开始捕获失败时关闭设备，避免句柄泄漏
            device.OnPacketArrival -= OnPacketArrival;

            try
            {
                device.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MoxTerm] 关闭网卡失败: {ex.Message}");
            }

            throw;
        }

        _device = device;
        IsRunning = true;
    }

    /// <summary>停止捕获并关闭网卡；可重复调用。</summary>
    public void Stop()
    {
        var device = _device;
        _device = null;
        IsRunning = false;

        if (device is null)
        {
            return;
        }

        device.OnPacketArrival -= OnPacketArrival;

        try
        {
            device.StopCapture();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MoxTerm] 停止抓包失败: {ex.Message}");
        }

        try
        {
            device.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MoxTerm] 关闭网卡失败: {ex.Message}");
        }
    }

    /// <summary>取走一个待展示的数据包（界面定时批量调用）。</summary>
    public bool TryDequeue(out CapturedPacket? packet) => _queue.TryDequeue(out packet);

    /// <summary>清空待展示队列并重置丢弃计数。</summary>
    public void ResetCounters()
    {
        while (_queue.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _dropped, 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        ResetCounters();
    }

    /// <summary>
    /// 捕获线程回调：只做最小解析与入队，任何异常都不能抛回驱动。
    /// </summary>
    private void OnPacketArrival(object sender, PacketCapture e)
    {
        try
        {
            if (_queue.Count >= MaxQueued)
            {
                Interlocked.Increment(ref _dropped);
                return;
            }

            var raw = e.GetPacket();
            var data = raw.Data ?? Array.Empty<byte>();
            var linkType = (int)raw.LinkLayerType;

            var (source, destination, protocol, info) = Describe(linkType, data);

            _queue.Enqueue(new CapturedPacket
            {
                Time = DateTime.Now,
                Data = data,
                Source = source,
                Destination = destination,
                Protocol = protocol,
                Info = info,
                LinkType = linkType,
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MoxTerm] 解析数据包失败: {ex.Message}");
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>解析链路层与 IP 层，得到列表展示所需的几个字段。</summary>
    private static (string Source, string Destination, string Protocol, string Info) Describe(int linkType, byte[] data)
    {
        Packet? packet;
        try
        {
            packet = Packet.ParsePacket((LinkLayers)linkType, data);
        }
        catch
        {
            return (string.Empty, string.Empty, "RAW", string.Empty);
        }

        var ip = packet.Extract<IPPacket>();
        if (ip is not null)
        {
            var source = ip.SourceAddress?.ToString() ?? string.Empty;
            var destination = ip.DestinationAddress?.ToString() ?? string.Empty;

            if (packet.Extract<TcpPacket>() is { } tcp)
            {
                return (
                    $"{source}:{tcp.SourcePort}",
                    $"{destination}:{tcp.DestinationPort}",
                    "TCP",
                    $"Len={tcp.PayloadData?.Length ?? 0}");
            }

            if (packet.Extract<UdpPacket>() is { } udp)
            {
                return (
                    $"{source}:{udp.SourcePort}",
                    $"{destination}:{udp.DestinationPort}",
                    "UDP",
                    $"Len={udp.PayloadData?.Length ?? 0}");
            }

            if (packet.Extract<IcmpV4Packet>() is { } icmp4)
            {
                return (source, destination, "ICMP", icmp4.TypeCode.ToString());
            }

            if (packet.Extract<IcmpV6Packet>() is { } icmp6)
            {
                return (source, destination, "ICMPv6", icmp6.Type.ToString());
            }

            return (source, destination, ip.Version.ToString(), string.Empty);
        }

        if (packet.Extract<ArpPacket>() is { } arp)
        {
            return (
                arp.SenderProtocolAddress?.ToString() ?? string.Empty,
                arp.TargetProtocolAddress?.ToString() ?? string.Empty,
                "ARP",
                string.Empty);
        }

        // 非 IP / ARP（例如 LLDP、STP）只显示链路层信息
        if (packet.Extract<EthernetPacket>() is { } ethernet)
        {
            return (
                ethernet.SourceHardwareAddress?.ToString() ?? string.Empty,
                ethernet.DestinationHardwareAddress?.ToString() ?? string.Empty,
                "ETH",
                string.Empty);
        }

        return (string.Empty, string.Empty, "RAW", string.Empty);
    }
}
