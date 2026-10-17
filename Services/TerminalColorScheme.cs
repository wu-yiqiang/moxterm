using Microsoft.UI.Xaml;
using Windows.UI;

namespace MoxTerm.Services;

/// <summary>
/// 终端配色方案：一组背景色与前景色。
/// <para>
/// 除“跟随应用主题”外，其余方案的配色是固定的，不随应用主题变化。
/// </para>
/// </summary>
/// <param name="Key">持久化用的稳定标识。</param>
/// <param name="Name">设置界面显示名。</param>
/// <param name="Background">终端背景色。</param>
/// <param name="Foreground">终端前景（文字）色。</param>
public sealed record TerminalColorScheme(string Key, string Name, Color Background, Color Foreground)
{
    /// <summary>深色主题下使用的默认配色。</summary>
    public static TerminalColorScheme Dark { get; } =
        new("Dark", "深色", Rgb(0x0C, 0x0C, 0x0C), Rgb(0xCC, 0xCC, 0xCC));

    /// <summary>浅色主题下使用的默认配色。</summary>
    public static TerminalColorScheme Light { get; } =
        new("Light", "浅色", Rgb(0xFF, 0xFF, 0xFF), Rgb(0x1F, 0x1F, 0x1F));

    /// <summary>Solarized 深色配色。</summary>
    public static TerminalColorScheme SolarizedDark { get; } =
        new("SolarizedDark", "Solarized Dark", Rgb(0x00, 0x2B, 0x36), Rgb(0x83, 0x94, 0x96));

    /// <summary>Solarized 浅色配色。</summary>
    public static TerminalColorScheme SolarizedLight { get; } =
        new("SolarizedLight", "Solarized Light", Rgb(0xFD, 0xF6, 0xE3), Rgb(0x65, 0x7B, 0x83));

    /// <summary>经典绿字终端配色。</summary>
    public static TerminalColorScheme Green { get; } =
        new("Green", "复古绿", Rgb(0x00, 0x10, 0x00), Rgb(0x33, 0xFF, 0x33));

    /// <summary>跟随应用主题；实际颜色由 <see cref="Resolve"/> 按主题决定。</summary>
    public static TerminalColorScheme FollowTheme { get; } = new("System", "跟随应用主题", default, default);

    /// <summary>设置界面可选的全部方案。每次访问都重建，让显示名跟随当前界面语言。</summary>
    public static IReadOnlyList<TerminalColorScheme> All =>
    [
        FollowTheme with { Name = Loc.Instance.SchemeFollow },
        Dark with { Name = Loc.Instance.SchemeDark },
        Light with { Name = Loc.Instance.SchemeLight },
        SolarizedDark,
        SolarizedLight,
        Green with { Name = Loc.Instance.SchemeGreen },
    ];

    /// <summary>按持久化的标识还原方案，无法识别时回退到“跟随应用主题”。</summary>
    public static TerminalColorScheme FromKey(string? key) =>
        All.FirstOrDefault(scheme => scheme.Key == key) ?? FollowTheme;

    /// <summary>把方案解析成实际配色；“跟随应用主题”按给定主题落到深色或浅色。</summary>
    public static TerminalColorScheme Resolve(string? key, ElementTheme theme) =>
        string.IsNullOrEmpty(key) || key == FollowTheme.Key
            ? (theme == ElementTheme.Light ? Light : Dark)
            : FromKey(key);

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);
}
