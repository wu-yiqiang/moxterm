using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MoxTerm.Services;
using Windows.Storage.Pickers;
using Windows.UI;

namespace MoxTerm.Views;

/// <summary>
/// 网络扫描工作区：按 CIDR 或地址区间并发探测在线主机，展示 IP、主机名、MAC 与延迟，并支持导出 CSV。
/// </summary>
public sealed partial class NetworkScanPage : Page
{
    private readonly ObservableCollection<HostRow> _rows = new();

    /// <summary>后台扫描线程写入、界面定时取走的在线主机。</summary>
    private readonly ConcurrentQueue<HostScanResult> _pending = new();

    private DispatcherQueueTimer? _timer;
    private CancellationTokenSource? _cts;
    private int _total;
    private int _scanned;
    private int _lastScanned;
    private bool _busy;
    private bool _windowHooked;
    private DateTime _started;

    /// <summary>界面文案源；x:Bind 绑定到它的属性，语言切换时自动刷新。</summary>
    private Loc Loc => Loc.Instance;

    public NetworkScanPage()
    {
        InitializeComponent();

        HostsList.ItemsSource = _rows;

        // 预填本机所在网段，用户可直接开始扫描
        RangeBox.Text = LocalNetwork.GetLocalSubnet() ?? string.Empty;

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(150);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => DrainResults();

        Loaded += OnLoaded;
        UpdateButtons();
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

    private void OnLocalSubnetClick(object sender, RoutedEventArgs e)
    {
        var subnet = LocalNetwork.GetLocalSubnet();
        if (subnet is null)
        {
            ShowError(Loc.NetScanNoLocalSubnet);
            return;
        }

        RangeBox.Text = subnet;
    }

    private void UpdateButtons()
    {
        StartButton.IsEnabled = !_busy;
        StopButton.IsEnabled = _busy;
        ClearButton.IsEnabled = !_busy && _rows.Count > 0;
        ExportButton.IsEnabled = !_busy && _rows.Count > 0;

        RangeBox.IsEnabled = !_busy;
        LocalSubnetButton.IsEnabled = !_busy;
    }

    private void OnScanClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (!NetworkScanService.TryParseRange(RangeBox.Text, out var hosts, out var tooLarge))
        {
            ShowError(tooLarge ? Loc.NetScanRangeTooLarge : Loc.NetScanRangeInvalid);
            RangeBox.Focus(FocusState.Programmatic);
            return;
        }

        _busy = true;
        _rows.Clear();
        while (_pending.TryDequeue(out _))
        {
        }

        _total = hosts.Count;
        _scanned = 0;
        _lastScanned = 0;
        _started = DateTime.Now;

        EmptyText.Visibility = Visibility.Collapsed;
        ErrorBar.IsOpen = false;
        StatusText.Text = Loc.NetScanProgress(0, _total, 0);
        UpdateButtons();

        _timer?.Start();
        _ = ScanAsync(hosts);
    }

    private async Task ScanAsync(IReadOnlyList<IPAddress> hosts)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            await NetworkScanService.ScanAsync(
                hosts,
                result => _pending.Enqueue(result),
                () => Interlocked.Increment(ref _scanned),
                token);

            // 等待最后一批回调从后台线程入队
            await Task.Delay(180);
            DrainResults();

            var elapsed = (DateTime.Now - _started).TotalSeconds;
            StatusText.Text = _rows.Count == 0
                ? Loc.NetScanNoHosts
                : Loc.NetScanDone(_rows.Count, _total, elapsed);
        }
        catch (OperationCanceledException)
        {
            await Task.Delay(150);
            DrainResults();
            StatusText.Text = Loc.NetScanCancelled(_rows.Count);
        }
        catch (Exception ex)
        {
            ShowError(Loc.NetScanFailed(ex.Message));
            StatusText.Text = string.Empty;
        }
        finally
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            _timer?.Stop();
            DrainResults();
            UpdateButtons();
        }
    }

    /// <summary>把后台线程产生的在线主机搬到界面集合，并按 IP 升序插入。</summary>
    private void DrainResults()
    {
        var added = false;

        while (_pending.TryDequeue(out var host))
        {
            InsertSorted(host);
            added = true;
        }

        if (_busy)
        {
            var scanned = Volatile.Read(ref _scanned);
            if (scanned != _lastScanned)
            {
                _lastScanned = scanned;
                StatusText.Text = Loc.NetScanProgress(scanned, _total, _rows.Count);
            }
        }

        if (added)
        {
            UpdateButtons();
        }
    }

    private void InsertSorted(HostScanResult host)
    {
        var row = new HostRow(host);
        var index = 0;

        while (index < _rows.Count && Compare(_rows[index].Result.Address, row.Result.Address) < 0)
        {
            index++;
        }

        _rows.Insert(index, row);
    }

    private static int Compare(IPAddress left, IPAddress right)
    {
        var leftBytes = left.GetAddressBytes();
        var rightBytes = right.GetAddressBytes();

        for (var index = 0; index < Math.Min(leftBytes.Length, rightBytes.Length); index++)
        {
            if (leftBytes[index] != rightBytes[index])
            {
                return leftBytes[index].CompareTo(rightBytes[index]);
            }
        }

        return leftBytes.Length.CompareTo(rightBytes.Length);
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
        _lastScanned = 0;
        _total = 0;
        StatusText.Text = string.Empty;
        EmptyText.Visibility = Visibility.Visible;
        UpdateButtons();
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0 || App.Shell is null)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"moxterm-scan-{DateTime.Now:yyyyMMdd-HHmmss}",
        };
        picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });

        PickerHelper.TryAttach(picker);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var content = BuildCsv();
            var payload = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray();
            await Windows.Storage.FileIO.WriteBytesAsync(file, payload);
            StatusText.Text = Loc.NetScanExportDone(file.Name);
        }
        catch (Exception ex)
        {
            ErrorBar.Title = Loc.NetScanExportTitle;
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
    }

    /// <summary>导出为带 BOM 的 UTF-8 CSV，Excel 打开中文不乱码。</summary>
    private string BuildCsv()
    {
        var builder = new StringBuilder();

        var header = new List<string>
        {
            Loc.NetScanColAddress,
            Loc.NetScanColHostName,
            Loc.NetScanColMac,
            Loc.NetScanColLatency,
        };
        header.AddRange(NetworkScanService.ServiceNames);
        builder.AppendLine(string.Join(',', header.Select(Escape)));

        foreach (var row in _rows)
        {
            var fields = new List<string>
            {
                row.Result.IpText,
                row.Result.HostNameText,
                row.Result.MacText,
                row.Result.LatencyText,
            };
            fields.AddRange(row.Result.Services.Values.Select(open => open ? "✓" : "✗"));
            builder.AppendLine(string.Join(',', fields.Select(Escape)));
        }

        return builder.ToString();

        static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private void ShowError(string message)
    {
        ErrorBar.Title = Loc.NetScanErrorTitle;
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}

/// <summary>在线主机列表的一行：显示字段 + 常见服务检测结果。</summary>
public sealed class HostRow
{
    private static readonly Brush OpenBrush = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));
    private static readonly Brush ClosedBrush = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140));

    public HostRow(HostScanResult result)
    {
        Result = result;
        ServiceCells = result.Services.Values
            .Select(open => new ServiceCell(open, open ? OpenBrush : ClosedBrush))
            .ToList();
    }

    public HostScanResult Result { get; }

    public string IpText => Result.IpText;

    public string HostNameText => Result.HostNameText;

    public string MacText => Result.MacText;

    public string LatencyText => Result.LatencyText;

    /// <summary>按 SSH、RDP、VNC、FTP、Telnet、Rlogin、HTTP 顺序排列的勾/叉。</summary>
    public IReadOnlyList<ServiceCell> ServiceCells { get; }
}

/// <summary>服务检测结果对应的图标与颜色。</summary>
public sealed record ServiceCell(bool IsOpen, Brush Brush)
{
    /// <summary>Segoe Fluent Icons 的 CheckMark / Cancel。</summary>
    public string Glyph => IsOpen ? "\uE73E" : "\uE711";
}
