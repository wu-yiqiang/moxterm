using System.Text.RegularExpressions;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
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

    /// <summary>终端输出里可点击的链接：http(s):// 或 www. 开头的地址。</summary>
    private static readonly Regex LinkPattern = new(
        @"(?:https?://|www\.)[^\s<>""'`]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>紧贴链接末尾、通常属于句子的标点，命中链接时一并去掉。</summary>
    private static readonly char[] LinkTrailingPunctuation =
        ['.', ',', ';', ':', '!', '?', ')', ']', '}', '"', '\'', '。', '，', '；', '：', '！', '？', '）', '】', '》'];

    private readonly TerminalEmulator _emulator = new();
    private readonly DispatcherQueueTimer _renderTimer;
    private readonly DispatcherQueueTimer _blinkTimer;
    private readonly MenuFlyout _paneMenu;

    private ITerminalSession? _session;
    private bool _started;
    private bool _renderPending;
    private bool _passwordSent;
    private bool _usernameSent;
    private bool _copyOnSelect;
    private bool _selecting;
    private string? _lastLinkScanText;
    private string? _hoveredLink;
    private int _hoveredLinkRow;
    private int _hoveredLinkColumn;
    private int _hoveredLinkLength;
    private InputSystemCursor? _handCursor;
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

        // TextBlock 开启文本选择后会自行处理指针事件，链接的悬停/点击只能收听“已处理”事件
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnScreenPointerPressed), handledEventsToo: true);
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnScreenPointerMoved), handledEventsToo: true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnScreenPointerReleased), handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnScreenPointerCaptureLost), handledEventsToo: true);
        PointerExited += OnScreenPointerExited;
        Scroller.ViewChanged += OnScrollerViewChanged;

        // 键盘等方式改动选中范围时同样生效；鼠标拖选期间由松手事件统一复制
        Screen.SelectionChanged += OnScreenSelectionChanged;

        // 右键菜单：分屏 / 关闭分屏 / 复制。TextBlock 开启文本选择后自带右键菜单，
        // ContextFlyout 和 SelectionFlyout 都指向同一个菜单，避免右键弹出两套。
        _paneMenu = CreatePaneMenu();
        Screen.ContextFlyout = _paneMenu;
        Screen.SelectionFlyout = _paneMenu;
        AddHandler(RightTappedEvent, new RightTappedEventHandler(OnPaneMenuRequested), handledEventsToo: true);

        LinkHintText.Text = Loc.Instance.OpenLinkHint;
    }

    /// <summary>宿主导入：当前标签页内是否还有其它窗格，决定“关闭此分屏”是否可用。</summary>
    public bool CanClosePane { get; set; }

    /// <summary>右键菜单里的分屏操作，由宿主（TerminalPaneHost）订阅处理。</summary>
    public event EventHandler<TerminalPaneEventArgs>? PaneActionRequested;

    private MenuFlyout CreatePaneMenu()
    {
        var copy = new MenuFlyoutItem { Icon = new FontIcon { Glyph = "\uE8C8" } };
        copy.Click += (_, _) => CopySelection();

        // 分屏图标用 Segoe Fluent Icons 的 CollatePortraitSeparated / CollateLandscapeSeparated
        var splitLeftRight = new MenuFlyoutItem { Icon = FluentIcon("\uF57D") };
        splitLeftRight.Click += (_, _) =>
            PaneActionRequested?.Invoke(this, new TerminalPaneEventArgs(TerminalPaneAction.SplitLeftRight));

        var splitTopBottom = new MenuFlyoutItem { Icon = FluentIcon("\uF5AC") };
        splitTopBottom.Click += (_, _) =>
            PaneActionRequested?.Invoke(this, new TerminalPaneEventArgs(TerminalPaneAction.SplitTopBottom));

        var closePane = new MenuFlyoutItem { Icon = new FontIcon { Glyph = "\uE711" } };
        closePane.Click += (_, _) =>
            PaneActionRequested?.Invoke(this, new TerminalPaneEventArgs(TerminalPaneAction.Close));

        var menu = new MenuFlyout();
        menu.Items.Add(copy);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(splitLeftRight);
        menu.Items.Add(splitTopBottom);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(closePane);

        // 文案与可用状态每次弹出时刷新：语言可能已切换，选中内容与窗格数量也会变
        menu.Opening += (_, _) =>
        {
            var loc = Loc.Instance;
            copy.Text = loc.Copy;
            copy.IsEnabled = !string.IsNullOrEmpty(Screen.SelectedText);
            splitLeftRight.Text = loc.SplitLeftRight;
            splitTopBottom.Text = loc.SplitTopBottom;
            closePane.Text = loc.ClosePane;
            closePane.IsEnabled = CanClosePane;
        };

        return menu;
    }

    /// <summary>菜单图标统一用 Segoe Fluent Icons；分屏图标只在该字体里有。</summary>
    private static FontIcon FluentIcon(string glyph) => new()
    {
        Glyph = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons"),
    };

    /// <summary>
    /// 在终端任意位置右键弹出分屏菜单。TextBlock 会先用自己的 SelectionFlyout 弹出同一个实例，
    /// 这里只兜住没有选中文本、TextBlock 不处理右键的情况。
    /// </summary>
    private void OnPaneMenuRequested(object sender, RightTappedRoutedEventArgs e)
    {
        if (_paneMenu.IsOpen)
        {
            return;
        }

        e.Handled = true;
        _paneMenu.ShowAt(Surface, new FlyoutShowOptions
        {
            Position = e.GetPosition(Surface),
            ShowMode = FlyoutShowMode.Transient,
        });
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
        _copyOnSelect = settings.CopyOnSelect;
        _lastLinkScanText = null;
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
        // 拖选期间重设 Text 会把 TextBlock 的选中状态清掉（光标闪烁/新输出都会触发重绘），
        // 先跳过，松手后 _renderPending 仍在，下一次定时器再补上
        if (!_renderPending || _selecting)
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

        // 逐字比较替掉整段赋值：内容没变时不动 Text，避免无谓的布局与选中状态丢失
        var text = _emulator.GetText(showCursor: !_cursorBlink || _blinkOn);
        if (text != Screen.Text)
        {
            Screen.Text = text;
        }

        UpdateLinkUnderlines();

        if (!wasAtBottom)
        {
            return;
        }

        Scroller.UpdateLayout();
        Scroller.ChangeView(null, Scroller.ScrollableHeight, null, disableAnimation: true);
    }

    /// <summary>
    /// 内容有变化时重扫一遍链接并重建下划线。光标闪烁只改变一个方块字符，
    /// 因此用不含光标的文本做比较，闪烁不会触发重复扫描。
    /// </summary>
    private void UpdateLinkUnderlines()
    {
        var plain = _emulator.GetText(showCursor: false);

        if (plain == _lastLinkScanText)
        {
            return;
        }

        _lastLinkScanText = plain;
        RebuildLinkUnderlines();
    }

    /// <summary>所有疑似链接的字符画上一条下划线，位置按等宽字符网格换算。</summary>
    private void RebuildLinkUnderlines()
    {
        LinkUnderlines.Children.Clear();

        for (var row = 0; row < _emulator.LineCount; row++)
        {
            var line = _emulator.GetLineText(row);

            foreach (Match match in LinkPattern.Matches(line))
            {
                // 与命中测试保持一致：去掉紧贴链接末尾的句读标点
                var length = match.Length;
                while (length > 0 && Array.IndexOf(LinkTrailingPunctuation, match.Value[length - 1]) >= 0)
                {
                    length--;
                }

                if (length == 0)
                {
                    continue;
                }

                var underline = new Rectangle
                {
                    Width = length * _cellWidth,
                    Height = 1,
                    Fill = Screen.Foreground,
                };

                Canvas.SetLeft(underline, match.Index * _cellWidth);
                Canvas.SetTop(underline, row * _lineHeight + _lineHeight - 4);
                LinkUnderlines.Children.Add(underline);
            }
        }
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

    /// <summary>在文本区域按下就进入拖选状态，期间暂停重绘以保住 TextBlock 的选中内容。</summary>
    private void OnScreenPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // 点在“打开链接”提示上时交给提示自己的点击逻辑，不进入拖选
        if (IsPointInLinkHint(e.GetCurrentPoint(Surface).Position))
        {
            return;
        }

        var position = e.GetCurrentPoint(Screen).Position;

        // 不能依赖 IsLeftButtonPressed：这里收到的是被 TextBlock 处理过的事件，按键状态可能已不可见
        if (position.X >= 0 && position.Y >= 0
            && position.X <= Screen.ActualWidth && position.Y <= Screen.ActualHeight)
        {
            _selecting = true;
            LinkHint.Visibility = Visibility.Collapsed;
        }
    }

    private void OnScreenPointerCaptureLost(object sender, PointerRoutedEventArgs e) => _selecting = false;

    /// <summary>选中范围变化时复制；鼠标拖选期间跳过，等松手再统一写剪贴板。</summary>
    private void OnScreenSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_copyOnSelect && !_selecting)
        {
            CopySelection();
        }
    }

    /// <summary>
    /// 指针悬停在链接上时换成手型光标并跟随显示“打开链接”提示。
    /// 链接与提示之间留一块过渡区域，指针移向提示时不会让提示先一步消失，从而能直接点提示打开。
    /// </summary>
    private void OnScreenPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var surfacePoint = e.GetCurrentPoint(Surface).Position;

        // 指针已经在提示上：保持现状，交给提示自身的 PointerEntered / Tapped
        if (IsPointInLinkHint(surfacePoint))
        {
            return;
        }

        var screenPoint = e.GetCurrentPoint(Screen).Position;

        if (TryGetLinkAt(screenPoint, out var url, out var row, out var column, out var length))
        {
            var changed = _hoveredLink != url
                || _hoveredLinkRow != row
                || _hoveredLinkColumn != column
                || _hoveredLinkLength != length;

            _hoveredLink = url;
            _hoveredLinkRow = row;
            _hoveredLinkColumn = column;
            _hoveredLinkLength = length;

            SetHandCursor(true);

            // 提示吸附在链接旁，光标在同一链接内移动时不跟着跑
            if (changed || LinkHint.Visibility != Visibility.Visible)
            {
                ShowLinkHintForHoveredLink();
            }

            return;
        }

        if (_hoveredLink is not null && IsInLinkKeepZone(screenPoint, surfacePoint))
        {
            return;
        }

        ClearLinkHover();
    }

    private void OnScreenPointerExited(object sender, PointerRoutedEventArgs e) => ClearLinkHover();

    private void OnLinkHintPointerEntered(object sender, PointerRoutedEventArgs e) => SetHandCursor(true);

    /// <summary>点击“打开链接”提示本身也能打开当前链接。</summary>
    private void OnLinkHintTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;

        if (_hoveredLink is { } url)
        {
            _ = OpenLinkAsync(url);
        }
    }

    private void SetHandCursor(bool hand)
    {
        if ((ProtectedCursor is not null) == hand)
        {
            return;
        }

        ProtectedCursor = hand
            ? _handCursor ??= InputSystemCursor.Create(InputSystemCursorShape.Hand)
            : null;
    }

    private void ClearLinkHover()
    {
        _hoveredLink = null;
        SetHandCursor(false);
        LinkHint.Visibility = Visibility.Collapsed;
    }

    /// <summary>链接本身及提示周围的一圈过渡区域，指针在其中时暂不收起提示。</summary>
    private bool IsInLinkKeepZone(Point screenPoint, Point surfacePoint)
    {
        if (IsPointInLinkHint(surfacePoint))
        {
            return true;
        }

        var left = _hoveredLinkColumn * _cellWidth;
        var right = (_hoveredLinkColumn + _hoveredLinkLength) * _cellWidth;
        var top = _hoveredLinkRow * _lineHeight;
        var bottom = top + _lineHeight;

        return screenPoint.X >= left - 24 && screenPoint.X <= right + 24
            && screenPoint.Y >= top - 8 && screenPoint.Y <= bottom + 56;
    }

    private bool IsPointInLinkHint(Point surfacePoint)
    {
        if (LinkHint.Visibility != Visibility.Visible)
        {
            return false;
        }

        var left = LinkHint.Margin.Left;
        var top = LinkHint.Margin.Top;

        return surfacePoint.X >= left && surfacePoint.X <= left + LinkHint.ActualWidth
            && surfacePoint.Y >= top && surfacePoint.Y <= top + LinkHint.ActualHeight;
    }

    /// <summary>
    /// 把提示吸附在当前链接的正下方（底部空间不足时放上方）。
    /// 链接矩形先在内容坐标里算好，再换算到 Surface 坐标，滚动/内边距都能自动对上。
    /// </summary>
    private void ShowLinkHintForHoveredLink()
    {
        var linkLeft = _hoveredLinkColumn * _cellWidth;
        var linkTop = _hoveredLinkRow * _lineHeight;

        var origin = Screen.TransformToVisual(Surface).TransformPoint(new Point(linkLeft, linkTop));

        if (LinkHint.Visibility != Visibility.Visible)
        {
            // 先显示才能量到提示的实际宽高，用于边界收拢
            LinkHint.Visibility = Visibility.Visible;
            LinkHint.UpdateLayout();
        }

        var width = LinkHint.ActualWidth;
        var height = LinkHint.ActualHeight;
        var left = Math.Clamp(origin.X, 8, Math.Max(8, Surface.ActualWidth - width - 8));

        var top = origin.Y + _lineHeight + 4;
        if (top + height > Surface.ActualHeight - 8)
        {
            top = Math.Max(8, origin.Y - height - 4);
        }

        LinkHint.Margin = new Thickness(left, top, 0, 0);
    }

    /// <summary>滚动时链接会移动，吸附在它旁边的提示跟着重新定位。</summary>
    private void OnScrollerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_hoveredLink is not null)
        {
            ShowLinkHintForHoveredLink();
        }
    }

    /// <summary>单击链接用系统默认浏览器打开；拖选文本时不触发。</summary>
    private void OnScreenPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Screen);

        // 任何按键松开都结束拖选状态，否则右键松开会把 _selecting 卡在 true
        _selecting = false;

        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
        {
            return;
        }

        // 松开在提示上时由 OnLinkHintTapped 负责打开，避免同时命中提示下方的文本链接
        if (IsPointInLinkHint(e.GetCurrentPoint(Surface).Position))
        {
            return;
        }

        // 拖选（以及双击选词的第二下）会留下选中文本，此时用户的意图是选择而不是打开
        if (Screen.SelectedText.Length > 0)
        {
            // 开启“选中即复制”时，选完松手就写入剪贴板
            if (_copyOnSelect)
            {
                CopySelection();
            }

            return;
        }

        if (LinkAt(point.Position) is { } url)
        {
            _ = OpenLinkAsync(url);
        }
    }

    private static async Task OpenLinkAsync(string url)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[MoxTerm] 打开链接失败: {ex.Message}");
        }
    }

    /// <summary>按字符网格把指针位置换算成行列，返回该处链接的绝对地址。</summary>
    private string? LinkAt(Point position) =>
        TryGetLinkAt(position, out var url, out _, out _, out _) ? url : null;

    /// <summary>
    /// 按字符网格把指针位置换算成行列，返回该处链接的绝对地址与所在位置。
    /// 终端是等宽无换行渲染，行列可由 <see cref="_cellWidth"/> / <see cref="_lineHeight"/> 直接算出。
    /// </summary>
    private bool TryGetLinkAt(Point position, out string url, out int row, out int column, out int length)
    {
        url = string.Empty;
        row = 0;
        column = 0;
        length = 0;

        if (_cellWidth <= 0 || _lineHeight <= 0 || position.X < 0 || position.Y < 0)
        {
            return false;
        }

        // 指针落在文本区域外（如滚动条、下方空白）时不参与命中
        if (position.X > Screen.ActualWidth || position.Y > Screen.ActualHeight)
        {
            return false;
        }

        row = (int)(position.Y / _lineHeight);
        var pointerColumn = (int)(position.X / _cellWidth);
        var line = _emulator.GetLineText(row);

        if (pointerColumn >= line.Length)
        {
            return false;
        }

        foreach (Match match in LinkPattern.Matches(line))
        {
            // 句末标点常紧贴链接，命中时去掉，避免把 "https://a.com." 当成地址
            length = match.Length;
            while (length > 0 && Array.IndexOf(LinkTrailingPunctuation, match.Value[length - 1]) >= 0)
            {
                length--;
            }

            if (pointerColumn < match.Index || pointerColumn >= match.Index + length)
            {
                continue;
            }

            url = match.Value[..length];

            if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                url = uri.AbsoluteUri;
                // 返回链接起始列，供提示吸附在链接旁边使用
                column = match.Index;
                return true;
            }

            url = string.Empty;
        }

        length = 0;
        return false;
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
