using System.ComponentModel;
using System.Globalization;

namespace MoxTerm.Services;

/// <summary>
/// 运行时双语（中/英）字符串服务。所有用户可见文案从这里取，
/// 切换语言时通过 <see cref="PropertyChanged"/>（属性名为 <c>null</c>）刷新全部 x:Bind 绑定，
/// 并通过 <see cref="Changed"/> 通知页面重建下拉项等不走路径绑定的内容。
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    /// <summary>语言已切换；页面用它重建下拉项、动态描述等。</summary>
    public event EventHandler? Changed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>当前生效的语言：<c>zh</c> 或 <c>en</c>。</summary>
    public string ActiveLanguage { get; private set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";

    /// <summary>
    /// 应用设置里的语言选项：<c>Default</c> 跟随系统，<c>zh-CN</c> / <c>en-US</c> 固定语言。
    /// </summary>
    public void Apply(string? setting)
    {
        var language = setting switch
        {
            "zh-CN" => "zh",
            "en-US" => "en",
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en",
        };

        if (language == ActiveLanguage)
        {
            return;
        }

        ActiveLanguage = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string T(string zh, string en) => ActiveLanguage == "zh" ? zh : en;

    // 主窗口与终端工具栏
    public string NavTerminal => T("终端", "Terminal");
    public string NewSession => T("新建会话", "New session");
    public string ShellPickerTip => T("新建会话使用的 Shell", "Shell used for new sessions");
    public string Clear => T("清屏", "Clear");
    public string ClearTip => T("清屏 (Ctrl+L)", "Clear (Ctrl+L)");

    // 设置页分组
    public string SettingsTitle => T("设置", "Settings");
    public string GroupAppearance => T("外观", "Appearance");
    public string GroupTerminal => T("终端", "Terminal");
    public string GroupInput => T("输入", "Input");
    public string GroupAbout => T("关于", "About");

    // 外观
    public string Language => T("语言", "Language");
    public string LanguageDesc => T("应用界面使用的语言", "Language used by the app UI");
    public string LangSystem => T("跟随系统", "Follow system");
    public string Theme => T("应用主题", "App theme");
    public string ThemeDesc => T("选择要显示的应用主题", "Choose which theme the app displays");
    public string ThemeSystem => T("跟随系统", "Follow system");
    public string ThemeLight => T("浅色", "Light");
    public string ThemeDark => T("深色", "Dark");
    public string ColorScheme => T("终端配色", "Terminal colors");
    public string ColorSchemeDesc => T("终端背景与文字颜色", "Terminal background and text colors");
    public string Font => T("终端字体", "Terminal font");
    public string FontDesc => T("使用系统中已安装的等宽字体", "Monospace fonts installed on the system");
    public string FontSize => T("字体大小", "Font size");
    public string FontSizeDesc => T("取值 8 – 32", "Range 8 – 32");
    public string LineHeight => T("行高倍数", "Line height");
    public string LineHeightDesc => T("相对字符高度，取值 1.0 – 2.0", "Relative to character height, 1.0 – 2.0");

    // 终端
    public string DefaultShell => T("默认 Shell", "Default shell");
    public string DefaultShellDesc => T("新建会话时使用的 Shell", "Shell used for new sessions");
    public string WorkingDir => T("起始目录", "Working directory");
    public string WorkingDirEmpty => T("留空则使用进程当前目录。", "Leave empty to use the current process directory.");
    public string WorkingDirValid => T("目录存在，新建会话将在此目录启动。", "Directory exists; new sessions will start here.");
    public string WorkingDirInvalid => T("目录不存在，不会被保存。", "Directory doesn't exist and won't be saved.");
    public string Scrollback => T("回滚缓冲行数", "Scrollback lines");
    public string ScrollbackDesc => T("保留的历史输出行数，取值 200 – 100000", "History lines kept, 200 – 100000");
    public string CursorShape => T("光标形状", "Cursor shape");
    public string CursorShapeDesc => T("终端光标的显示方式", "How the terminal cursor is displayed");
    public string CursorBlink => T("光标闪烁", "Cursor blinking");
    public string CursorBlinkDesc => T("仅在终端获得焦点时闪烁", "Blink only while the terminal is focused");
    public string Backend => T("终端后端", "Terminal backend");

    // 输入
    public string ConfirmPaste => T("粘贴多行文本时确认", "Confirm multi-line paste");
    public string ConfirmPasteDesc => T(
        "剪贴板中包含换行时，粘贴前先弹窗确认，避免误执行多行命令",
        "Ask before pasting text containing newlines, to avoid running multi-line commands by mistake");

    // 关于
    public string Reset => T("恢复默认设置", "Reset to defaults");
    public string ResetDesc => T("将所有设置恢复为默认值", "Restore all settings to default values");
    public string ResetDialogContent => T(
        "所有设置都会恢复为默认值，确定继续吗？",
        "All settings will be restored to default values. Continue?");
    public string Cancel => T("取消", "Cancel");

    public string AboutVersion(string version) => T(
        $"版本 {version} · 基于 .NET 10 与 Windows App SDK（WinUI 3）构建",
        $"Version {version} · Built with .NET 10 and Windows App SDK (WinUI 3)");

    // 选项显示名（配色 / 光标 / 后端 / 字体）
    public string SchemeDark => T("深色", "Dark");
    public string SchemeLight => T("浅色", "Light");
    public string SchemeGreen => T("复古绿", "Retro green");
    public string SchemeFollow => T("跟随应用主题", "Follow app theme");
    public string CursorBlock => T("方块", "Block");
    public string CursorUnderline => T("下划线", "Underline");
    public string CursorBar => T("竖线", "Bar");
    public string BackendPipe => T("管道（兼容模式）", "Pipe (compatibility mode)");
    public string BackendPipeDesc => T(
        "重定向标准输入输出，兼容性最好；输出为纯文本，不支持窗口尺寸协商与 ANSI 颜色。",
        "Redirects standard input and output; best compatibility. Plain-text output only, no size negotiation or ANSI colors.");
    public string BackendConPty => T("伪控制台（ConPTY）", "Pseudo console (ConPTY)");
    public string BackendConPtyDesc => T(
        "使用 Windows 伪控制台，支持 VT 序列与窗口尺寸协商；部分受限环境下子进程可能无法挂载。",
        "Uses the Windows pseudo console with VT sequences and size negotiation; may fail to attach in restricted environments.");
    public string FontSystemDefault => T("系统默认", "System default");

    // 终端运行时
    public string SessionStartFailed(string name, string message) => T(
        $"无法启动 {name}：{message}",
        $"Failed to start {name}: {message}");
    public string SessionExited(int exitCode) => T(
        $"[进程已退出，代码 {exitCode}]",
        $"[Process exited with code {exitCode}]");
    public string PasteTitle => T("粘贴多行文本", "Paste multiple lines");
    public string PasteContent(int lines) => T(
        $"剪贴板中包含 {lines} 行文本，确认要粘贴到终端吗？",
        $"The clipboard contains {lines} lines of text. Paste into the terminal?");
    public string PastePrimary => T("粘贴", "Paste");
}
