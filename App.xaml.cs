using Microsoft.UI.Xaml;
using MoxTerm.Services;

namespace MoxTerm;

/// <summary>
/// 应用入口。负责创建主窗口，并持有全局唯一的设置服务实例。
/// </summary>
public partial class App : Application
{
    /// <summary>主窗口。窗口构造完成并 <c>Activate</c> 之后才可用。</summary>
    public static MainWindow? Shell { get; private set; }

    /// <summary>全局设置（外观、终端、输入行为与默认 Shell）。</summary>
    public static SettingsService Settings { get; } = new();

    public App()
    {
        InitializeComponent();
        Settings.Load();
        Loc.Instance.Apply(Settings.Current.Language);

        // 任何入口保存设置后都重新解析语言（语言本身没变时 Apply 会直接返回）
        Settings.Changed += (_, _) => Loc.Instance.Apply(Settings.Current.Language);

        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        Shell = window;
        window.Activate();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // 兜底：把异常写进调试输出，避免 UI 线程异常直接静默退出
        System.Diagnostics.Debug.WriteLine($"[MoxTerm] 未处理异常: {e.Exception}");
    }
}
