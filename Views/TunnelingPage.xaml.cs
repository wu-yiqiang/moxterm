using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MoxTerm.Controls;
using MoxTerm.Services;
using Windows.Globalization.NumberFormatting;
using Windows.UI;

namespace MoxTerm.Views;

/// <summary>
/// SSH 隧道（端口转发）工作区：管理隧道配置，并启动 / 停止本地、远程与动态转发。
/// 页面被导航缓存复用，隧道在切换页面后继续运行，关窗时统一断开。
/// </summary>
public sealed partial class TunnelingPage : Page
{
    private readonly ObservableCollection<TunnelRow> _rows = new();

    /// <summary>正在执行启动或停止，期间禁用工具栏避免并发操作。</summary>
    private bool _busy;

    private bool _windowHooked;

    /// <summary>界面文案源；x:Bind 绑定到它的属性，语言切换时自动刷新。</summary>
    private Loc Loc => Loc.Instance;

    public TunnelingPage()
    {
        InitializeComponent();

        TunnelsList.ItemsSource = _rows;

        foreach (var profile in App.Settings.Current.Tunnels)
        {
            _rows.Add(CreateRow(profile));
        }

        UpdateEmptyState();
        UpdateButtons();

        Loaded += OnLoaded;
        Loc.Changed += (_, _) =>
        {
            foreach (var row in _rows)
            {
                row.Refresh();
            }

            UpdateButtons();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 页面在 MainWindow 构造期间创建，此时 App.Shell 还没赋值
        if (_windowHooked || App.Shell is not { } window)
        {
            return;
        }

        window.Closed += OnWindowClosed;
        _windowHooked = true;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        foreach (var row in _rows)
        {
            row.Dispose();
        }
    }

    private TunnelRow CreateRow(TunnelProfile profile)
    {
        var row = new TunnelRow(profile, DispatcherQueue);

        row.Changed += (_, _) =>
        {
            UpdateButtons();

            // 运行时失败（连接断开、监听端口被占用等）直接把原因写到状态栏
            if (row.State == TunnelState.Failed)
            {
                StatusText.Text = Loc.TunnelStartFailed(row.Error ?? string.Empty);
            }
        };

        return row;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        var row = TunnelsList.SelectedItem as TunnelRow;
        var idle = !_busy && row is not null && !row.IsRunning && !row.IsBusy;

        StartButton.IsEnabled = idle;
        StopButton.IsEnabled = !_busy && row is not null
            && (row.IsRunning || row.State == TunnelState.Failed);
        EditButton.IsEnabled = idle;
        DeleteButton.IsEnabled = idle;
    }

    private void UpdateEmptyState() =>
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (TunnelsList.SelectedItem is TunnelRow row)
        {
            await StartRowAsync(row);
        }
    }

    private async void OnStopClick(object sender, RoutedEventArgs e)
    {
        if (TunnelsList.SelectedItem is TunnelRow row)
        {
            await StopRowAsync(row);
        }
    }

    private async Task StartRowAsync(TunnelRow row)
    {
        if (_busy || row.IsRunning)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        StatusText.Text = Loc.TunnelStateConnecting;

        await Task.Run(row.Start);

        _busy = false;
        row.Refresh();
        UpdateButtons();

        StatusText.Text = row.State == TunnelState.Running
            ? Loc.TunnelStarted(row.Name)
            : Loc.TunnelStartFailed(row.Error ?? string.Empty);
    }

    private async Task StopRowAsync(TunnelRow row)
    {
        if (_busy || row.State == TunnelState.Stopped)
        {
            return;
        }

        _busy = true;
        UpdateButtons();

        await Task.Run(row.Stop);

        _busy = false;
        row.Refresh();
        UpdateButtons();
        StatusText.Text = Loc.TunnelStopped(row.Name);
    }

    private async void OnListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TunnelsList.SelectedItem is not TunnelRow row || row.IsBusy)
        {
            return;
        }

        if (row.IsRunning)
        {
            await StopRowAsync(row);
        }
        else
        {
            await StartRowAsync(row);
        }
    }

    private async void OnNewClick(object sender, RoutedEventArgs e) => await ShowTunnelDialogAsync(null);

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (TunnelsList.SelectedItem is TunnelRow row)
        {
            await ShowTunnelDialogAsync(row);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (TunnelsList.SelectedItem is not TunnelRow row)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.DeleteSession,
            Content = new TextBlock
            {
                Text = Loc.TunnelDeleteConfirm(row.Name),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = Loc.DeleteSession,
            CloseButtonText = Loc.Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Opened += (_, _) => DialogHelper.SwapButtons(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        row.Dispose();
        _rows.Remove(row);
        App.Settings.Current.Tunnels.Remove(row.Profile);
        App.Settings.Save();

        UpdateEmptyState();
        UpdateButtons();
    }

    /// <summary>
    /// 新建 / 编辑隧道：SSH 服务器与认证信息 + 监听端点 + 目标端点，
    /// 动态转发不填写目标。传入 <paramref name="row"/> 时进入编辑模式。
    /// </summary>
    private async Task ShowTunnelDialogAsync(TunnelRow? row)
    {
        var isNew = row is null;
        var draft = row?.Profile.Clone() ?? new TunnelProfile();

        var nameBox = new TextBox
        {
            Header = Loc.TunnelNameLabel,
            PlaceholderText = Loc.TunnelNamePlaceholder,
            Text = draft.Name,
        };
        var typeBox = new ComboBox
        {
            Header = Loc.TunnelTypeLabel,
            ItemsSource = new List<string> { Loc.TunnelTypeLocal, Loc.TunnelTypeRemote, Loc.TunnelTypeDynamic },
            SelectedIndex = TunnelTypes.IndexOf(draft.Type),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var sshHostBox = new TextBox
        {
            Header = Loc.TunnelSshHostLabel,
            PlaceholderText = Loc.SshHostPlaceholder,
            Text = draft.SshHost,
        };
        var sshPortBox = PortBox(Loc.TunnelSshPortLabel, draft.SshPort is >= 1 and <= 65535 ? draft.SshPort : 22);
        var userBox = new TextBox
        {
            Header = Loc.SshUsernameLabel,
            PlaceholderText = Loc.SshUsernamePlaceholder,
            Text = draft.Username,
        };
        var passwordBox = new PasswordBox
        {
            Header = Loc.SshPasswordLabel,
            Password = SecretProtector.Unprotect(draft.Password),
        };

        var bindHostBox = new TextBox
        {
            Header = Loc.TunnelBindHostLabel,
            Text = string.IsNullOrWhiteSpace(draft.BindHost) ? "127.0.0.1" : draft.BindHost,
        };
        var bindPortBox = PortBox(Loc.TunnelBindPortLabel, draft.BindPort is >= 1 and <= 65535 ? draft.BindPort : double.NaN);
        var targetHostBox = new TextBox
        {
            Header = Loc.TunnelTargetHostLabel,
            Text = string.IsNullOrWhiteSpace(draft.TargetHost) ? "127.0.0.1" : draft.TargetHost,
        };
        var targetPortBox = PortBox(Loc.TunnelTargetPortLabel, draft.TargetPort is >= 1 and <= 65535 ? draft.TargetPort : double.NaN);

        var hint = new TextBlock
        {
            Text = Loc.TunnelInvalid,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var targetPair = Pair(targetHostBox, targetPortBox);
        var content = new StackPanel { Spacing = 12, Width = 420 };
        content.Children.Add(nameBox);
        content.Children.Add(typeBox);
        content.Children.Add(Pair(sshHostBox, sshPortBox));
        content.Children.Add(userBox);
        content.Children.Add(passwordBox);
        content.Children.Add(Pair(bindHostBox, bindPortBox));
        content.Children.Add(targetPair);
        content.Children.Add(hint);

        void SyncType()
        {
            var type = TunnelTypes.All[Math.Clamp(typeBox.SelectedIndex, 0, TunnelTypes.All.Count - 1)];
            targetPair.Visibility = type == TunnelTypes.Dynamic ? Visibility.Collapsed : Visibility.Visible;
            bindHostBox.Header = type == TunnelTypes.Remote
                ? Loc.TunnelBindHostRemoteLabel
                : Loc.TunnelBindHostLabel;
        }

        typeBox.SelectionChanged += (_, _) => SyncType();
        SyncType();

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = isNew ? Loc.TunnelDialogNew : Loc.TunnelDialogEdit,
            Content = content,
            PrimaryButtonText = isNew ? Loc.TunnelNew : Loc.SaveChanges,
            CloseButtonText = Loc.Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };

        dialog.Opened += (_, _) =>
        {
            nameBox.Focus(FocusState.Programmatic);
            DialogHelper.SwapButtons(dialog);
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            var type = TunnelTypes.All[Math.Clamp(typeBox.SelectedIndex, 0, TunnelTypes.All.Count - 1)];
            var sshPort = ReadPort(sshPortBox);
            var bindPort = ReadPort(bindPortBox);
            var targetPort = ReadPort(targetPortBox);
            var invalid = string.IsNullOrWhiteSpace(sshHostBox.Text)
                || string.IsNullOrWhiteSpace(userBox.Text)
                || sshPort is < 1 or > 65535
                || bindPort is < 1 or > 65535
                || (type != TunnelTypes.Dynamic && targetPort is < 1 or > 65535);

            hint.Visibility = invalid ? Visibility.Visible : Visibility.Collapsed;
            if (invalid)
            {
                args.Cancel = true;
                return;
            }

            draft.Type = type;
            draft.SshHost = sshHostBox.Text.Trim();
            draft.SshPort = sshPort > 0 ? sshPort : 22;
            draft.Username = userBox.Text.Trim();
            draft.Password = SecretProtector.Protect(passwordBox.Password);
            draft.BindHost = string.IsNullOrWhiteSpace(bindHostBox.Text) ? "127.0.0.1" : bindHostBox.Text.Trim();
            draft.BindPort = bindPort;
            draft.TargetHost = string.IsNullOrWhiteSpace(targetHostBox.Text) ? "127.0.0.1" : targetHostBox.Text.Trim();
            draft.TargetPort = type == TunnelTypes.Dynamic ? 0 : targetPort;

            var name = nameBox.Text.Trim();
            draft.Name = name.Length > 0 ? name : $"{draft.SshHost}:{draft.BindPort}";
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (row is null)
        {
            App.Settings.Current.Tunnels.Add(draft);
            App.Settings.Save();

            var newRow = CreateRow(draft);
            _rows.Add(newRow);
            TunnelsList.SelectedItem = newRow;
            UpdateEmptyState();
        }
        else
        {
            row.Apply(draft);
            App.Settings.Save();
            row.Refresh();
        }

        UpdateButtons();
    }

    /// <summary>端口输入框：整数、1–65535，带步进箭头。</summary>
    private static NumberBox PortBox(string header, double value) => new()
    {
        Header = header,
        Minimum = 1,
        Maximum = 65535,
        SmallChange = 1,
        LargeChange = 10,
        AcceptsExpression = false,
        Value = value,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        NumberFormatter = new DecimalFormatter
        {
            FractionDigits = 0,
            IntegerDigits = 1,
        },
    };

    /// <summary>
    /// 读取端口：NumberBox 的 Value 与 Text 都要等到输入提交（失焦 / 回车）才更新，
    /// 光标还停在输入框里就点“新建”时会读到旧值，所以以模板里输入框的当前文本为准。
    /// </summary>
    private static int ReadPort(NumberBox box)
    {
        if (FindInputTextBox(box) is { } input)
        {
            return int.TryParse(input.Text?.Trim(), out var current) ? current : 0;
        }

        return int.TryParse(box.Text?.Trim(), out var parsed)
            ? parsed
            : double.IsNaN(box.Value) ? 0 : (int)box.Value;
    }

    private static TextBox? FindInputTextBox(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);

        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);

            if (child is TextBox { Name: "InputBox" } input)
            {
                return input;
            }

            if (FindInputTextBox(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>地址与端口并排：地址占满剩余宽度，端口固定宽度。</summary>
    private static Grid Pair(FrameworkElement address, FrameworkElement port)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        Grid.SetColumn(port, 1);
        grid.Children.Add(address);
        grid.Children.Add(port);
        return grid;
    }
}

/// <summary>
/// 隧道列表的一行：把配置与运行时状态转成界面文案与状态色，
/// 状态变化经 DispatcherQueue 回到 UI 线程再刷新绑定。
/// </summary>
public sealed class TunnelRow : INotifyPropertyChanged
{
    private static readonly Brush RunningBrush = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));
    private static readonly Brush ConnectingBrush = new SolidColorBrush(Color.FromArgb(255, 255, 185, 0));
    private static readonly Brush FailedBrush = new SolidColorBrush(Color.FromArgb(255, 232, 17, 35));
    private static readonly Brush StoppedBrush = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));

    private readonly TunnelRuntime _runtime;
    private readonly DispatcherQueue _dispatcher;
    private bool _disposed;

    public TunnelRow(TunnelProfile profile, DispatcherQueue dispatcher)
    {
        Profile = profile;
        _dispatcher = dispatcher;
        _runtime = new TunnelRuntime(profile);
        _runtime.StateChanged += OnStateChanged;
    }

    public TunnelProfile Profile { get; }

    public TunnelState State => _runtime.State;

    public string? Error => _runtime.Error;

    public bool IsRunning => State == TunnelState.Running;

    public bool IsBusy => State == TunnelState.Connecting;

    public string Name => Profile.Name;

    public string TypeText => Loc.Instance.TunnelTypeName(Profile.Type);

    public string EndpointText => Profile.Type == TunnelTypes.Dynamic
        ? $"{Profile.BindHost}:{Profile.BindPort} · SOCKS"
        : $"{Profile.BindHost}:{Profile.BindPort} → {Profile.TargetHost}:{Profile.TargetPort}";

    public string Detail => State == TunnelState.Failed && !string.IsNullOrWhiteSpace(Error)
        ? $"{TypeText} · {EndpointText} · {Error}"
        : $"{TypeText} · {EndpointText}";

    public string StatusText => Loc.Instance.TunnelStateName(State);

    public Brush StatusBrush => State switch
    {
        TunnelState.Running => RunningBrush,
        TunnelState.Connecting => ConnectingBrush,
        TunnelState.Failed => FailedBrush,
        _ => StoppedBrush,
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>任意状态或文案变化，页面据此刷新按钮与状态栏。</summary>
    public event EventHandler? Changed;

    public void Start() => _runtime.Start();

    public void Stop() => _runtime.Stop();

    /// <summary>把编辑结果写回原配置，运行时在下次启动时读取新值。</summary>
    public void Apply(TunnelProfile source)
    {
        Profile.Name = source.Name;
        Profile.Type = source.Type;
        Profile.SshHost = source.SshHost;
        Profile.SshPort = source.SshPort;
        Profile.Username = source.Username;
        Profile.Password = source.Password;
        Profile.BindHost = source.BindHost;
        Profile.BindPort = source.BindPort;
        Profile.TargetHost = source.TargetHost;
        Profile.TargetPort = source.TargetPort;
    }

    public void Refresh() => NotifyAll();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime.StateChanged -= OnStateChanged;
        _runtime.Dispose();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.TryEnqueue(NotifyAll);
    }

    private void NotifyAll()
    {
        if (_disposed)
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
