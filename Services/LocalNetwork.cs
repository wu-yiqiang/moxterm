using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MoxTerm.Services;

/// <summary>本机局域网信息：统一枚举可用的 IPv4 地址，供网络扫描与本地服务复用。</summary>
public static class LocalNetwork
{
    /// <summary>一个可用的局域网 IPv4 地址。</summary>
    public readonly record struct LocalIPv4(IPAddress Address, int PrefixLength, bool HasGateway);

    /// <summary>枚举处于连接状态、非回环/隧道网卡上的 IPv4 单播地址。</summary>
    public static IEnumerable<LocalIPv4> EnumerateIPv4()
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up
                || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var properties = adapter.GetIPProperties();

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                yield return new LocalIPv4(
                    unicast.Address,
                    unicast.PrefixLength,
                    properties.GatewayAddresses.Count > 0);
            }
        }
    }

    /// <summary>取本机局域网 IPv4 地址，优先带默认网关的网卡。</summary>
    public static string GetLocalAddress()
    {
        string? fallback = null;

        foreach (var item in EnumerateIPv4())
        {
            if (item.HasGateway)
            {
                return item.Address.ToString();
            }

            fallback ??= item.Address.ToString();
        }

        return fallback ?? IPAddress.Loopback.ToString();
    }

    /// <summary>取本机首个可用 IPv4 网段的 CIDR，找不到时返回 null。</summary>
    public static string? GetLocalSubnet()
    {
        string? fallback = null;

        foreach (var item in EnumerateIPv4())
        {
            if (item.PrefixLength is < 8 or > 30)
            {
                continue;
            }

            var cidr = $"{MaskedAddress(item.Address, item.PrefixLength)}/{item.PrefixLength}";

            // 优先带默认网关的网卡（真实局域网），否则记住第一个候选
            if (item.HasGateway)
            {
                return cidr;
            }

            fallback ??= cidr;
        }

        return fallback;
    }

    private static string MaskedAddress(IPAddress address, int prefixLength)
    {
        var value = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
        var mask = prefixLength == 0 ? 0 : 0xFFFFFFFFu << (32 - prefixLength);
        value &= mask;

        return new IPAddress(new[]
        {
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value,
        }).ToString();
    }
}
