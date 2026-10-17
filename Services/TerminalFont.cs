namespace MoxTerm.Services;

/// <summary>
/// 一个可选的终端等宽字体。
/// </summary>
/// <param name="Name">设置界面显示名。</param>
/// <param name="Family">字体族名；<c>null</c> 表示使用应用内置的默认字体链。</param>
public sealed record TerminalFont(string Name, string? Family)
{
    private static readonly string FontsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

    /// <summary>应用内置的默认等宽字体链（见 App.xaml 的 <c>TerminalFontFamily</c>）。</summary>
    public static TerminalFont SystemDefault => new(Loc.Instance.FontSystemDefault, null);

    /// <summary>候选字体，以及用于判断是否已安装的字体文件名。</summary>
    private static readonly (TerminalFont Font, string ProbeFile)[] Candidates =
    [
        (new TerminalFont("Cascadia Mono", "Cascadia Mono"), "CascadiaMono.ttf"),
        (new TerminalFont("Cascadia Code", "Cascadia Code"), "CascadiaCode.ttf"),
        (new TerminalFont("Consolas", "Consolas"), "consola.ttf"),
        (new TerminalFont("Lucida Console", "Lucida Console"), "lucon.ttf"),
        (new TerminalFont("Courier New", "Courier New"), "cour.ttf"),
        (new TerminalFont("MS Gothic", "MS Gothic"), "msgothic.ttc"),
    ];

    /// <summary>探测本机已安装的候选字体；首项始终是默认字体。</summary>
    public static IReadOnlyList<TerminalFont> Discover()
    {
        var available = new List<TerminalFont> { SystemDefault };

        foreach (var (font, probeFile) in Candidates)
        {
            try
            {
                if (File.Exists(Path.Combine(FontsDirectory, probeFile)))
                {
                    available.Add(font);
                }
            }
            catch (ArgumentException)
            {
                // 字体目录路径含非法字符时跳过该项
            }
        }

        return available;
    }

    /// <summary>按字体族名还原选项，无法识别时回退到默认字体。</summary>
    public static TerminalFont FromFamily(string? family) =>
        Discover().FirstOrDefault(font => font.Family == family) ?? SystemDefault;
}
