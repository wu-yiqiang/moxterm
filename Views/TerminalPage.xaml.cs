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

        LoadShells();
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

    private ShellProfile SelectedShell =>
        ShellComboBox.SelectedItem as ShellProfile ?? ShellProfile.CommandPrompt;

    /// <summary>
    /// 把当前设置应用到所有已打开的会话（字号、字体、行高、配色、光标、回滚缓冲），
    /// 并同步工具栏上的默认 Shell 选择。
    /// </summary>
    public void ApplySettings()
    {
        foreach (var view in EnumerateTerminalViews())
        {
            view.ApplySettings(App.Settings.Current);
        }

        SyncShellSelection();
    }

    private void LoadShells()
    {
        var shells = ShellProfile.Discover();
        ShellComboBox.ItemsSource = shells;
        ShellComboBox.DisplayMemberPath = nameof(ShellProfile.Name);

        ShellComboBox.SelectedItem = shells.FirstOrDefault(shell => shell.Name == App.Settings.Current.DefaultShell)
            ?? shells[0];
    }

    /// <summary>设置页改动默认 Shell 后，把工具栏的选择同步过去。</summary>
    private void SyncShellSelection()
    {
        var preferred = App.Settings.Current.DefaultShell;
        var match = ShellComboBox.Items
            .OfType<ShellProfile>()
            .FirstOrDefault(shell => shell.Name == preferred);

        if (match is not null && !ReferenceEquals(ShellComboBox.SelectedItem, match))
        {
            ShellComboBox.SelectedItem = match;
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
            CreateSession(SelectedShell);
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

    private void OnNewSessionClick(object sender, RoutedEventArgs e) => CreateSession(SelectedShell);

    private void OnAddTabButtonClick(TabView sender, object args) => CreateSession(SelectedShell);

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
            CreateSession(SelectedShell);
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (Tabs.SelectedItem is TabViewItem { Content: TerminalView view })
        {
            view.Clear();
        }
    }

    private void OnShellSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShellComboBox.SelectedItem is not ShellProfile profile)
        {
            return;
        }

        App.Settings.Current.DefaultShell = profile.Name;
        App.Settings.Save();
    }

    private IEnumerable<TerminalView> EnumerateTerminalViews() =>
        Tabs.TabItems
            .OfType<TabViewItem>()
            .Select(tab => tab.Content)
            .OfType<TerminalView>();
}
