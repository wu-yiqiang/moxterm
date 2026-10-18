using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace MoxTerm.Services;

/// <summary>
/// 可持久化的应用设置。
/// </summary>
public sealed class AppSettings
{
    /// <summary>应用主题：<c>Default</c> / <c>Light</c> / <c>Dark</c>。</summary>
    public string Theme { get; set; } = "Default";

    /// <summary>界面语言：<c>Default</c> 跟随系统，<c>zh-CN</c> / <c>en-US</c> 固定语言。</summary>
    public string Language { get; set; } = "Default";

    /// <summary>终端字体大小。</summary>
    public double FontSize { get; set; } = 14;

    /// <summary>行高倍数（相对字符高度）。</summary>
    public double LineHeight { get; set; } = 1.0;

    /// <summary>终端字体族名；<c>null</c> 表示使用内置的默认字体链。</summary>
    public string? FontFamily { get; set; }

    /// <summary>
    /// 终端配色方案，取值对应 <see cref="TerminalColorScheme.Key"/>。
    /// 默认“跟随主题”：系统深色落深色方案、系统浅色落浅色方案；用户改选具体方案后即固定。
    /// </summary>
    public string ColorScheme { get; set; } = TerminalColorScheme.FollowTheme.Key;

    /// <summary>回滚缓冲行数。</summary>
    public int ScrollbackLines { get; set; } = 2000;

    /// <summary>光标形状，取值对应 <see cref="TerminalCursorStyle"/>。</summary>
    public string CursorStyle { get; set; } = nameof(TerminalCursorStyle.Block);

    /// <summary>光标是否闪烁（仅在终端获得焦点时闪烁）。</summary>
    public bool CursorBlink { get; set; } = true;

    /// <summary>新建会话时默认使用的 Shell 名称。</summary>
    public string? DefaultShell { get; set; }

    /// <summary>新会话的起始目录；<c>null</c> 表示沿用进程当前目录。</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// 终端后端，取值对应 <see cref="TerminalBackendKind"/>。
    /// 默认 ConPTY：管道模式下 PowerShell 用不了 PSReadLine（退格/方向键失效），仅作旧系统兼容兜底。
    /// </summary>
    public string Backend { get; set; } = nameof(TerminalBackendKind.ConPty);

    /// <summary>粘贴含换行的内容前是否弹窗确认。</summary>
    public bool ConfirmMultilinePaste { get; set; } = true;
}

/// <summary>
/// 设置读写。非打包应用无法使用 <c>ApplicationData.Current</c>，
/// 因此落地到 <c>%LOCALAPPDATA%\MoxTerm\settings.json</c>。
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;

    public SettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MoxTerm");

        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "settings.json");
    }

    /// <summary>当前设置。修改后需调用 <see cref="Save"/> 持久化。</summary>
    public AppSettings Current { get; private set; } = new();

    /// <summary>设置发生变化（已保存）后触发。</summary>
    public event EventHandler? Changed;

    public void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var json = File.ReadAllText(_filePath);
            Current = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 配置损坏时回退到默认值，不阻塞启动
            System.Diagnostics.Debug.WriteLine($"[MoxTerm] 读取设置失败，使用默认值: {ex.Message}");
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Current, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[MoxTerm] 保存设置失败: {ex.Message}");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>把设置中的主题字符串解析成 <see cref="ElementTheme"/>。</summary>
    public ElementTheme ResolveTheme() => Current.Theme switch
    {
        "Light" => ElementTheme.Light,
        "Dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>把所有设置恢复为默认值并持久化。</summary>
    public void Reset()
    {
        Current = new AppSettings();
        Save();
    }
}
