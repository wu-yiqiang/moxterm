namespace MoxTerm.Services;

/// <summary>终端光标形状。</summary>
public enum TerminalCursorStyle
{
    /// <summary>方块。</summary>
    Block,

    /// <summary>下划线。</summary>
    Underline,

    /// <summary>竖线。</summary>
    Bar,
}

/// <summary>
/// 光标形状选项：枚举值、显示名，以及渲染时插入的字符。
/// </summary>
/// <param name="Style">形状。</param>
/// <param name="Name">设置界面显示名。</param>
/// <param name="Glyph">渲染光标用的字符。</param>
public sealed record TerminalCursorOption(TerminalCursorStyle Style, string Name, char Glyph)
{
    /// <summary>设置界面可选的全部形状。每次访问都重建，让显示名跟随当前界面语言。</summary>
    public static IReadOnlyList<TerminalCursorOption> All =>
    [
        new(TerminalCursorStyle.Block, Loc.Instance.CursorBlock, '█'),
        new(TerminalCursorStyle.Underline, Loc.Instance.CursorUnderline, '_'),
        new(TerminalCursorStyle.Bar, Loc.Instance.CursorBar, '▏'),
    ];

    /// <summary>按持久化的枚举名还原选项，无法识别时回退到方块光标。</summary>
    public static TerminalCursorOption FromName(string? name) =>
        All.FirstOrDefault(option => option.Style.ToString() == name) ?? All[0];
}
