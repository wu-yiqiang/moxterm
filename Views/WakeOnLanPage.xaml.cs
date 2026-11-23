using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MoxTerm.Controls;
using MoxTerm.Services;
using Windows.Globalization.NumberFormatting;

namespace MoxTerm.Views;

/// <summary>
/// Wake On Lan 工作区：保存常用设备，向 MAC 地址发送魔术包将其唤醒。
/// </summary>
public sealed partial class WakeOnLanPage : Page
{
    private readonly ObservableCollection<WolDevice> _devices = new();

    /// <summary>正在发送唤醒包，期间禁用工具栏避免重复操作。</summary>
    private bool _busy;

    /// <summary>界面文案源；x:Bind 绑定到它的属性，语言切换时自动刷新。</summary>
    private Loc Loc => Loc.Instance;

    public WakeOnLanPage()
    {
        InitializeComponent();

        DevicesList.ItemsSource = _devices;

        foreach (var device in App.Settings.Current.WolDevices)
        {
            _devices.Add(device);
        }

        UpdateEmptyState();
        UpdateButtons();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        var selected = DevicesList.SelectedItem is WolDevice;

        WakeButton.IsEnabled = !_busy && selected;
        EditButton.IsEnabled = !_busy && selected;
        DeleteButton.IsEnabled = !_busy && selected;
    }

    private void UpdateEmptyState() =>
        EmptyText.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private async void OnListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (DevicesList.SelectedItem is WolDevice device)
        {
            await WakeAsync(device);
        }
    }

    private async void OnWakeClick(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedItem is WolDevice device)
        {
            await WakeAsync(device);
        }
    }

    private async Task WakeAsync(WolDevice device)
    {
        if (_busy)
        {
            return;
        }

        if (!WolService.TryParseMac(device.Mac, out var mac))
        {
            StatusText.Text = Loc.WolInvalid;
            return;
        }

        _busy = true;
        UpdateButtons();
        StatusText.Text = Loc.WolSending(device.DisplayName);

        try
        {
            await WolService.SendAsync(mac, device.BroadcastAddress, device.Port);
            StatusText.Text = Loc.WolSent(device.DisplayName);
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.WolSendFailed(ex.Message);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private async void OnNewClick(object sender, RoutedEventArgs e) => await ShowDeviceDialogAsync(null);

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedItem is WolDevice device)
        {
            await ShowDeviceDialogAsync(device);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedItem is not WolDevice device)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.WolDelete,
            Content = new TextBlock
            {
                Text = Loc.WolDeleteConfirm(device.DisplayName),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = Loc.WolDelete,
            CloseButtonText = Loc.Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Opened += (_, _) => DialogHelper.SwapButtons(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _devices.Remove(device);
        App.Settings.Current.WolDevices.Remove(device);
        App.Settings.Save();

        UpdateEmptyState();
        UpdateButtons();
    }

    /// <summary>新建 / 编辑设备：名称、MAC、广播地址与端口；传入设备时进入编辑模式。</summary>
    private async Task ShowDeviceDialogAsync(WolDevice? device)
    {
        var isNew = device is null;
        var draft = device is null
            ? new WolDevice()
            : new WolDevice
            {
                Name = device.Name,
                Mac = device.Mac,
                BroadcastAddress = device.BroadcastAddress,
                Port = device.Port,
            };

        var nameBox = new TextBox
        {
            Header = Loc.WolNameLabel,
            PlaceholderText = Loc.WolNamePlaceholder,
            Text = draft.Name,
        };
        var macBox = new TextBox
        {
            Header = Loc.WolMacLabel,
            PlaceholderText = Loc.WolMacPlaceholder,
            Text = draft.Mac,
        };
        var broadcastBox = new TextBox
        {
            Header = Loc.WolBroadcastLabel,
            Text = string.IsNullOrWhiteSpace(draft.BroadcastAddress) ? "255.255.255.255" : draft.BroadcastAddress,
        };
        var portBox = PortInput.Create(Loc.WolPortLabel, draft.Port is >= 1 and <= 65535 ? draft.Port : 9);

        var hint = new TextBlock
        {
            Text = Loc.WolInvalid,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var content = new StackPanel { Spacing = 12, Width = 380 };
        content.Children.Add(nameBox);
        content.Children.Add(macBox);
        content.Children.Add(broadcastBox);
        content.Children.Add(portBox);
        content.Children.Add(hint);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = isNew ? Loc.WolDialogNew : Loc.WolDialogEdit,
            Content = content,
            PrimaryButtonText = isNew ? Loc.WolNew : Loc.SaveChanges,
            CloseButtonText = Loc.Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };

        dialog.Opened += (_, _) =>
        {
            macBox.Focus(FocusState.Programmatic);
            DialogHelper.SwapButtons(dialog);
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            var broadcast = string.IsNullOrWhiteSpace(broadcastBox.Text)
                ? "255.255.255.255"
                : broadcastBox.Text.Trim();
            var port = PortInput.Read(portBox);

            var macValid = WolService.TryParseMac(macBox.Text, out var macBytes);
            var broadcastValid = IPAddress.TryParse(broadcast, out var address)
                && address.AddressFamily == AddressFamily.InterNetwork;
            var invalid = !macValid || !broadcastValid || port is < 1 or > 65535;

            hint.Visibility = invalid ? Visibility.Visible : Visibility.Collapsed;
            if (invalid)
            {
                args.Cancel = true;
                return;
            }

            draft.Name = nameBox.Text.Trim();
            draft.Mac = WolService.FormatMac(macBytes);
            draft.BroadcastAddress = broadcast;
            draft.Port = port;
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (device is null)
        {
            App.Settings.Current.WolDevices.Add(draft);
            App.Settings.Save();

            _devices.Add(draft);
            DevicesList.SelectedItem = draft;
            UpdateEmptyState();
        }
        else
        {
            // 直接替换列表项，触发界面刷新（WolDevice 不实现属性变更通知）
            var index = _devices.IndexOf(device);
            if (index >= 0)
            {
                _devices[index] = draft;
            }

            var stored = App.Settings.Current.WolDevices.IndexOf(device);
            if (stored >= 0)
            {
                App.Settings.Current.WolDevices[stored] = draft;
            }

            App.Settings.Save();
            DevicesList.SelectedItem = draft;
        }

        UpdateButtons();
    }
}
