using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MoxTerm.Controls;
using MoxTerm.Services;
using Windows.UI;
using Ellipse = Microsoft.UI.Xaml.Shapes.Ellipse;

namespace MoxTerm.Views;

/// <summary>
/// 网络服务：在本地启动只读的 HTTP / FTP / TFTP 文件服务，共享同一个目录。
/// </summary>
public sealed partial class NetworkServicesPage : Page
{
    private static readonly Brush RunningBrush = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));
    private static readonly Brush StoppedBrush = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140));

    private readonly HttpFileServer _http = new();
    private readonly FtpFileServer _ftp = new();
    private readonly TftpFileServer _tftp = new();

    private bool _windowHooked;

    /// <summary>界面文案源；x:Bind 绑定到它的属性，语言切换时自动刷新。</summary>
    private Loc Loc => Loc.Instance;

    public NetworkServicesPage()
    {
        InitializeComponent();

        // 默认共享下载目录，不存在时退回用户目录
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        RootBox.Text = Directory.Exists(downloads)
            ? downloads
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Loaded += OnLoaded;
        Loc.Changed += (_, _) => UpdateAllUi();
        UpdateAllUi();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 页面在 MainWindow 构造期间创建，此时 App.Shell 还没赋值；关闭窗口时停掉后台监听
        if (!_windowHooked && App.Shell is { } window)
        {
            window.Closed += OnWindowClosed;
            _windowHooked = true;
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _http.Stop();
        _ftp.Stop();
        _tftp.Stop();
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (App.Shell is null)
        {
            return;
        }

        var picker = new Windows.Storage.Pickers.FolderPicker();
        PickerHelper.TryAttach(picker);

        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
        // FolderPicker 仍要求至少一个文件类型过滤器，这是 API 的历史遗留
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            RootBox.Text = folder.Path;
        }
    }

    private void OnHttpToggle(object sender, RoutedEventArgs e) =>
        Toggle(_http, "HTTP", HttpPortBox, HttpStatus, HttpDot, HttpButton, "http");

    private void OnFtpToggle(object sender, RoutedEventArgs e) =>
        Toggle(_ftp, "FTP", FtpPortBox, FtpStatus, FtpDot, FtpButton, "ftp");

    private void OnTftpToggle(object sender, RoutedEventArgs e) =>
        Toggle(_tftp, "TFTP", TftpPortBox, TftpStatus, TftpDot, TftpButton, "tftp");

    private void Toggle(
        IFileServer server,
        string protocol,
        NumberBox portBox,
        TextBlock status,
        Ellipse dot,
        Button button,
        string scheme)
    {
        if (server.IsRunning)
        {
            server.Stop();
            UpdateServiceUi(server, status, button, portBox, dot, scheme);
            return;
        }

        var root = RootBox.Text.Trim();
        if (!Directory.Exists(root))
        {
            ShowError(Loc.NetSvcFolderInvalid);
            return;
        }

        var port = PortInput.Read(portBox);
        if (port is < 1 or > 65535)
        {
            ShowError(Loc.NetSvcPortInvalid);
            return;
        }

        try
        {
            server.Start(port, root);
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            ShowError(Loc.NetSvcStartFailed(protocol, ex.Message));
        }

        UpdateServiceUi(server, status, button, portBox, dot, scheme);
    }

    private void UpdateAllUi()
    {
        UpdateServiceUi(_http, HttpStatus, HttpButton, HttpPortBox, HttpDot, "http");
        UpdateServiceUi(_ftp, FtpStatus, FtpButton, FtpPortBox, FtpDot, "ftp");
        UpdateServiceUi(_tftp, TftpStatus, TftpButton, TftpPortBox, TftpDot, "tftp");
    }

    private void UpdateServiceUi(
        IFileServer server,
        TextBlock status,
        Button button,
        NumberBox portBox,
        Ellipse dot,
        string scheme)
    {
        if (server.IsRunning)
        {
            button.Content = Loc.NetSvcStop;
            portBox.IsEnabled = false;
            status.Opacity = 1;
            status.Text = Loc.NetSvcRunning(
                $"{scheme}://{LocalNetwork.GetLocalAddress()}:{server.Port}/");
        }
        else
        {
            button.Content = Loc.NetSvcStart;
            portBox.IsEnabled = true;
            status.Opacity = 0.6;
            status.Text = Loc.NetSvcStopped;
        }

        dot.Fill = server.IsRunning ? RunningBrush : StoppedBrush;

        // 共享目录和端口在服务运行时不可修改，避免端口与实际监听不一致
        var anyRunning = _http.IsRunning || _ftp.IsRunning || _tftp.IsRunning;
        RootBox.IsEnabled = !anyRunning;
        BrowseButton.IsEnabled = !anyRunning;
    }

    private void ShowError(string message)
    {
        ErrorBar.Title = Loc.NetSvcErrorTitle;
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
