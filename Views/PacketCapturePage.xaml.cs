using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MoxTerm.Services;
using Windows.Storage.Pickers;

namespace MoxTerm.Views;

/// <summary>
/// 网络抓包工作区：选择网卡与 BPF 过滤器，实时展示数据包摘要与十六进制内容，并导出 pcap。
/// 页面被导航缓存复用，捕获在切换页面后继续运行，关窗时统一停止。
/// </summary>
public sealed partial class PacketCapturePage : Page
{
    /// <summary>界面列表最多保留的数据包条数，超出时移除最旧的条目（导出缓冲不受影响）。</summary>
    private const int MaxRows = 5000;

    /// <summary>导出缓冲上限，避免长时间抓包占用过多内存。</summary>
    private const int MaxExportPackets = 200000;

    /// <summary>每次刷新界面最多新增的行数，防止突发流量卡住 UI 线程。</summary>
    private const int MaxRowsPerTick = 400;

    private const int MaxHexBytes = 1024;

    private readonly PacketCaptureService _service = new();
    private readonly ObservableCollection<CapturedPacket> _rows = new();

    /// <summary>本次抓包会话的全部数据包，清空时一并丢弃，导出以它为准。</summary>
    private readonly List<CapturedPacket> _exportBuffer = new();

    private readonly List<CaptureAdapter> _adapters = new();

    private DispatcherQueueTimer? _timer;
    private long _capturedTotal;
    private long _lastDropped;
    private bool _driverAvailable;
    private bool _busy;
    private bool _windowHooked;
    private bool _follow = true;

    /// <summary>当前捕获的网卡描述，用于状态栏文案。</summary>
    private string? _captureAdapter;

    /// <summary>界面文案源；x:Bind 绑定到它的属性，语言切换时自动刷新。</summary>
    private Loc Loc => Loc.Instance;

    public PacketCapturePage()
    {
        InitializeComponent();

        PacketsList.ItemsSource = _rows;
        DetailHeader.Text = Loc.PktNoSelection;

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(150);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => DrainPackets();

        Loaded += OnLoaded;
        Loc.Changed += (_, _) =>
        {
            UpdateDriverBar();
            UpdateStatus();

            if (PacketsList.SelectedItem is null)
            {
                DetailHeader.Text = Loc.PktNoSelection;
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 页面在 MainWindow 构造期间创建，此时 App.Shell 还没赋值
        if (!_windowHooked && App.Shell is { } window)
        {
            window.Closed += OnWindowClosed;
            _windowHooked = true;
        }

        if (_adapters.Count == 0 && !_service.IsRunning)
        {
            RefreshAdapters();
        }

        UpdateButtons();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _timer?.Stop();
        _service.Dispose();
    }

    /// <summary>重新枚举网卡；Npcap 缺失时打开驱动缺失提示。</summary>
    private void RefreshAdapters()
    {
        _driverAvailable = PacketCaptureService.TryGetAdapters(out var adapters, out _);
        _adapters.Clear();
        _adapters.AddRange(adapters);

        AdapterBox.ItemsSource = null;
        AdapterBox.ItemsSource = _adapters;
        AdapterBox.SelectedIndex = _adapters.Count > 0 ? 0 : -1;

        UpdateDriverBar();
        UpdateButtons();

        if (!_driverAvailable)
        {
            StatusText.Text = string.Empty;
        }
        else if (_adapters.Count == 0)
        {
            StatusText.Text = Loc.PktNoAdapters;
        }
    }

    private void UpdateDriverBar()
    {
        DriverBar.Title = Loc.PktDriverMissingTitle;
        DriverBar.Message = Loc.PktDriverMissing;
        DownloadNpcapButton.Content = Loc.PktDownloadNpcap;
        DriverBar.IsOpen = !_driverAvailable;
    }

    private void UpdateButtons()
    {
        var idle = !_busy;

        StartButton.IsEnabled = idle && _driverAvailable && _adapters.Count > 0 && !_service.IsRunning;
        StopButton.IsEnabled = idle && _service.IsRunning;
        ClearButton.IsEnabled = _rows.Count > 0 || _exportBuffer.Count > 0;
        ExportButton.IsEnabled = _exportBuffer.Count > 0;

        AdapterBox.IsEnabled = idle && !_service.IsRunning;
        RefreshButton.IsEnabled = idle && !_service.IsRunning;
        FilterBox.IsEnabled = idle && !_service.IsRunning;
    }

    /// <summary>把捕获线程队列里的数据包批量搬到界面集合。</summary>
    private void DrainPackets()
    {
        var added = 0;

        while (added < MaxRowsPerTick && _service.TryDequeue(out var packet) && packet is not null)
        {
            _rows.Add(packet);
            _exportBuffer.Add(packet);
            _capturedTotal++;
            added++;
        }

        if (_exportBuffer.Count > MaxExportPackets)
        {
            _exportBuffer.RemoveRange(0, _exportBuffer.Count - MaxExportPackets);
        }

        if (_rows.Count > MaxRows)
        {
            while (_rows.Count > MaxRows)
            {
                _rows.RemoveAt(0);
            }
        }

        if (added > 0 && _follow && _rows.Count > 0)
        {
            PacketsList.ScrollIntoView(_rows[^1]);
        }

        var dropped = _service.Dropped;
        if (added > 0 || dropped != _lastDropped)
        {
            _lastDropped = dropped;
            UpdateStatus();
            UpdateButtons();
        }
    }

    private void UpdateStatus()
    {
        if (_service.IsRunning && _captureAdapter is not null)
        {
            StatusText.Text = Loc.PktCapturing(_captureAdapter);
            return;
        }

        StatusText.Text = _capturedTotal > 0
            ? Loc.PktStatus(_capturedTotal, _service.Dropped)
            : string.Empty;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        RefreshAdapters();
    }

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (AdapterBox.SelectedItem is not CaptureAdapter adapter)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        ErrorBar.IsOpen = false;
        StatusText.Text = Loc.PktStarting;

        try
        {
            await Task.Run(() => _service.Start(adapter, FilterBox.Text));

            _captureAdapter = adapter.Description;
            _lastDropped = _service.Dropped;
            _follow = true;
            _timer?.Start();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            ErrorBar.Title = Loc.PktStartFailedTitle;
            ErrorBar.Message = NeedsAdministrator(ex) ? Loc.PktAdminRequired : ex.Message;
            ErrorBar.IsOpen = true;
            StatusText.Text = string.Empty;
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private async void OnStopClick(object sender, RoutedEventArgs e)
    {
        _busy = true;
        UpdateButtons();

        await Task.Run(_service.Stop);

        // 驱动线程可能还有少量包在队列里，稍等后再收尾一次
        await Task.Delay(150);
        DrainPackets();

        _timer?.Stop();
        _captureAdapter = null;
        _busy = false;

        StatusText.Text = _capturedTotal > 0
            ? $"{Loc.PktStopped} {Loc.PktStatus(_capturedTotal, _service.Dropped)}"
            : Loc.PktStopped;

        UpdateButtons();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _service.ResetCounters();
        _rows.Clear();
        _exportBuffer.Clear();
        _capturedTotal = 0;
        _lastDropped = 0;
        _follow = true;

        DetailHeader.Text = Loc.PktNoSelection;
        HexText.Text = string.Empty;
        UpdateStatus();
        UpdateButtons();
    }

    private void OnPacketSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 有选中项时停止自动跟随，方便查看历史数据包
        _follow = PacketsList.SelectedItem is null;

        if (PacketsList.SelectedItem is not CapturedPacket packet)
        {
            DetailHeader.Text = Loc.PktNoSelection;
            HexText.Text = string.Empty;
            return;
        }

        DetailHeader.Text =
            $"{packet.Source} → {packet.Destination}    {packet.Protocol}    {packet.Data.Length} B    {packet.TimeText}";
        HexText.Text = FormatHexDump(packet.Data);
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_exportBuffer.Count == 0 || App.Shell is null)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"moxterm-{DateTime.Now:yyyyMMdd-HHmmss}",
        };
        picker.FileTypeChoices.Add("pcap", new List<string> { ".pcap" });

        // 非打包应用需要手动把选择器关联到窗口句柄，否则会立即返回 null
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Shell);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        // 先做快照，写文件期间捕获仍可继续追加
        var packets = _exportBuffer.ToArray();

        try
        {
            var bytes = await Task.Run(() => BuildPcap(packets));
            await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
            StatusText.Text = Loc.PktExportDone(file.Name);
        }
        catch (Exception ex)
        {
            ErrorBar.Title = Loc.PktExportFailedTitle;
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
    }

    private async void OnDownloadNpcapClick(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://npcap.com/#download"));

    /// <summary>过滤表达式语法错误与权限不足都会以 PcapException 抛出，这里只识别后者。</summary>
    private static bool NeedsAdministrator(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("administrator", StringComparison.OrdinalIgnoreCase)
                || message.Contains("permission", StringComparison.OrdinalIgnoreCase)
                || message.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
                || message.Contains("(5)", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 生成 pcap 文件：24 字节全局头 + 每个包 16 字节记录头（时间戳、长度），全部小端序。
    /// </summary>
    private static byte[] BuildPcap(IReadOnlyList<CapturedPacket> packets)
    {
        var linkType = packets.Count > 0 ? packets[0].LinkType : 1;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(unchecked((int)0xa1b2c3d4));
        writer.Write((ushort)2);
        writer.Write((ushort)4);
        writer.Write(0);
        writer.Write(0);
        writer.Write(65535);
        writer.Write(linkType);

        foreach (var packet in packets)
        {
            var offset = new DateTimeOffset(packet.Time);
            writer.Write((uint)offset.ToUnixTimeSeconds());
            writer.Write((uint)((offset.Ticks % TimeSpan.TicksPerSecond) / 10));
            writer.Write((uint)packet.Data.Length);
            writer.Write((uint)packet.Data.Length);
            writer.Write(packet.Data);
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>经典的十六进制 + ASCII 对照视图；超长包只显示开头部分。</summary>
    private string FormatHexDump(byte[] data)
    {
        var count = Math.Min(data.Length, MaxHexBytes);
        var builder = new StringBuilder(count * 4);

        for (var offset = 0; offset < count; offset += 16)
        {
            builder.Append(offset.ToString("x4")).Append("  ");

            for (var index = 0; index < 16; index++)
            {
                if (offset + index < count)
                {
                    builder.Append(data[offset + index].ToString("x2")).Append(' ');
                }
                else
                {
                    builder.Append("   ");
                }

                if (index == 7)
                {
                    builder.Append(' ');
                }
            }

            builder.Append(' ');

            for (var index = 0; index < 16 && offset + index < count; index++)
            {
                var value = data[offset + index];
                builder.Append(value is >= 0x20 and < 0x7f ? (char)value : '.');
            }

            builder.AppendLine();
        }

        if (data.Length > count)
        {
            builder.AppendLine().Append(Loc.PktHexTruncated(count));
        }

        return builder.ToString();
    }
}
