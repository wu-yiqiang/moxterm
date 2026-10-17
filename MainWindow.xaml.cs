using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MoxTerm.Services;
using MoxTerm.Views;
using Windows.Graphics;

namespace MoxTerm;

/// <summary>
/// 应用主窗口：Mica 背景 + 自定义标题栏 + NavigationView 导航骨架。
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>界面文案源；x:Bind 绑定到它的属性，语言切换时自动刷新。</summary>
    private Loc Loc => Loc.Instance;

    public MainWindow()
    {
        InitializeComponent();

        Title = "MoxTerm";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);

        // 窗口与任务栏图标（exe 图标由 csproj 的 ApplicationIcon 负责）
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        // 自定义标题栏左侧的应用图标；非打包应用没有 ms-appx 路径，直接按文件路径加载
        TitleBarIcon.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png")));

        TryApplyBackdrop();
        ApplyTheme(App.Settings.ResolveTheme());
        ConfigureWindowState();

        // 主题切换（含跟随系统时的系统主题变化）后重算终端配色
        if (Content is FrameworkElement root)
        {
            root.ActualThemeChanged += (_, _) => TerminalPage.Current?.ApplySettings();
        }

        // 默认进入终端页（会触发 OnNavSelectionChanged）
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    /// <summary>把主题应用到窗口内容，并同步系统标题按钮的前景色。</summary>
    public void ApplyTheme(ElementTheme theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }

        UpdateCaptionButtonColors(theme);
    }

    private void TryApplyBackdrop()
    {
        try
        {
            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop();
            }
            else if (DesktopAcrylicController.IsSupported())
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
            }
        }
        catch (Exception ex)
        {
            // 不支持背景材质的系统上直接退化为纯色，不影响功能
            System.Diagnostics.Debug.WriteLine($"[MoxTerm] 应用背景材质失败: {ex.Message}");
        }
    }

    private void UpdateCaptionButtonColors(ElementTheme theme)
    {
        // ElementTheme.Default 时以系统主题为准
        var effective = theme == ElementTheme.Default
            ? (Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light)
            : theme;

        var foreground = effective == ElementTheme.Dark
            ? Microsoft.UI.Colors.White
            : Microsoft.UI.Colors.Black;

        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = foreground;
    }

    private void ConfigureWindowState()
    {
        // 窗口最小尺寸：再小设置页与终端就排不开了；达到下限后无法继续缩小
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1200;
            presenter.PreferredMinimumHeight = 700;

            // 启动即最大化
            presenter.Maximize();
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var pageType = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : args.SelectedItem is NavigationViewItem { Tag: string tag } && tag == "terminal"
                ? typeof(TerminalPage)
                : null;

        // 页面设置了 NavigationCacheMode=Required，重复导航会白白重建实例，这里直接跳过
        if (pageType is null || ContentFrame.CurrentSourcePageType == pageType)
        {
            return;
        }

        ContentFrame.Navigate(pageType);
    }
}
