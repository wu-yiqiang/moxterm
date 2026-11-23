using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MoxTerm.Services;

/// <summary>单个端口的扫描结果。</summary>
public sealed record PortScanResult(int Port, bool IsOpen, string Service, string Banner)
{
    public string PortText => Port.ToString();

    /// <summary>Banner 为空时列表里显示占位符，避免行高跳动。</summary>
    public string BannerText => string.IsNullOrWhiteSpace(Banner) ? "—" : Banner;
}

/// <summary>
/// TCP 端口扫描：解析端口表达式、解析主机名，并用限定并发的连接尝试探测端口是否开放。
/// </summary>
public static class PortScanService
{
    /// <summary>与探测并行度配合的单个连接超时，过小会漏报慢速主机，过大则整轮扫描变慢。</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(800);

    private const int DefaultConcurrency = 128;

    private const int BannerBytes = 256;

    /// <summary>
    /// 解析端口表达式，支持 <c>1-1024</c>、<c>22,80,443</c>、空格或分号分隔以及混用。
    /// </summary>
    public static bool TryParsePorts(string? text, out IReadOnlyList<int> ports, out string? error)
    {
        var result = new SortedSet<int>();

        foreach (var token in (text ?? string.Empty).Split([',', '，', ';', '；', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var dash = token.IndexOf('-');
            if (dash < 0)
            {
                if (!TryParsePort(token, out var single))
                {
                    ports = Array.Empty<int>();
                    error = token;
                    return false;
                }

                result.Add(single);
                continue;
            }

            var fromText = token[..dash].Trim();
            var toText = token[(dash + 1)..].Trim();

            if (!TryParsePort(fromText, out var from) || !TryParsePort(toText, out var to) || from > to)
            {
                ports = Array.Empty<int>();
                error = token;
                return false;
            }

            for (var port = from; port <= to; port++)
            {
                result.Add(port);
            }
        }

        if (result.Count == 0)
        {
            ports = Array.Empty<int>();
            error = string.Empty;
            return false;
        }

        ports = result.ToList();
        error = null;
        return true;
    }

    private static bool TryParsePort(string text, out int port) =>
        int.TryParse(text, out port) && port is >= 1 and <= 65535;

    /// <summary>
    /// 扫描 <paramref name="host"/> 的指定端口。每完成一个端口回调一次，
    /// 结果回调在后台线程触发，调用方负责切回 UI 线程。
    /// </summary>
    public static async Task<IPAddress> ScanAsync(
        string host,
        IReadOnlyList<int> ports,
        Action<PortScanResult> onResult,
        Action onScanned,
        CancellationToken cancellationToken)
    {
        var address = await ResolveAsync(host, cancellationToken);

        await Parallel.ForEachAsync(
            ports,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = DefaultConcurrency,
                CancellationToken = cancellationToken,
            },
            async (port, token) =>
            {
                var result = await ProbeAsync(address, port, token);
                onResult(result);
                onScanned();
            });

        return address;
    }

    /// <summary>把主机名解析为 IPv4（优先）或 IPv6 地址。</summary>
    public static async Task<IPAddress> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var parsed))
        {
            return parsed;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"无法解析主机名 {host}：{ex.Message}", ex);
        }

        return addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault()
            ?? throw new InvalidOperationException($"无法解析主机名 {host}。");
    }

    /// <summary>
    /// 尝试建立 TCP 连接；连上后短暂等待服务端主动发送的欢迎信息（SSH、SMTP 等）。
    /// 连接被拒绝或超时都视为未开放。
    /// </summary>
    private static async Task<PortScanResult> ProbeAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient(address.AddressFamily);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultTimeout);

        try
        {
            await client.ConnectAsync(address, port, timeout.Token);
            var banner = await TryReadBannerAsync(client, cancellationToken);
            return new PortScanResult(port, true, WellKnownService(port), banner);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 超时：端口可能被防火墙丢弃
            return new PortScanResult(port, false, WellKnownService(port), string.Empty);
        }
        catch (SocketException)
        {
            // 连接被拒绝 / 不可达
            return new PortScanResult(port, false, WellKnownService(port), string.Empty);
        }
    }

    /// <summary>读取服务端主动发送的横幅；对 HTTP 等不主动说话的服务返回空串。</summary>
    private static async Task<string> TryReadBannerAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(700));

            var buffer = new byte[BannerBytes];
            var read = await client.GetStream().ReadAsync(buffer, timeout.Token);
            if (read <= 0)
            {
                return string.Empty;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, read);
            var line = text.Split('\n')[0].TrimEnd('\r').Trim();
            var printable = new string(line.Where(character => !char.IsControl(character)).ToArray());

            return printable.Length <= 120 ? printable : printable[..120];
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>常见端口对应的服务名，未知端口留空。</summary>
    private static string WellKnownService(int port) => port switch
    {
        20 => "FTP-Data",
        21 => "FTP",
        22 => "SSH",
        23 => "Telnet",
        25 => "SMTP",
        53 => "DNS",
        67 or 68 => "DHCP",
        69 => "TFTP",
        80 => "HTTP",
        88 => "Kerberos",
        110 => "POP3",
        111 => "RPC",
        123 => "NTP",
        135 => "MS RPC",
        137 or 138 or 139 => "NetBIOS",
        143 => "IMAP",
        161 or 162 => "SNMP",
        389 => "LDAP",
        443 => "HTTPS",
        445 => "SMB",
        465 => "SMTPS",
        513 => "Rlogin",
        514 => "Syslog",
        587 => "SMTP",
        631 => "IPP",
        636 => "LDAPS",
        873 => "rsync",
        993 => "IMAPS",
        995 => "POP3S",
        1080 => "SOCKS",
        1433 => "MSSQL",
        1521 => "Oracle",
        1723 => "PPTP",
        2049 => "NFS",
        2375 or 2376 => "Docker",
        3000 => "Dev HTTP",
        3306 => "MySQL",
        3389 => "RDP",
        5044 => "Logstash",
        5432 => "PostgreSQL",
        5672 => "AMQP",
        5900 => "VNC",
        5985 or 5986 => "WinRM",
        6379 => "Redis",
        6443 => "Kubernetes",
        8080 or 8888 or 9000 => "HTTP",
        8443 => "HTTPS",
        9200 or 9300 => "Elasticsearch",
        11211 => "Memcached",
        27017 => "MongoDB",
        _ => string.Empty,
    };
}
