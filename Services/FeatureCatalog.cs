using Microsoft.UI.Xaml.Controls;

namespace MoxTerm.Services;

/// <summary>规划中功能的展示信息：图标、简介与规划能力列表，文案随语言动态解析。</summary>
public sealed record FeatureInfo(Symbol Icon, Func<string> Description, Func<IReadOnlyList<string>> Items);

/// <summary>
/// 功能分组目录：导航项的 <c>Tag</c> → 占位页内容。
/// 标题直接复用对应的导航文案，实现真实页面后从导航里换掉占位页即可。
/// </summary>
public static class FeatureCatalog
{
    public static FeatureInfo? Get(string? key)
    {
        var loc = Loc.Instance;

        return key switch
        {
            "netsvc" => new(Symbol.List, () => loc.FeatureNetServicesDesc, () => loc.FeatureNetServicesItems),
            "tunnel" => new(Symbol.Sync, () => loc.FeatureTunnelDesc, () => loc.FeatureTunnelItems),
            "keygen" => new(Symbol.Accept, () => loc.FeatureKeyGenDesc, () => loc.FeatureKeyGenItems),
            "netports" => new(Symbol.Target, () => loc.FeatureOpenPortsDesc, () => loc.FeatureOpenPortsItems),
            "wol" => new(Symbol.Clock, () => loc.FeatureWakeOnLanDesc, () => loc.FeatureWakeOnLanItems),
            "netscan" => new(Symbol.Refresh, () => loc.FeatureNetScanDesc, () => loc.FeatureNetScanItems),
            "portscan" => new(Symbol.Accept, () => loc.FeaturePortScanDesc, () => loc.FeaturePortScanItems),
            "pktcap" => new(Symbol.Folder, () => loc.FeaturePacketCaptureDesc, () => loc.FeaturePacketCaptureItems),
            "xserver" => new(Symbol.Target, () => loc.FeatureXServerDesc, () => loc.FeatureXServerItems),
            _ => null,
        };
    }
}
