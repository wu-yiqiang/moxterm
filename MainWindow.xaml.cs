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

        // FooterMenuItems 里写 x:Bind 会让 XamlCompiler 崩溃（MSB3073 无明细），设置项文案改为代码绑定并跟随语言切换
        SettingsNavItem.Content = Loc.Instance.SettingsTitle;
        Loc.Instance.Changed += (_, _) => SettingsNavItem.Content = Loc.Instance.SettingsTitle;

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

    // 还原后的固定窗口尺寸（物理像素，与 AppWindow/工作区同一坐标系）
    // 1080p 工作区约 1920×1040，此尺寸留有余量；更小屏幕会按工作区自动收缩
    private const int RestoreWidth = 1600;
    private const int RestoreHeight = 950;

    private void ConfigureWindowState()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        // 还原尺寸居中放在主屏工作区；窗口不可调大小，这个尺寸就是还原后的唯一形态
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min(RestoreWidth, workArea.Width);
        var height = Math.Min(RestoreHeight, workArea.Height);
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width, height));

        // 禁止拖动边框调整大小；最大化/还原按钮不受影响
        presenter.IsResizable = false;

        // 启动即最大化
        presenter.Maximize();
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
        {
            return;
        }

        // 页面设置了 NavigationCacheMode=Required，重复导航会白白重建实例，这里直接跳过
        var target = tag switch
        {
            "terminal" => typeof(TerminalPage),
            "settings" => typeof(SettingsPage),
            _ => null,
        };

        if (target is not null)
        {
            if (ContentFrame.CurrentSourcePageType != target)
            {
                ContentFrame.Navigate(target);
            }

            return;
        }

        // 规划中的功能分组统一走占位页，Tag 作为参数决定展示内容，无需跳过重复导航
        ContentFrame.Navigate(typeof(FeaturePage), tag);
    }
}
