using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MoxTerm.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

// Windows.System 里也有同名的 DispatcherQueueTimer，这里显式指向 WinUI 的实现
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;


namespace MoxTerm.Controls;

/// <summary>
/// 终端显示与输入控件。
/// <para>
/// 结构是“屏幕缓冲区 + 等宽文本渲染”：<see cref="TerminalEmulator"/> 把 Shell 输出的
/// 控制序列解析成字符网格，再由一个只读 <see cref="TextBlock"/> 呈现。输入则反向走
/// <see cref="KeyDown"/> / <see cref="CharacterReceived"/>，映射成终端序列写回 Shell。
/// </para>
/// </summary>
public sealed partial class TerminalView : UserControl
{
    private const int RenderIntervalMilliseconds = 33;
    private const int BlinkIntervalMilliseconds = 530;

    private readonly TerminalEmulator _emulator = new();
    private readonly DispatcherQueueTimer _renderTimer;
    private readonly DispatcherQueueTimer _blinkTimer;

    private ITerminalSession? _session;
    private bool _started;
    private bool _renderPending;
    private bool _passwordSent;
    private bool _usernameSent;
    private double _fontSize = 14;
    private double _lineHeightFactor = 1.0;
    private double _cellWidth = 8;
    private double _lineHeight = 18;
    private bool _cursorBlink = true;
    private bool _blinkOn = true;

    public TerminalView()
    {
        InitializeComponent();

        _renderTimer = DispatcherQueue.CreateTimer();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(RenderIntervalMilliseconds);
        _renderTimer.IsRepeating = true;
        _renderTimer.Tick += OnRenderTick;

        _blinkTimer = DispatcherQueue.CreateTimer();
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(BlinkIntervalMilliseconds);
        _blinkTimer.IsRepeating = true;
        _blinkTimer.Tick += OnBlinkTick;

        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        GotFocus += OnFocusChanged;
        LostFocus += OnFocusChanged;

        // ScrollViewer 会先处理方向键用于滚动，这里需要连“已处理”的事件也收下来
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown), handledEventsToo: true);
        CharacterReceived += OnCharacterReceived;
        PointerPressed += OnPointerPressed;
    }

    /// <summary>本会话要启动的 Shell。</summary>
    public ShellProfile Profile { get; set; } = ShellProfile.CommandPrompt;

    /// <summary>驱动 Shell 的后端。默认 ConPTY：PowerShell 走 PSReadLine，退格/方向键/补全才完整可用。</summary>
    public TerminalBackend Backend { get; set; } = TerminalBackend.ConPty;


    /// <summary>清空屏幕与回滚缓冲。</summary>
    public void Clear()
    {
        _emulator.Clear();
        _renderPending = true;
    }

    /// <summary>
    /// 应用外观与终端设置：字号、字体、行高、配色、光标、回滚缓冲。
    /// 字体相关项会触发重新测量字符尺寸并同步会话行列数。
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        _fontSize = Math.Clamp(settings.FontSize, 8, 32);
        _lineHeightFactor = Math.Clamp(settings.LineHeight, 1.0, 2.0);

        var font = TerminalFont.FromFamily(settings.FontFamily);
        Screen.FontSize = _fontSize;
        Screen.FontFamily = font.Family is null ? DefaultFontFamily() : new FontFamily(font.Family);

        _emulator.MaxScrollback = Math.Max(0, settings.ScrollbackLines);
        _emulator.CursorGlyph = TerminalCursorOption.FromName(settings.CursorStyle).Glyph;

        // 用 ActualTheme 而不是设置里的主题字符串：跟随系统时它才拿得到真实生效的深浅色
        ApplyColorScheme(TerminalColorScheme.Resolve(settings.ColorScheme, ActualTheme));

        _cursorBlink = settings.CursorBlink;
        UpdateBlinkState();

        MeasureCell();
        ResizeSession();
        _renderPending = true;
    }

    /// <summary>结束会话并释放 Shell 进程。</summary>
    public void Dispose()
    {
        _renderTimer.Stop();
        _blinkTimer.Stop();

        if (_session is null)
        {
            return;
        }

        _session.OutputReceived -= OnSessionOutput;
        _session.Exited -= OnSessionExited;
        _session.Dispose();
        _session = null;
    }

    /// <summary>默认字体链定义在 App.xaml 的资源里，这里取不到时退回 Consolas。</summary>
    private static FontFamily DefaultFontFamily() =>
        Application.Current.Resources["TerminalFontFamily"] as FontFamily ?? new FontFamily("Consolas");

    private void ApplyColorScheme(TerminalColorScheme scheme)
    {
        // 直接写本地值：XAML 里的 ThemeResource 只是初始值，这里起覆盖作用
        Surface.Background = new SolidColorBrush(scheme.Background);
        Screen.Foreground = new SolidColorBrush(scheme.Foreground);
    }

    /// <summary>只在获得焦点时闪烁；失去焦点时保持常亮，顺带省掉空闲时的重绘。</summary>
    private void UpdateBlinkState()
    {
        var active = _cursorBlink && _started && FocusState != FocusState.Unfocused;

        if (active)
        {
            _blinkTimer.Start();
        }
        else
        {
            _blinkTimer.Stop();
            _blinkOn = true;
        }

        _renderPending = true;
    }

    private void OnBlinkTick(DispatcherQueueTimer sender, object args)
    {
        _blinkOn = !_blinkOn;
        _renderPending = true;
    }

    private void OnFocusChanged(object sender, RoutedEventArgs e) => UpdateBlinkState();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            // 切换标签页回来时 Loaded 会再次触发，但会话已经在跑，不能重启
            return;
        }

        _started = true;

        ApplySettings(App.Settings.Current);

        Focus(FocusState.Programmatic);
        StartSession();
        _renderTimer.Start();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ResizeSession();

    private void StartSession()
    {
        var profile = ResolveProfile();

        // Telnet / 串口由各自的会话类直连对端，其余走本地进程（ConPTY / 管道）
        _session = profile.Transport switch
        {
            SessionTransport.Telnet => new TelnetTerminalSession(),
            SessionTransport.Serial => new SerialTerminalSession(),
            _ => Backend.CreateSession(),
        };
        _session.OutputReceived += OnSessionOutput;
        _session.Exited += OnSessionExited;

        var (columns, rows) = CalculateGridSize();

        try
        {
            _session.Start(profile, columns, rows);
        }
        catch (Exception ex)
        {
            // 启动失败时把原因直接显示在终端里，而不是让页面崩掉
            _emulator.Feed($"\r\n{Loc.Instance.SessionStartFailed(profile.Name, ex.Message)}\r\n");
            _renderPending = true;
        }
    }

    /// <summary>设置里的起始目录优先于 Shell 配置自带的目录；目录不存在时忽略该设置。</summary>
    private ShellProfile ResolveProfile()
    {
        var workingDirectory = App.Settings.Current.WorkingDirectory;

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return Profile;
        }

        return Profile with { WorkingDirectory = workingDirectory };
    }

    private void OnSessionOutput(object? sender, string text)
    {
        // 输出来自 ConPTY 读取线程，必须切回 UI 线程再碰屏幕缓冲区
        DispatcherQueue.TryEnqueue(() =>
        {
            _emulator.Feed(text);
            TryAutoFillCredentials(text);
            _renderPending = true;
        });
    }

    /// <summary>
    /// 已保存账号密码时自动响应对端的登录提示：OpenSSH 不支持从命令行传密码，
    /// FTP 又会先问用户名，都只能走终端交互。每项每个会话只回填一次；
    /// 输错时对端会再次提示，由用户手动输入。
    /// </summary>
    private void TryAutoFillCredentials(string chunk)
    {
        if (_session is null)
        {
            return;
        }

        if (!_usernameSent && Profile.Username is { Length: > 0 } username && IsUsernamePrompt(chunk))
        {
            _usernameSent = true;
            _session.Write(username + "\r");
        }

        if (!_passwordSent && Profile.Password is { Length: > 0 } password && IsPasswordPrompt(chunk))
        {
            _passwordSent = true;
            _session.Write(password + "\r");
        }
    }

    private static bool IsPasswordPrompt(string chunk) =>
        chunk.Contains("password:", StringComparison.OrdinalIgnoreCase) || chunk.Contains("密码");

    /// <summary>覆盖 ssh 之外的常见问法：FTP 的 <c>User (host:(none)):</c>、login as、login:。</summary>
    private static bool IsUsernamePrompt(string chunk) =>
        chunk.Contains("user (", StringComparison.OrdinalIgnoreCase)
        || chunk.Contains("username", StringComparison.OrdinalIgnoreCase)
        || chunk.Contains("login as", StringComparison.OrdinalIgnoreCase)
        || chunk.Contains("login:", StringComparison.OrdinalIgnoreCase)
        || chunk.Contains("用户名")
        || chunk.Contains("登录");

    private void OnSessionExited(object? sender, int exitCode)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _emulator.Feed($"\r\n{Loc.Instance.SessionExited(exitCode)}\r\n");
            _renderPending = true;
        });
    }

    private void OnRenderTick(DispatcherQueueTimer sender, object args)
    {
        if (!_renderPending)
        {
            return;
        }

        _renderPending = false;
        Render();
    }

    private void Render()
    {
        // 渲染前先记住用户是否停留在底部，是的话渲染后继续跟随输出
        var wasAtBottom = Scroller.ScrollableHeight - Scroller.VerticalOffset < 24;

        Screen.Text = _emulator.GetText(showCursor: !_cursorBlink || _blinkOn);

        if (!wasAtBottom)
        {
            return;
        }

        Scroller.UpdateLayout();
        Scroller.ChangeView(null, Scroller.ScrollableHeight, null, disableAnimation: true);
    }

    private void ResizeSession()
    {
        var (columns, rows) = CalculateGridSize();

        if (_emulator.Columns == columns && _emulator.Rows == rows)
        {
            return;
        }

        _emulator.Resize(columns, rows);
        _session?.Resize(columns, rows);
        _renderPending = true;
    }

    private (int Columns, int Rows) CalculateGridSize()
    {
        var paddingX = Scroller.Padding.Left + Scroller.Padding.Right;
        var paddingY = Scroller.Padding.Top + Scroller.Padding.Bottom;

        var availableWidth = Math.Max(0, Scroller.ActualWidth - paddingX);
        var availableHeight = Math.Max(0, Scroller.ActualHeight - paddingY);

        var columns = (int)Math.Floor(availableWidth / _cellWidth);
        var rows = (int)Math.Floor(availableHeight / _lineHeight);

        return (Math.Clamp(columns, 20, 500), Math.Clamp(rows, 5, 200));
    }

    /// <summary>用一个临时 TextBlock 量出等宽字体的单字符宽高。</summary>
    private void MeasureCell()
    {
        const int sampleLength = 20;

        // 字符高度量不出来时的兜底值，与字段初始默认一致
        var cellHeight = 18.0;

        var probe = new TextBlock
        {
            FontFamily = Screen.FontFamily,
            FontSize = _fontSize,
            Text = new string('M', sampleLength),
        };

        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        if (probe.DesiredSize.Width > 0)
        {
            _cellWidth = probe.DesiredSize.Width / sampleLength;
        }

        if (probe.DesiredSize.Height > 0)
        {
            cellHeight = Math.Ceiling(probe.DesiredSize.Height);
        }

        // 行高按倍数放大后再取整，保证渲染出的行距与行列数换算一致
        _lineHeight = Math.Ceiling(cellHeight * _lineHeightFactor);
        Screen.LineHeight = _lineHeight;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // 点在文本上时把焦点留给 TextBlock（用于选择），点空白处则取回键盘焦点
        if (!ReferenceEquals(e.OriginalSource, Screen))
        {
            Focus(FocusState.Pointer);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = IsKeyDown(VirtualKey.Control);
        var shift = IsKeyDown(VirtualKey.Shift);

        if (control && shift && e.Key == VirtualKey.C)
        {
            CopySelection();
            e.Handled = true;
            return;
        }

        if (control && e.Key == VirtualKey.C)
        {
            // 有选中内容时按复制处理，否则按终端惯例发送中断
            if (Screen.SelectedText.Length > 0)
            {
                CopySelection();
            }
            else
            {
                _session?.Write("\u0003");
            }

            e.Handled = true;
            return;
        }

        if (control && e.Key == VirtualKey.V)
        {
            _ = PasteAsync();
            e.Handled = true;
            return;
        }

        if (control && e.Key == VirtualKey.L)
        {
            // 清屏是 shell（PSReadLine/readline）的功能，转发 0x0C 由它清屏并重绘提示符；
            // 只清本地缓冲区的话 shell 不知道屏幕已清，不会再画提示符
            _session?.Write("\u000c");
            e.Handled = true;
            return;
        }

        if (control || IsKeyDown(VirtualKey.Menu))
        {
            // 其余 Ctrl / Alt 组合暂不转发，交给系统处理
            return;
        }

        // 本地进程的管道后端要发 0x08（cmd 的行编辑器才认）；
        // ConPTY 与 Telnet / 串口都按 VT 约定发 0x7F
        var backspace = Profile.Transport == SessionTransport.Process && Backend.Kind != TerminalBackendKind.ConPty
            ? "\b"
            : "\x7f";

        var sequence = e.Key switch
        {
            VirtualKey.Enter => "\r",
            VirtualKey.Back => backspace,
            VirtualKey.Tab => "\t",
            VirtualKey.Escape => "\u001b",
            VirtualKey.Up => "\u001b[A",
            VirtualKey.Down => "\u001b[B",
            VirtualKey.Right => "\u001b[C",
            VirtualKey.Left => "\u001b[D",
            VirtualKey.Home => "\u001b[H",
            VirtualKey.End => "\u001b[F",
            VirtualKey.Delete => "\u001b[3~",
            VirtualKey.PageUp => "\u001b[5~",
            VirtualKey.PageDown => "\u001b[6~",
            _ => null,
        };

        if (sequence is null)
        {
            return;
        }

        _session?.Write(sequence);
        e.Handled = true;
    }

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        var character = e.Character;

        // 控制字符（回车、退格、Esc 等）统一由 KeyDown 负责，避免重复发送
        if (character < ' ' || character == '\u007f')
        {
            return;
        }

        if (IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.Menu))
        {
            return;
        }

        _session?.Write(character.ToString());
        e.Handled = true;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void CopySelection()
    {
        // TextBlock 没有 SelectionLength，用选中文本是否为空来判断
        var selected = Screen.SelectedText;
        if (string.IsNullOrEmpty(selected))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(selected);
        Clipboard.SetContent(package);
    }

    private async Task PasteAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                return;
            }

            var text = await content.GetTextAsync();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (App.Settings.Current.ConfirmMultilinePaste && ContainsLineBreak(text) && !await ConfirmPasteAsync(text))
            {
                return;
            }

            // 统一成回车换行，避免多行粘贴被 Shell 当成“未提交的换行”
            _session?.Write(text.Replace("\r\n", "\r").Replace('\n', '\r'));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MoxTerm] 粘贴失败: {ex.Message}");
        }
    }

    private static bool ContainsLineBreak(string text) => text.Contains('\r') || text.Contains('\n');

    /// <summary>粘贴多行内容前弹窗确认，避免误把整段脚本提交给 Shell。</summary>
    private async Task<bool> ConfirmPasteAsync(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Length;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Instance.PasteTitle,
            Content = Loc.Instance.PasteContent(lines),
            PrimaryButtonText = Loc.Instance.PastePrimary,
            CloseButtonText = Loc.Instance.Cancel,
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
