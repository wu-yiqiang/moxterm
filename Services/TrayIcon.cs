using System.Runtime.InteropServices;

namespace MoxTerm.Services;

/// <summary>
/// 通知区域（托盘）图标：最小化到托盘时显示，左键单击还原窗口，右键弹出“显示/退出”菜单。
/// <para>
/// 托盘回调需要一个窗口过程，这里创建独立的 message-only 窗口来接收，
/// 避免去子类化 WinUI 主窗口的消息处理。
/// </para>
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WM_APP = 0x8000;
    private const int WM_TRAY = WM_APP + 1;
    private const int WM_COMMAND = 0x0111;
    private const int WM_NULL = 0x0000;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1;
    private const uint NIF_ICON = 0x2;
    private const uint NIF_TIP = 0x4;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x10;
    private const uint LR_DEFAULTSIZE = 0x40;

    private const uint MF_STRING = 0;
    private const uint MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2;

    private const int ID_SHOW = 1;
    private const int ID_EXIT = 2;
    private const uint TRAY_ID = 1;
    private const int IDI_APPLICATION = 32512;

    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly Action _onShow;
    private readonly Action _onExit;
    private readonly WndProc _wndProc;
    private readonly string _className = "MoxTermTrayIcon";
    private readonly IntPtr _icon;
    private IntPtr _hwnd;
    private bool _visible;
    private bool _disposed;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public TrayIcon(Action onShow, Action onExit)
    {
        _onShow = onShow;
        _onExit = onExit;
        _wndProc = OnMessage;

        _icon = LoadTrayIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        CreateHostWindow();
    }

    /// <summary>在通知区域显示图标（重复调用无副作用）。</summary>
    public void Show()
    {
        if (_visible || _hwnd == IntPtr.Zero)
        {
            return;
        }

        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = TRAY_ID,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _icon,
            szTip = "MoxTerm",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        _visible = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    /// <summary>从通知区域移除图标。</summary>
    public void Hide()
    {
        if (!_visible || _hwnd == IntPtr.Zero)
        {
            return;
        }

        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = TRAY_ID,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        Shell_NotifyIcon(NIM_DELETE, ref data);
        _visible = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Hide();

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        UnregisterClass(_className, GetModuleHandle(null));
    }

    private static IntPtr LoadTrayIcon(string path)
    {
        var icon = LoadImage(IntPtr.Zero, path, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        return icon != IntPtr.Zero ? icon : LoadIconW(IntPtr.Zero, new IntPtr(IDI_APPLICATION));
    }

    private void CreateHostWindow()
    {
        var instance = GetModuleHandle(null);

        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = _className,
        };

        RegisterClassEx(ref windowClass);

        _hwnd = CreateWindowEx(
            0, _className, "MoxTerm", 0,
            0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, instance, IntPtr.Zero);
    }

    private IntPtr OnMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_TRAY:
                var mouseMessage = (int)(lParam.ToInt64() & 0xFFFF);
                if (mouseMessage == WM_LBUTTONUP)
                {
                    _onShow();
                }
                else if (mouseMessage == WM_RBUTTONUP)
                {
                    ShowMenu();
                }

                return IntPtr.Zero;

            case WM_COMMAND:
                var command = wParam.ToInt32() & 0xFFFF;
                if (command == ID_SHOW)
                {
                    _onShow();
                }
                else if (command == ID_EXIT)
                {
                    _onExit();
                }

                return IntPtr.Zero;

            default:
                return DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        AppendMenu(menu, MF_STRING, new UIntPtr(ID_SHOW), Loc.Instance.TrayShow);
        AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
        AppendMenu(menu, MF_STRING, new UIntPtr(ID_EXIT), Loc.Instance.TrayExit);

        GetCursorPos(out var point);

        // 不先置前台的话，菜单点击后不会自动消失
        SetForegroundWindow(_hwnd);
        TrackPopupMenu(menu, TPM_RIGHTBUTTON, point.X, point.Y, 0, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr instance, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", SetLastError = true)]
    private static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr itemId, string? item);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(
        IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
