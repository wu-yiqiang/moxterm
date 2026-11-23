using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MoxTerm.Services;

namespace MoxTerm.Views;

/// <summary>
/// 列出开放端口：读取本机 TCP / UDP 端点与所属进程，支持按端口、地址、进程名或 PID 过滤。
/// </summary>
public sealed partial class OpenPortsPage : Page
{
    private readonly ObservableCollection<PortEntry> _rows = new();

    private List<PortEntry> _all = new();
    private bool _loaded;

    /// <summary>界面文案源；x:Bind 绑定到它的属性，语言切换时自动刷新。</summary>
    private Loc Loc => Loc.Instance;

    public OpenPortsPage()
    {
        InitializeComponent();

        PortsList.ItemsSource = _rows;

        Loaded += OnLoaded;
        Loc.Changed += (_, _) => UpdateEmptyState();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 页面被导航缓存复用，只在首次进入时读取一次
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        LoadPorts();
    }

    private void LoadPorts()
    {
        try
        {
            _all = NetworkPortService.GetPorts().ToList();
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            _all.Clear();
            ErrorBar.Title = Loc.OpenPortsErrorTitle;
            ErrorBar.Message = Loc.OpenPortsFailed(ex.Message);
            ErrorBar.IsOpen = true;
        }

        ApplyFilter();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => LoadPorts();

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var filter = FilterBox.Text.Trim();

        var matches = filter.Length == 0
            ? _all
            : _all.Where(entry => Matches(entry, filter)).ToList();

        _rows.Clear();
        foreach (var entry in matches)
        {
            _rows.Add(entry);
        }

        var tcp = _all.Count(entry => entry.Protocol == "TCP");
        StatusText.Text = Loc.OpenPortsStatus(_all.Count, tcp, _all.Count - tcp);

        UpdateEmptyState();
    }

    private static bool Matches(PortEntry entry, string filter)
    {
        if (entry.LocalPort.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.RemotePort.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.Pid.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return entry.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.LocalAddress.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.RemoteAddress.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.Protocol.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.State.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateEmptyState()
    {
        EmptyText.Text = _all.Count == 0 ? Loc.OpenPortsEmpty : Loc.OpenPortsNoMatch;
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
