using System.IO.Ports;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using MoxTerm.Controls;
using MoxTerm.Services;

namespace MoxTerm.Views;

/// <summary>
/// 终端工作区：一个 <see cref="TabView"/>，每个标签页承载一个独立的 Shell 会话。
/// </summary>
public sealed partial class TerminalPage : Page
{
    /// <summary>串口默认波特率，也是下拉框的默认选中项。</summary>
    private const int DefaultBaudRate = 115200;

    /// <summary>串口波特率下拉框的可选项，允许手动输入其它取值。</summary>
    private static readonly int[] BaudRates = [9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600];

    // 菜单图标（Segoe Fluent）：终端、密钥（已保存会话）、新建、编辑、删除
    private const string GlyphTerminal = "\uE756";
    private const string GlyphSession = "\uE8D7";
    private const string GlyphNewConnection = "\uE710";
    private const string GlyphEdit = "\uE70F";
    private const string GlyphDelete = "\uE74D";

    private bool _windowHooked;

    /// <summary>
    /// 当前活动实例。页面设置了 <c>NavigationCacheMode=Required</c>，全应用只会有一份，
    /// 设置页通过它把设置变化实时应用到所有已打开的会话。
    /// </summary>
    public static TerminalPage? Current { get; private set; }

    /// <summary>界面文案源；x:Bind 绑定到它的属性，语言切换时自动刷新。</summary>
    private Loc Loc => Loc.Instance;

    public TerminalPage()
    {
        InitializeComponent();
        Current = this;

        Loaded += OnLoaded;
        Tabs.Loaded += OnTabsLoaded;
    }

    /// <summary>
    /// TabView 模板里承载标签内容的 ContentPresenter 所在行会按内容高度收缩，
    /// 对齐属性（VerticalContentAlignment）改不动它，只能从可视化树里强制拉伸。
    /// </summary>
    private void OnTabsLoaded(object sender, RoutedEventArgs e)
    {
        if (FindVisualChild<ContentPresenter>(Tabs, "TabContentPresenter") is not { } presenter)
        {
            return;
        }

        presenter.VerticalAlignment = VerticalAlignment.Stretch;

        if (presenter.Parent is Grid grid)
        {
            var row = Grid.GetRow(presenter);
            if (row < grid.RowDefinitions.Count)
            {
                grid.RowDefinitions[row].Height = new GridLength(1, GridUnitType.Star);
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T match && match.Name == name)
            {
                return match;
            }

            if (FindVisualChild<T>(child, name) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>默认 Shell：设置页配置的优先，否则取系统发现的第一个。</summary>
    private static ShellProfile DefaultShell()
    {
        var shells = ShellProfile.Discover();
        return shells.FirstOrDefault(shell => shell.Name == App.Settings.Current.DefaultShell) ?? shells[0];
    }

    /// <summary>
    /// 把当前设置应用到所有已打开的会话（字号、字体、行高、配色、光标、回滚缓冲）。
    /// </summary>
    public void ApplySettings()
    {
        foreach (var view in EnumerateTerminalViews())
        {
            view.ApplySettings(App.Settings.Current);
        }

        // 活动标签底色跟随终端配色，与下方内容连成一体（Windows Terminal 风格）；
        // 未选中标签保持透明（透出 Mica），自然形成明暗对比
        var selected = new SolidColorBrush(
            TerminalColorScheme.Resolve(App.Settings.Current.ColorScheme, ActualTheme).Background);
        Tabs.Resources["TabViewItemHeaderBackgroundSelected"] = selected;
        Tabs.Resources["TabViewItemHeaderBackgroundSelectedPointerOver"] = selected;
        Tabs.Resources["TabViewItemHeaderBackgroundSelectedPressed"] = selected;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // App.Shell 在窗口构造函数返回后才赋值，所以这里（首次 Loaded）才拿得到
        if (!_windowHooked && App.Shell is { } window)
        {
            window.Closed += OnWindowClosed;
            _windowHooked = true;
        }

        if (Tabs.TabItems.Count == 0)
        {
            CreateSession(DefaultShell());
        }

        // 启动时也要同步一次设置：活动标签的配色画刷在这里赋值，
        // 不调用的话只有 XAML 兜底色（与 Mica 相近，看起来像没标注）
        ApplySettings();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // 关窗时回收全部会话，否则 shell 子进程会残留
        foreach (var view in EnumerateTerminalViews())
        {
            view.Dispose();
        }

        Tabs.TabItems.Clear();
        Current = null;
    }

    private void CreateSession(ShellProfile profile)
    {
        var tab = new TabViewItem
        {
            Header = profile.Name,
            IconSource = new FontIconSource { Glyph = "\uE756" },
            Content = new TerminalView
            {
                Profile = profile,
                Backend = TerminalBackend.FromName(App.Settings.Current.Backend),
            },
            IsClosable = true,
        };

        Tabs.TabItems.Add(tab);
        Tabs.SelectedItem = tab;
    }

    /// <summary>
    /// Tab 栏加号：列出本地 Shell、已保存的会话，以及“新建连接”入口——
    /// 协议（SSH / Telnet / 串口）在新建连接对话框里选择，菜单里不再逐个列出。
    /// </summary>
    private void OnAddTabButtonClick(TabView sender, object args)
    {
        var flyout = new MenuFlyout();

        foreach (var profile in ShellProfile.Discover())
        {
            var captured = profile;
            flyout.Items.Add(MenuItem(profile.Name, GlyphTerminal, () => CreateSession(captured)));
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        // 已保存的连接：左键直接连接；右键（或 Shift+F10）弹出「编辑 / 删除」菜单。
        // 不能挂 ContextFlyout：父菜单在右键按下时会关闭，连带把嵌套弹层一起带走；
        // 所以先收起加号菜单，再在光标处独立弹出一个小菜单。
        var savedSessions = App.Settings.Current.SavedSessions;

        foreach (var saved in savedSessions)
        {
            var captured = saved;
            var item = MenuItem(saved.Name, GlyphSession, () => ConnectSaved(captured));
            item.Template = (ControlTemplate)XamlReader.Load(SavedSessionItemTemplate);

            // 行内“编辑 / 删除”按钮：模板在弹层首次打开（Loaded）后才进可视树，挂一次即可。
            // 嵌套 lambda 的参数不要用单下划线命名：内层的 `_ = Task` 弃元赋值会绑定到外层同名参数
            item.Loaded += (itemSender, itemArgs) =>
            {
                var row = (MenuFlyoutItem)itemSender;

                if (FindVisualChild<Button>(row, "EditButton") is { } editButton && editButton.Tag is not "hooked")
                {
                    editButton.Tag = "hooked";
                    editButton.Click += (buttonSender, buttonArgs) =>
                    {
                        flyout.Hide();
                        _ = ShowConnectDialogAsync(captured);
                    };
                }

                if (FindVisualChild<Button>(row, "DeleteButton") is { } deleteButton && deleteButton.Tag is not "hooked")
                {
                    deleteButton.Tag = "hooked";
                    deleteButton.Click += (buttonSender, buttonArgs) =>
                    {
                        flyout.Hide();
                        _ = DeleteSavedAsync(captured);
                    };
                }
            };

            item.ContextRequested += (itemSender, e) =>
            {
                e.Handled = true;
                e.TryGetPosition(this, out var position);
                _ = ShowSessionMenuAsync(captured, position);
            };
            flyout.Items.Add(item);
        }

        if (savedSessions.Count > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        flyout.Items.Add(MenuItem(Loc.NewConnection, GlyphNewConnection, () => _ = ShowConnectDialogAsync()));

        // 锚定到加号按钮（模板部件名 AddButton），找不到就退到整个 TabView
        FrameworkElement anchor = FindVisualChild<Button>(Tabs, "AddButton") is { } button ? button : sender;
        flyout.ShowAt(anchor);
    }

    /// <summary>创建一个菜单项：文本 + 图标 + 点击行为。</summary>
    private MenuFlyoutItem MenuItem(string text, string glyph, Action onClick)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph },
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>
    /// 已保存会话行的模板：名称右侧放两个行内按钮（编辑 / 删除）。
    /// 根 Grid 命名为 LayoutRoot、文本为 TextBlock，与默认样式的 VisualState 对齐，
    /// 悬停高亮和前景色动画照常生效。
    /// </summary>
    private const string SavedSessionItemTemplate = """
        <ControlTemplate
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            TargetType="MenuFlyoutItem">
          <Grid x:Name="LayoutRoot" Background="Transparent">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="Auto"/>
              <ColumnDefinition Width="*"/>
              <ColumnDefinition Width="Auto"/>
              <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <ContentPresenter x:Name="IconPresenter" Content="{TemplateBinding Icon}" Margin="12,0,0,0" VerticalAlignment="Center"/>
            <TextBlock x:Name="TextBlock" Grid.Column="1" Text="{TemplateBinding Text}" Margin="12,0,8,0" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
            <Button x:Name="EditButton" Grid.Column="2" Width="30" Height="26" Padding="0" Background="Transparent" BorderThickness="0" VerticalAlignment="Center">
              <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE70F;" FontSize="14"/>
            </Button>
            <Button x:Name="DeleteButton" Grid.Column="3" Width="30" Height="26" Padding="0" Background="Transparent" BorderThickness="0" Margin="0,0,10,0" VerticalAlignment="Center">
              <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE74D;" FontSize="14"/>
            </Button>
          </Grid>
        </ControlTemplate>
        """;

    /// <summary>会话的右键菜单：编辑、删除。先等加号菜单收起，否则新弹层会被它的关闭动画带走。</summary>
    private async Task ShowSessionMenuAsync(SavedSession saved, Windows.Foundation.Point position)
    {
        await Task.Delay(150);

        var menu = new MenuFlyout
        {
            Items =
            {
                MenuItem(Loc.Instance.EditSession, GlyphEdit, () => _ = ShowConnectDialogAsync(saved)),
                MenuItem(Loc.Instance.DeleteSession, GlyphDelete, () => _ = DeleteSavedAsync(saved)),
            },
        };
        menu.ShowAt(this, position);
    }

    /// <summary>
    /// 新建连接：先选协议，再填该协议需要的字段。SSH / SFTP / FTP 复用系统自带客户端
    /// （密码、用户名由终端在提示符处自动回填），Telnet 与串口由本应用直连；
    /// Shell / WSL 落到本地会话；其余协议只在列表里占位并提示尚未提供。
    /// 传入 <paramref name="editing"/> 时进入编辑模式：预填该会话并在确认后覆盖保存。
    /// </summary>
    private async Task ShowConnectDialogAsync(SavedSession? editing = null)
    {
        var protocols = SessionProtocol.All;

        var protocolBox = new ComboBox
        {
            Header = Loc.Instance.Protocol,
            ItemsSource = protocols.Select(protocol => protocol.Display).ToList(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var nameBox = new TextBox
        {
            Header = Loc.Instance.SshNameLabel,
            PlaceholderText = Loc.Instance.SshNamePlaceholder,
        };
        var hostBox = new TextBox
        {
            Header = Loc.Instance.SshHostLabel,
            PlaceholderText = Loc.Instance.SshHostPlaceholder,
        };
        var portBox = new TextBox
        {
            Header = Loc.Instance.SshPortLabel,
            Text = protocols[0].DefaultPort.ToString(),
        };
        var userBox = new TextBox
        {
            Header = Loc.Instance.SshUsernameLabel,
            PlaceholderText = Loc.Instance.SshUsernamePlaceholder,
        };
        var passwordBox = new PasswordBox
        {
            Header = Loc.Instance.SshPasswordLabel,
        };
        var serialPortBox = new ComboBox
        {
            Header = Loc.Instance.SerialPortLabel,
            PlaceholderText = Loc.Instance.SerialPortPlaceholder,
            IsEditable = true,
            ItemsSource = EnumerateSerialPorts(),
        };
        var baudBox = new ComboBox
        {
            Header = Loc.Instance.BaudRateLabel,
            IsEditable = true,
            ItemsSource = BaudRates,
            SelectedItem = DefaultBaudRate,
        };
        var notesBox = new TextBox
        {
            Header = Loc.Instance.SshNotesLabel,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 96,
        };
        var saveCheck = new CheckBox
        {
            Content = Loc.Instance.SshSaveSession,
            IsChecked = true,
        };

        // 名称与协议固定在最上方；连接字段按协议显隐，备注与“保存此会话”只在有连接信息的协议下出现
        var fields = new StackPanel { Spacing = 12 };
        fields.Children.Add(hostBox);
        fields.Children.Add(portBox);
        fields.Children.Add(userBox);
        fields.Children.Add(passwordBox);
        fields.Children.Add(serialPortBox);
        fields.Children.Add(baudBox);

        var details = new StackPanel { Spacing = 12 };
        details.Children.Add(notesBox);
        details.Children.Add(saveCheck);

        var hint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(nameBox);
        content.Children.Add(protocolBox);
        content.Children.Add(fields);
        content.Children.Add(details);
        content.Children.Add(hint);

        // 对话框宽度是按内容撑开的（下限 320），内容不定宽就会缩到最窄，表单很挤
        content.Width = 420;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = editing is null ? Loc.Instance.NewConnection : Loc.Instance.EditConnection,
            Content = content,
            PrimaryButtonText = editing is null ? Loc.Instance.Connect : Loc.Instance.SaveChanges,
            CloseButtonText = Loc.Instance.Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };

        SessionProtocol SelectedProtocol() =>
            protocols[Math.Clamp(protocolBox.SelectedIndex, 0, protocols.Count - 1)];

        void SyncProtocol()
        {
            var protocol = SelectedProtocol();

            Visibility Show(SessionField field) =>
                protocol.Fields.Contains(field) ? Visibility.Visible : Visibility.Collapsed;

            hostBox.Visibility = Show(SessionField.Host);
            portBox.Visibility = Show(SessionField.Port);
            userBox.Visibility = Show(SessionField.Username);
            passwordBox.Visibility = Show(SessionField.Password);
            serialPortBox.Visibility = Show(SessionField.SerialPort);
            baudBox.Visibility = Show(SessionField.BaudRate);

            details.Visibility = protocol.Fields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            hint.Visibility = protocol.Supported ? Visibility.Collapsed : Visibility.Visible;
            dialog.IsPrimaryButtonEnabled = protocol.Supported;

            if (!protocol.Supported)
            {
                // 协议尚未实现：说明原因并禁用“连接”，避免点下去没有反馈
                hint.Text = Loc.Instance.ProtocolComingSoon(protocol.Display);
            }
            else if (portBox.Visibility == Visibility.Visible)
            {
                // 端口默认值跟随协议（SSH 22 / Telnet 23）
                portBox.Text = protocol.DefaultPort.ToString();
            }
        }

        protocolBox.SelectionChanged += (_, _) => SyncProtocol();
        SyncProtocol();

        if (editing is not null)
        {
            // 先选协议（会触发 SyncProtocol 重置端口等字段），再逐项预填
            for (var i = 0; i < protocols.Count; i++)
            {
                if (protocols[i].Key == editing.Protocol)
                {
                    protocolBox.SelectedIndex = i;
                    break;
                }
            }

            nameBox.Text = editing.Name;
            hostBox.Text = editing.Host;
            portBox.Text = editing.Port > 0 ? editing.Port.ToString() : portBox.Text;
            userBox.Text = editing.Username;
            passwordBox.Password = SecretProtector.Unprotect(editing.Password);
            notesBox.Text = editing.Notes;
            serialPortBox.Text = editing.SerialPortName ?? "";
            if (editing.BaudRate > 0)
            {
                baudBox.Text = editing.BaudRate.ToString();
            }

            // 编辑即保存，不再提供“保存此会话”勾选项
            saveCheck.Visibility = Visibility.Collapsed;
        }

        dialog.Opened += (_, _) =>
        {
            nameBox.Focus(FocusState.Programmatic);

            // 模板默认“主按钮在左、关闭按钮在右”，这里对调成“取消在左、连接在右”
            SwapDialogButtons(dialog);
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var protocol = SelectedProtocol();
        var host = hostBox.Text.Trim().Replace("\"", "");
        var user = userBox.Text.Trim();
        var password = passwordBox.Password;
        var serialPortName = (serialPortBox.Text ?? "").Trim();

        if (protocol.Fields.Contains(SessionField.Host) && host.Length == 0)
        {
            return;
        }

        if (protocol.Fields.Contains(SessionField.SerialPort) && serialPortName.Length == 0)
        {
            return;
        }

        var port = int.TryParse(portBox.Text.Trim(), out var parsedPort) && parsedPort is >= 1 and <= 65535
            ? parsedPort
            : protocol.DefaultPort;
        var baudRate = int.TryParse(baudBox.Text?.Trim(), out var parsedBaud) && parsedBaud > 0
            ? parsedBaud
            : DefaultBaudRate;

        var name = nameBox.Text.Trim();
        if (name.Length == 0)
        {
            name = protocol.Fields.Contains(SessionField.Host)
                ? $"{protocol.Display}: {(user.Length == 0 ? host : $"{user}@{host}")}"
                : protocol.Fields.Contains(SessionField.SerialPort) ? $"{protocol.Display}: {serialPortName}" : protocol.Display;
        }

        var shouldSave = editing is not null || saveCheck.IsChecked == true;

        if (shouldSave && protocol.Fields.Count > 0)
        {
            var sessions = App.Settings.Current.SavedSessions;

            // 编辑模式：先移除原条目，再按新内容写回
            if (editing is not null)
            {
                sessions.Remove(editing);
            }

            // 同名覆盖，避免同一台主机被反复保存
            if (sessions.FirstOrDefault(session => session.Name == name) is { } existing)
            {
                sessions.Remove(existing);
            }

            sessions.Add(new SavedSession(
                name,
                protocol.Key,
                host,
                port,
                user,
                SecretProtector.Protect(password),
                notesBox.Text.Trim(),
                serialPortName,
                baudRate));

            App.Settings.Save();
        }

        // 编辑模式只保存修改；新建模式保存后直接连接
        if (editing is null)
        {
            Connect(protocol, name, host, port, user, password, serialPortName, baudRate);
        }
    }

    /// <summary>按协议建立会话：能放进标签页的都复用系统客户端，或本应用的 Telnet / 串口实现。</summary>
    private void Connect(
        SessionProtocol protocol,
        string name,
        string host,
        int port,
        string user,
        string password,
        string serialPortName,
        int baudRate)
    {
        var target = user.Length == 0 ? host : $"{user}@{host}";

        switch (protocol.Key)
        {
            case SessionProtocol.Ssh:
                CreateSession(new ShellProfile(name, SystemExecutable(@"OpenSSH\ssh.exe"), $"-p {port} \"{target}\"", Password: password));
                break;

            case SessionProtocol.Sftp:
                CreateSession(new ShellProfile(name, SystemExecutable(@"OpenSSH\sftp.exe"), $"-P {port} \"{target}\"", Password: password));
                break;

            case SessionProtocol.Ftp:
                // ftp.exe 不支持指定端口（固定 21），先不自动登录，用户名与密码交给终端回填
                CreateSession(new ShellProfile(name, SystemExecutable("ftp.exe"), $"-n \"{host}\"", Username: user, Password: password));
                break;

            case SessionProtocol.Telnet:
                CreateSession(new ShellProfile(
                    name,
                    "",
                    Transport: SessionTransport.Telnet,
                    Host: host,
                    Port: port,
                    Username: user,
                    Password: password));
                break;

            case SessionProtocol.Serial:
                CreateSession(new ShellProfile(
                    name,
                    "",
                    Transport: SessionTransport.Serial,
                    SerialPortName: serialPortName,
                    BaudRate: baudRate));
                break;

            case SessionProtocol.Shell:
                CreateSession(DefaultShell() with { Name = name });
                break;

            case SessionProtocol.Wsl:
                CreateSession(ShellProfile.Wsl with { Name = name });
                break;
        }
    }

    /// <summary>本机串口列表；查询失败时退化为空列表，用户仍可手动输入端口名。</summary>
    private static IReadOnlyList<string> EnumerateSerialPorts()
    {
        try
        {
            return [.. SerialPort.GetPortNames().OrderBy(name => name)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    /// <summary>连接已保存的会话：按协议还原参数，密码经 DPAPI 解密。</summary>
    private void ConnectSaved(SavedSession session)
    {
        var protocol = SessionProtocol.FromKey(session.Protocol);

        if (!protocol.Supported || protocol.Fields.Count == 0)
        {
            return;
        }

        Connect(
            protocol,
            session.Name,
            session.Host,
            session.Port,
            session.Username,
            SecretProtector.Unprotect(session.Password),
            session.SerialPortName ?? "",
            session.BaudRate);
    }

    /// <summary>删除已保存的会话：弹确认框，确认后从设置里移除并持久化。</summary>
    private async Task DeleteSavedAsync(SavedSession session)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.DeleteSession,
            Content = new TextBlock
            {
                Text = Loc.DeleteSessionConfirm(session.Name),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = Loc.DeleteSession,
            CloseButtonText = Loc.Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };

        // “删除”要带强调色（DefaultButton 决定哪个按钮高亮），并按惯例落在右侧
        dialog.Opened += (_, _) => SwapDialogButtons(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        App.Settings.Current.SavedSessions.Remove(session);
        App.Settings.Save();
    }

    /// <summary>
    /// WinUI 的 ContentDialog 固定按“主按钮在左、关闭按钮在右”排布按钮。
    /// 这里把两者的位置对调，让“连接”落在右侧。模板里的按钮容器可能是
    /// 带列定义的 Grid（按 Grid.Column 定位），也可能是横向 StackPanel（按顺序排列）。
    /// </summary>
    private static void SwapDialogButtons(ContentDialog dialog)
    {
        if (FindVisualChild<Button>(dialog, "PrimaryButton") is not { } primary ||
            FindVisualChild<Button>(dialog, "CloseButton") is not { } close ||
            !ReferenceEquals(primary.Parent, close.Parent))
        {
            return;
        }

        switch (close.Parent)
        {
            case Grid { ColumnDefinitions.Count: > 1 } grid:
                var primaryColumn = Grid.GetColumn(primary);
                Grid.SetColumn(primary, Grid.GetColumn(close));
                Grid.SetColumn(close, primaryColumn);
                break;

            case StackPanel panel:
                var primaryIndex = panel.Children.IndexOf(primary);
                var closeIndex = panel.Children.IndexOf(close);
                if (primaryIndex < 0 || closeIndex < 0)
                {
                    return;
                }

                panel.Children.Remove(primary);
                panel.Children.Remove(close);
                panel.Children.Insert(Math.Min(primaryIndex, closeIndex), close);
                panel.Children.Insert(Math.Max(primaryIndex, closeIndex), primary);
                break;
        }
    }

    /// <summary>
    /// 系统自带客户端的可执行文件：优先用 System32 下的绝对路径（非打包应用不保证 PATH 里有），
    /// 缺失时回退到裸文件名交给 PATH 解析。
    /// </summary>
    private static string SystemExecutable(string relativePath)
    {
        var path = Path.Combine(Environment.SystemDirectory, relativePath);
        return File.Exists(path) ? path : Path.GetFileName(relativePath);
    }

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab is TabViewItem tab)
        {
            if (tab.Content is TerminalView view)
            {
                view.Dispose();
            }

            sender.TabItems.Remove(tab);
        }

        // 关掉最后一个标签后自动补一个，保持工作区可用
        if (Tabs.TabItems.Count == 0)
        {
            CreateSession(DefaultShell());
        }
    }

    private IEnumerable<TerminalView> EnumerateTerminalViews() =>
        Tabs.TabItems
            .OfType<TabViewItem>()
            .Select(tab => tab.Content)
            .OfType<TerminalView>();
}
