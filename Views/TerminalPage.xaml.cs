using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MoxTerm.Controls;
using MoxTerm.Services;

namespace MoxTerm.Views;

/// <summary>
/// 终端工作区：一个 <see cref="TabView"/>，每个标签页承载一个独立的 Shell 会话。
/// </summary>
public sealed partial class TerminalPage : Page
{
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
    /// Tab 栏加号：列出全部会话类型供选择——本地 Shell 在前，远程协议（SSH/Telnet/串口）在后。
    /// </summary>
    private void OnAddTabButtonClick(TabView sender, object args)
    {
        var flyout = new MenuFlyout();

        foreach (var profile in ShellProfile.Discover())
        {
            var item = new MenuFlyoutItem
            {
                Text = profile.Name,
                Icon = new FontIcon { Glyph = "\uE756" },
            };

            var captured = profile;
            item.Click += (_, _) => CreateSession(captured);
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        AddProtocolItem(flyout, "SSH", "\uE8D7");
        AddProtocolItem(flyout, "Telnet", "\uE756");
        AddProtocolItem(flyout, Loc.Serial, "\uE756");

        // 锚定到加号按钮（模板部件名 AddButton），找不到就退到整个 TabView
        FrameworkElement anchor = FindVisualChild<Button>(Tabs, "AddButton") is { } button ? button : sender;
        flyout.ShowAt(anchor);
    }

    /// <summary>远程协议入口：协议实现前统一提示“即将推出”。</summary>
    private void AddProtocolItem(MenuFlyout flyout, string name, string glyph)
    {
        var item = new MenuFlyoutItem
        {
            Text = name,
            Icon = new FontIcon { Glyph = glyph },
        };

        item.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = name,
                Content = Loc.ProtocolComingSoon(name),
                CloseButtonText = Loc.OK,
            };

            await dialog.ShowAsync();
        };

        flyout.Items.Add(item);
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
