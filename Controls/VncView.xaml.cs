using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using MoxTerm.Services;
using Windows.Foundation;
using Windows.System;

namespace MoxTerm.Controls;

/// <summary>
/// VNC 远程桌面面板：显示远端画面，并把鼠标、滚轮、键盘事件发给服务器。
/// 网络读取在后台线程完成，画面更新经 DispatcherQueue 回到 UI 线程绘制。
/// </summary>
public sealed partial class VncView : UserControl
{
    private VncSession? _session;
    private WriteableBitmap? _bitmap;
    private Stream? _pixelStream;
    private bool _disposed;
    private bool _pointerPressed;
    private bool _shiftDown;
    private bool _frameShown;
    private bool _connecting;
    private int _buttons;
    private int _width;
    private int _height;

    /// <summary>连接参数，由调用方在加入可视树之前赋值。</summary>
    public ShellProfile Profile { get; set; } = new("VNC", "");

    private Loc Loc => Loc.Instance;

    public VncView()
    {
        InitializeComponent();
        ReconnectButton.Content = Loc.VncReconnect;

        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
        AddHandler(KeyUpEvent, new KeyEventHandler(OnKeyUp), true);

        Loaded += OnLoaded;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachSession();
        _pixelStream?.Dispose();
        _pixelStream = null;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async void OnReconnectClick(object sender, RoutedEventArgs e) => await ConnectAsync();

    private void DetachSession()
    {
        var session = _session;
        _session = null;

        if (session is not null)
        {
            session.RectUpdated -= OnRectUpdated;
            session.Resized -= OnResized;
            session.Disconnected -= OnDisconnected;
            session.Dispose();
        }
    }

    private async Task ConnectAsync()
    {
        if (_disposed || _connecting)
        {
            return;
        }

        _connecting = true;
        DetachSession();
        SetStatus(Loc.VncConnecting);
        InfoText.Text = string.Empty;
        _bitmap = null;
        _frameShown = false;
        Screen.Source = null;
        ShowOverlay(Loc.VncConnecting);

        var session = new VncSession(
            Profile.Host ?? string.Empty,
            Profile.Port,
            Profile.Password ?? string.Empty);

        try
        {
            await Task.Run(session.Connect);

            if (_disposed)
            {
                session.Dispose();
                return;
            }

            _session = session;
            _width = session.Width;
            _height = session.Height;

            session.RectUpdated += OnRectUpdated;
            session.Resized += OnResized;
            session.Disconnected += OnDisconnected;

            CreateBitmap(session.Width, session.Height);
            InfoText.Text = session.DesktopName;
            SetStatus(Loc.VncLoadingFrame);
            Surface.Focus(FocusState.Programmatic);
            ShowOverlay(Loc.VncLoadingFrame);

            // 订阅与位图都就绪后再开始收画面，避免首帧整屏更新被丢弃
            session.Start();
        }
        catch (Exception ex)
        {
            session.Dispose();
            HideOverlay();
            SetStatus($"{Loc.VncConnectFailed} {ex.Message}");
        }
        finally
        {
            _connecting = false;
        }
    }

    private void ShowOverlay(string text)
    {
        ConnectingText.Text = text;
        ConnectingRing.IsActive = true;
        ConnectingOverlay.Visibility = Visibility.Visible;
    }

    private void HideOverlay()
    {
        ConnectingRing.IsActive = false;
        ConnectingOverlay.Visibility = Visibility.Collapsed;
    }

    private void CreateBitmap(int width, int height)
    {
        _pixelStream?.Dispose();
        _width = width;
        _height = height;
        _bitmap = new WriteableBitmap(width, height);
        _pixelStream = _bitmap.PixelBuffer.AsStream();
        Screen.Source = _bitmap;
    }

    private void SetStatus(string text) => StatusText.Text = text;

    // ---------- 画面 ----------

    private void OnRectUpdated(VncRect rect) =>
        DispatcherQueue.TryEnqueue(() => ApplyRect(rect));

    private void OnResized(int width, int height) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            CreateBitmap(width, height);
            SetStatus(Loc.VncConnected(width, height));
        });

    private void OnDisconnected(string reason) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            HideOverlay();
            SetStatus($"{Loc.VncDisconnected} {reason}");
        });

    private void ApplyRect(VncRect rect)
    {
        var bitmap = _bitmap;
        var stream = _pixelStream;
        if (bitmap is null || stream is null || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        try
        {
            var rowBytes = rect.Width * 4;

            if (rect.X == 0 && rect.Width == _width)
            {
                // 整行矩形（整屏刷新最常见）直接一次写完，避免逐行 Seek
                stream.Seek((long)rect.Y * _width * 4, SeekOrigin.Begin);
                stream.Write(rect.Pixels, 0, rowBytes * rect.Height);
            }
            else
            {
                for (var row = 0; row < rect.Height; row++)
                {
                    stream.Seek(((rect.Y + row) * _width + rect.X) * 4, SeekOrigin.Begin);
                    stream.Write(rect.Pixels, row * rowBytes, rowBytes);
                }
            }

            bitmap.Invalidate();

            if (!_frameShown)
            {
                _frameShown = true;
                HideOverlay();
                SetStatus(Loc.VncConnected(_width, _height));
            }
        }
        catch (Exception)
        {
            // 尺寸切换瞬间可能写越界，直接丢弃这一帧
        }
    }

    // ---------- 鼠标 ----------

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        Surface.Focus(FocusState.Programmatic);
        _pointerPressed = true;
        _buttons = ButtonMask(e);
        SendPointer(e);
        Screen.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        if (_pointerPressed)
        {
            _buttons = ButtonMask(e);
        }

        SendPointer(e);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_session is null || !_pointerPressed)
        {
            return;
        }

        _pointerPressed = false;
        _buttons = 0;
        SendPointer(e);
        Screen.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _pointerPressed = false;
        _buttons = 0;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_session is null || !TryMapPoint(e.GetCurrentPoint(Screen).Position, out var x, out var y))
        {
            return;
        }

        var delta = e.GetCurrentPoint(Screen).Properties.MouseWheelDelta;
        var button = delta > 0 ? 8 : 16; // RFB：滚轮上 8、下 16
        _session.SendPointer(x, y, _buttons | button);
        _session.SendPointer(x, y, _buttons);
        e.Handled = true;
    }

    private void SendPointer(PointerRoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        if (TryMapPoint(e.GetCurrentPoint(Screen).Position, out var x, out var y))
        {
            _session.SendPointer(x, y, _buttons);
        }
    }

    /// <summary>鼠标按键位：左 1、中 2、右 4。</summary>
    private static int ButtonMask(PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(null).Properties;
        var buttons = 0;

        if (properties.IsLeftButtonPressed)
        {
            buttons |= 1;
        }

        if (properties.IsMiddleButtonPressed)
        {
            buttons |= 2;
        }

        if (properties.IsRightButtonPressed)
        {
            buttons |= 4;
        }

        return buttons;
    }

    /// <summary>把控件坐标换算成远端像素坐标（Image 按 Uniform 等比居中显示）。</summary>
    private bool TryMapPoint(Point point, out int x, out int y)
    {
        x = 0;
        y = 0;

        var controlWidth = Screen.ActualWidth;
        var controlHeight = Screen.ActualHeight;
        if (_width <= 0 || _height <= 0 || controlWidth <= 0 || controlHeight <= 0)
        {
            return false;
        }

        var scale = Math.Min(controlWidth / _width, controlHeight / _height);
        var shownWidth = _width * scale;
        var shownHeight = _height * scale;
        var offsetX = (controlWidth - shownWidth) / 2;
        var offsetY = (controlHeight - shownHeight) / 2;

        var mappedX = (int)((point.X - offsetX) / scale);
        var mappedY = (int)((point.Y - offsetY) / scale);

        if (mappedX < 0 || mappedY < 0 || mappedX >= _width || mappedY >= _height)
        {
            return false;
        }

        x = mappedX;
        y = mappedY;
        return true;
    }

    // ---------- 键盘 ----------

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        if (e.Key is VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift)
        {
            _shiftDown = true;
        }

        var keysym = VncKeysym.FromVirtualKey(e.Key, _shiftDown);
        if (keysym == 0)
        {
            return;
        }

        _session.SendKey(keysym, down: true);
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift)
        {
            _shiftDown = false;
        }

        if (_session is null)
        {
            return;
        }

        var keysym = VncKeysym.FromVirtualKey(e.Key, _shiftDown);
        if (keysym == 0)
        {
            return;
        }

        _session.SendKey(keysym, down: false);
        e.Handled = true;
    }
}

/// <summary>Windows 虚拟键到 X11 keysym 的映射（覆盖常用按键与符号）。</summary>
internal static class VncKeysym
{
    public static uint FromVirtualKey(VirtualKey key, bool shift)
    {
        var code = (int)key;

        // 字母：未按 Shift 时发送小写 keysym，否则远端会把小写输入显示成大写
        if (code is >= 65 and <= 90)
        {
            return shift ? (uint)code : (uint)(code + 32);
        }

        // 数字行（Shift 时发送对应符号）
        if (code is >= 48 and <= 57)
        {
            if (!shift)
            {
                return (uint)code;
            }

            return (uint)"!@#$%^&*()"[code - 48];
        }

        // 小键盘数字
        if (code is >= 96 and <= 105)
        {
            return (uint)(48 + (code - 96));
        }

        if (code is >= 112 and <= 123)
        {
            return (uint)(0xFFBE + (code - 112));
        }

        return (uint)(code switch
        {
            32 => 0x20,
            8 => 0xFF08,
            9 => 0xFF09,
            13 => 0xFF0D,
            27 => 0xFF1B,
            45 => 0xFF63,
            46 => 0xFFFF,
            36 => 0xFF50,
            35 => 0xFF57,
            33 => 0xFF55,
            34 => 0xFF56,
            37 => 0xFF51,
            38 => 0xFF52,
            39 => 0xFF53,
            40 => 0xFF54,
            16 => 0xFFE1,
            17 => 0xFFE3,
            18 => 0xFFE9,
            20 => 0xFFE5,
            186 => shift ? 0x3A : 0x3B,
            187 => shift ? 0x2B : 0x3D,
            188 => shift ? 0x3C : 0x2C,
            189 => shift ? 0x5F : 0x2D,
            190 => shift ? 0x3E : 0x2E,
            191 => shift ? 0x3F : 0x2F,
            192 => shift ? 0x7E : 0x60,
            219 => shift ? 0x7B : 0x5B,
            220 => shift ? 0x7C : 0x5C,
            221 => shift ? 0x7D : 0x5D,
            222 => shift ? 0x22 : 0x27,
            _ => 0,
        });
    }
}
