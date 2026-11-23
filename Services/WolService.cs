using System.Net;
using System.Net.Sockets;

namespace MoxTerm.Services;

/// <summary>一个保存的 Wake On Lan 设备。</summary>
public sealed class WolDevice
{
    public string Name { get; set; } = string.Empty;

    public string Mac { get; set; } = string.Empty;

    public string BroadcastAddress { get; set; } = "255.255.255.255";

    public int Port { get; set; } = 9;

    /// <summary>列表标题：未填名称时退回 MAC 地址。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Mac : Name;

    public string Detail => $"{Mac} · {BroadcastAddress}:{Port}";
}

/// <summary>
/// Wake On Lan：按网卡 MAC 构造魔术包（6 字节 0xFF + MAC 重复 16 次），
/// 通过 UDP 广播发送到目标设备的广播地址。
/// </summary>
public static class WolService
{
    private const int MacLength = 6;

    /// <summary>
    /// 解析 MAC 地址，兼容冒号、连字符、点号分隔及无分隔的 12 位十六进制。
    /// </summary>
    public static bool TryParseMac(string? text, out byte[] mac)
    {
        mac = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var digits = new string(text.Where(Uri.IsHexDigit).ToArray());
        if (digits.Length != MacLength * 2)
        {
            return false;
        }

        var result = new byte[MacLength];
        for (var index = 0; index < MacLength; index++)
        {
            result[index] = Convert.ToByte(digits.Substring(index * 2, 2), 16);
        }

        mac = result;
        return true;
    }

    /// <summary>把 MAC 格式化为大写冒号分隔形式。</summary>
    public static string FormatMac(byte[] mac) =>
        string.Join(':', mac.Select(value => value.ToString("X2")));

    /// <summary>发送魔术包；广播地址必须是 IPv4 地址。</summary>
    public static async Task SendAsync(byte[] mac, string broadcastAddress, int port)
    {
        if (!IPAddress.TryParse(broadcastAddress, out var target)
            || target.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("广播地址无效。", nameof(broadcastAddress));
        }

        var packet = new byte[MacLength * 17];
        packet.AsSpan(0, MacLength).Fill(0xFF);

        for (var repeat = 0; repeat < 16; repeat++)
        {
            mac.CopyTo(packet, MacLength + repeat * MacLength);
        }

        using var client = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        await client.SendAsync(packet, packet.Length, new IPEndPoint(target, port));
    }
}
