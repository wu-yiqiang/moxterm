using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MoxTerm.Services;

namespace MoxTerm.Views;

/// <summary>
/// 端口扫描工作区：解析目标主机与端口范围，并发探测 TCP 端口，只列出开放端口及其服务标识。
/// </summary>
public sealed partial class PortScanPage : Page
{
    /// <summary>预设“常用端口”：覆盖常见管理、数据库与 Web 服务。</summary>
    private const string CommonPorts =
        "21,22,23,25,53,80,110,135,139,143,443,445,587,993,995,1433,1521,3306,3389,5432,5900,6379,8080,8443,27017";

    private readonly ObservableCollection<PortScanResult> _rows = new();

    private CancellationTokenSource? _cts;
    private int _total;
    private int _scanned;
    private int _open;
    private int _presetIndex;
    private bool _updatingPresets;
    private bool _busy;
    private bool _windowHooked;
    private DateTime _started;

    /// <summary>界面文案源；x:Bind 绑定到它的属性，语言切换时自动刷新。</summary>
    private Loc Loc => Loc.Instance;

    public PortScanPage()
    {
        InitializeComponent();

        PortsList.ItemsSource = _rows;
        PortsBox.Text = CommonPorts;

        RefreshPresets();
        UpdateButtons();

        Loaded += OnLoaded;
        Loc.Changed += (_, _) => RefreshPresets();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 页面在 MainWindow 构造期间创建，此时 App.Shell 还没赋值
        if (!_windowHooked && App.Shell is { } window)
        {
            window.Closed += OnWindowClosed;
            _windowHooked = true;
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args) => _cts?.Cancel();

    /// <summary>重建预设下拉项；语言切换时只刷新文案，不覆盖用户输入的端口。</summary>
    private void RefreshPresets()
    {
        _updatingPresets = true;
        PresetBox.ItemsSource = new List<string>
        {
            Loc.PortScanPresetCommon,
            Loc.PortScanPresetLow,
            Loc.PortScanPresetAll,
        };
        PresetBox.SelectedIndex = _presetIndex;
        _updatingPresets = false;
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingPresets)
        {
            return;
        }

        _presetIndex = PresetBox.SelectedIndex;
        PortsBox.Text = _presetIndex switch
        {
            1 => "1-1024",
            2 => "1-65535",
            _ => CommonPorts,
        };
    }

    private void UpdateButtons()
    {
        StartButton.IsEnabled = !_busy;
        StopButton.IsEnabled = _busy;
        ClearButton.IsEnabled = !_busy && _rows.Count > 0;

        HostBox.IsEnabled = !_busy;
        PortsBox.IsEnabled = !_busy;
        PresetBox.IsEnabled = !_busy;
    }

    private void OnScanClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var host = HostBox.Text.Trim();
        if (host.Length == 0)
        {
            ShowError(Loc.PortScanHostRequired);
            HostBox.Focus(FocusState.Programmatic);
            return;
        }

        if (!PortScanService.TryParsePorts(PortsBox.Text, out var ports, out var token))
        {
            ShowError(string.IsNullOrEmpty(token) ? Loc.PortScanPortsEmpty : Loc.PortScanPortsInvalid(token));
            PortsBox.Focus(FocusState.Programmatic);
            return;
        }

        _busy = true;
        _rows.Clear();
        _total = ports.Count;
        _scanned = 0;
        _open = 0;
        _started = DateTime.Now;
        EmptyText.Visibility = Visibility.Collapsed;
        ErrorBar.IsOpen = false;
        StatusText.Text = Loc.PortScanResolving(host);
        UpdateButtons();

        _ = ScanAsync(host, ports);
    }

    private async Task ScanAsync(string host, IReadOnlyList<int> ports)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            await PortScanService.ScanAsync(
                host,
                ports,
                result => DispatcherQueue.TryEnqueue(() => AddResult(result)),
                () => DispatcherQueue.TryEnqueue(OnScanned),
                token);

            var elapsed = (DateTime.Now - _started).TotalSeconds;
            StatusText.Text = _open == 0
                ? Loc.PortScanNoOpen
                : Loc.PortScanDone(_open, _total, elapsed);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Loc.PortScanCancelled(_open);
        }
        catch (Exception ex)
        {
            ShowError(Loc.PortScanFailed(ex.Message));
            StatusText.Text = string.Empty;
        }
        finally
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            UpdateButtons();
        }
    }

    private void AddResult(PortScanResult result)
    {
        if (!result.IsOpen)
        {
            return;
        }

        _open++;
        _rows.Add(result);
        UpdateButtons();
    }

    private void OnScanned()
    {
        _scanned++;

        if (_scanned % 16 == 0 || _scanned == _total)
        {
            StatusText.Text = Loc.PortScanProgress(_scanned, _total, _open);
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _rows.Clear();
        _scanned = 0;
        _open = 0;
        _total = 0;
        StatusText.Text = string.Empty;
        EmptyText.Visibility = Visibility.Visible;
        UpdateButtons();
    }

    private void ShowError(string message)
    {
        ErrorBar.Title = Loc.PortScanErrorTitle;
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
