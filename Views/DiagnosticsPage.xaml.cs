using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MoxTerm.Services;

namespace MoxTerm.Views;

/// <summary>
/// 网络诊断工作区：对目标主机执行 Ping（延迟与丢包统计）或 Traceroute（逐跳路由）。
/// </summary>
public sealed partial class DiagnosticsPage : Page
{
    private readonly ObservableCollection<string> _lines = new();

    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _windowHooked;

    /// <summary>界面文案源；x:Bind 绑定到它的属性，语言切换时自动刷新。</summary>
    private Loc Loc => Loc.Instance;

    public DiagnosticsPage()
    {
        InitializeComponent();

        OutputList.ItemsSource = _lines;

        Loaded += OnLoaded;
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

    private void OnPingClick(object sender, RoutedEventArgs e) => _ = RunAsync(traceroute: false);

    private void OnTracerouteClick(object sender, RoutedEventArgs e) => _ = RunAsync(traceroute: true);

    private void OnStopClick(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _lines.Clear();
        StatusText.Text = string.Empty;
        EmptyText.Visibility = Visibility.Visible;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        PingButton.IsEnabled = !_busy;
        TracerouteButton.IsEnabled = !_busy;
        HostBox.IsEnabled = !_busy;
        StopButton.IsEnabled = _busy;
        ClearButton.IsEnabled = !_busy && _lines.Count > 0;
    }

    private async Task RunAsync(bool traceroute)
    {
        if (_busy)
        {
            return;
        }

        var host = HostBox.Text.Trim();
        if (host.Length == 0)
        {
            ShowError(Loc.DiagHostRequired);
            HostBox.Focus(FocusState.Programmatic);
            return;
        }

        _busy = true;
        _cts = new CancellationTokenSource();
        _lines.Clear();
        EmptyText.Visibility = Visibility.Collapsed;
        ErrorBar.IsOpen = false;
        UpdateButtons();

        var mode = traceroute ? Loc.DiagTraceroute : Loc.DiagPing;
        StatusText.Text = Loc.DiagRunning(mode, host);
        Append(StatusText.Text);
        Append(string.Empty);

        try
        {
            if (traceroute)
            {
                Append(Loc.DiagTracerouteHeader(host));
                await NetworkDiagnostics.TracerouteAsync(host, line => Append(line), _cts.Token);
            }
            else
            {
                var summary = await NetworkDiagnostics.PingAsync(
                    host,
                    reply => Append(FormatReply(reply)),
                    _cts.Token);

                Append(string.Empty);
                Append(summary.Received == 0 ? Loc.DiagPingNoReply : Loc.DiagPingSummary(
                    summary.Sent,
                    summary.Received,
                    summary.LossPercent,
                    summary.Min,
                    summary.Average,
                    summary.Max));
            }

            StatusText.Text = Loc.DiagFinished;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Loc.DiagCancelled;
        }
        catch (Exception ex)
        {
            ShowError(Loc.DiagFailed(ex.Message));
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

    private static string FormatReply(PingReplyInfo reply) => reply.Status switch
    {
        nameof(IPStatus.Success) => Loc.Instance.DiagPingReply(reply.Address, reply.Bytes, reply.Milliseconds, reply.Ttl),
        nameof(IPStatus.TimedOut) => Loc.Instance.DiagTimedOut,
        _ => Loc.Instance.DiagRequestFailed(reply.Status),
    };

    private void Append(string line)
    {
        _lines.Add(line);
        OutputList.ScrollIntoView(line);
        UpdateButtons();
    }

    private void ShowError(string message)
    {
        ErrorBar.Title = Loc.DiagFailedTitle;
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
