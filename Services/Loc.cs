using System.ComponentModel;
using System.Globalization;

namespace MoxTerm.Services;

/// <summary>
/// 运行时双语（中/英）字符串服务。所有用户可见文案从这里取，
/// 切换语言时通过 <see cref="PropertyChanged"/>（属性名为 <c>null</c>）刷新全部 x:Bind 绑定，
/// 并通过 <see cref="Changed"/> 通知页面重建下拉项等不走路径绑定的内容。
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    /// <summary>语言已切换；页面用它重建下拉项、动态描述等。</summary>
    public event EventHandler? Changed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>当前生效的语言：<c>zh</c> 或 <c>en</c>。</summary>
    public string ActiveLanguage { get; private set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";

    /// <summary>
    /// 应用设置里的语言选项：<c>Default</c> 跟随系统，<c>zh-CN</c> / <c>en-US</c> 固定语言。
    /// </summary>
    public void Apply(string? setting)
    {
        var language = setting switch
        {
            "zh-CN" => "zh",
            "en-US" => "en",
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en",
        };

        if (language == ActiveLanguage)
        {
            return;
        }

        ActiveLanguage = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string T(string zh, string en) => ActiveLanguage == "zh" ? zh : en;

    // 主窗口与终端工具栏
    public string NavTerminal => T("终端", "Terminal");

    public string ProtocolComingSoon(string protocol) => T(
        $"{protocol} 会话将在后续版本提供。",
        $"{protocol} sessions will be available in a future version.");

    // 新建连接对话框
    public string NewConnection => T("新建连接", "New connection");
    public string Protocol => T("协议", "Protocol");
    public string SshNameLabel => T("名称", "Name");
    public string SshNamePlaceholder => T("可选，默认 SSH: 主机", "Optional, defaults to SSH: host");
    public string SshHostLabel => T("主机", "Host");
    public string SshHostPlaceholder => T("主机名或 IP", "hostname or IP");
    public string SshPortLabel => T("端口", "Port");
    public string SshUsernameLabel => T("用户名", "Username");
    public string SshUsernamePlaceholder => T("可选", "Optional");
    public string SshPasswordLabel => T("密码", "Password");
    public string RevealPassword => T("显示密码", "Reveal password");
    public string HidePassword => T("隐藏密码", "Hide password");
    public string SshNotesLabel => T("备注", "Notes");
    public string Connect => T("连接", "Connect");
    public string SshSaveSession => T("保存此会话", "Save this session");

    // 已保存会话的编辑与删除
    public string EditSession => T("编辑", "Edit");
    public string EditConnection => T("编辑连接", "Edit connection");
    public string DeleteSession => T("删除", "Delete");
    public string SaveChanges => T("保存", "Save");
    public string DeleteSessionConfirm(string name) => T(
        $"确定删除会话“{name}”吗？",
        $"Delete session \"{name}\"?");

    // 新建连接：串口等非 SSH 协议的字段
    public string SerialPortLabel => T("串口", "Serial port");
    public string SerialPortPlaceholder => T("例如 COM3", "e.g. COM3");
    public string BaudRateLabel => T("波特率", "Baud rate");
    public string ConnectionFailed(string target, string message) => T(
        $"无法连接到 {target}：{message}",
        $"Failed to connect to {target}: {message}");

    // 设置页分组
    public string SettingsTitle => T("设置", "Settings");
    public string GroupAppearance => T("外观", "Appearance");
    public string GroupTerminal => T("终端", "Terminal");
    public string GroupInput => T("输入", "Input");
    public string GroupAbout => T("关于", "About");

    // 外观
    public string Language => T("语言", "Language");
    public string LanguageDesc => T("应用界面使用的语言", "Language used by the app UI");
    public string LangSystem => T("跟随系统", "Follow system");
    public string Theme => T("应用主题", "App theme");
    public string ThemeDesc => T("选择要显示的应用主题", "Choose which theme the app displays");
    public string ThemeSystem => T("跟随系统", "Follow system");
    public string ThemeLight => T("浅色", "Light");
    public string ThemeDark => T("深色", "Dark");
    public string ColorScheme => T("终端配色", "Terminal colors");
    public string ColorSchemeDesc => T("终端背景与文字颜色", "Terminal background and text colors");
    public string Font => T("终端字体", "Terminal font");
    public string FontDesc => T("使用系统中已安装的等宽字体", "Monospace fonts installed on the system");
    public string FontSize => T("字体大小", "Font size");
    public string FontSizeDesc => T("取值 8 – 32", "Range 8 – 32");
    public string LineHeight => T("行高倍数", "Line height");
    public string LineHeightDesc => T("相对字符高度，取值 1.0 – 2.0", "Relative to character height, 1.0 – 2.0");

    // 终端
    public string DefaultShell => T("默认 Shell", "Default shell");
    public string DefaultShellDesc => T("新建会话时使用的 Shell", "Shell used for new sessions");
    public string WorkingDir => T("起始目录", "Working directory");
    public string WorkingDirEmpty => T("留空则使用进程当前目录。", "Leave empty to use the current process directory.");
    public string WorkingDirInvalid => T("目录不存在，不会被保存。", "Directory doesn't exist and won't be saved.");
    public string Browse => T("浏览", "Browse");
    public string UseParentDir => T("使用父进程目录", "Use parent process directory");
    public string Scrollback => T("回滚缓冲行数", "Scrollback lines");
    public string ScrollbackDesc => T("保留的历史输出行数，取值 200 – 100000", "History lines kept, 200 – 100000");
    public string CursorShape => T("光标形状", "Cursor shape");
    public string CursorShapeDesc => T("终端光标的显示方式", "How the terminal cursor is displayed");
    public string CursorBlink => T("光标闪烁", "Cursor blinking");
    public string CursorBlinkDesc => T("仅在终端获得焦点时闪烁", "Blink only while the terminal is focused");
    public string Backend => T("终端后端", "Terminal backend");

    // 输入
    public string ConfirmPaste => T("粘贴多行文本时确认", "Confirm multi-line paste");
    public string ConfirmPasteDesc => T(
        "剪贴板中包含换行时，粘贴前先弹窗确认，避免误执行多行命令",
        "Ask before pasting text containing newlines, to avoid running multi-line commands by mistake");

    // 关于
    public string Cancel => T("取消", "Cancel");

    public string AboutVersion(string version) => T(
        $"版本 {version} · 基于 .NET 10 与 Windows App SDK（WinUI 3）构建",
        $"Version {version} · Built with .NET 10 and Windows App SDK (WinUI 3)");

    // 选项显示名（配色 / 光标 / 后端 / 字体）
    public string SchemeDark => T("深色", "Dark");
    public string SchemeLight => T("浅色", "Light");
    public string SchemeGreen => T("复古绿", "Retro green");
    public string SchemeFollow => T("跟随应用主题", "Follow app theme");
    public string CursorBlock => T("方块", "Block");
    public string CursorUnderline => T("下划线", "Underline");
    public string CursorBar => T("竖线", "Bar");
    public string BackendPipe => T("管道（兼容模式）", "Pipe (compatibility mode)");
    public string BackendPipeDesc => T(
        "重定向标准输入输出，兼容性最好；输出为纯文本，不支持窗口尺寸协商与 ANSI 颜色。",
        "Redirects standard input and output; best compatibility. Plain-text output only, no size negotiation or ANSI colors.");
    public string BackendConPty => T("伪控制台（ConPTY）", "Pseudo console (ConPTY)");
    public string BackendConPtyDesc => T(
        "使用 Windows 伪控制台，支持 VT 序列与窗口尺寸协商；部分受限环境下子进程可能无法挂载。",
        "Uses the Windows pseudo console with VT sequences and size negotiation; may fail to attach in restricted environments.");
    public string FontSystemDefault => T("系统默认", "System default");

    // 终端运行时
    public string SessionStartFailed(string name, string message) => T(
        $"无法启动 {name}：{message}",
        $"Failed to start {name}: {message}");
    public string SessionExited(int exitCode) => T(
        $"[进程已退出，代码 {exitCode}]",
        $"[Process exited with code {exitCode}]");
    public string PasteTitle => T("粘贴多行文本", "Paste multiple lines");
    public string PasteContent(int lines) => T(
        $"剪贴板中包含 {lines} 行文本，确认要粘贴到终端吗？",
        $"The clipboard contains {lines} lines of text. Paste into the terminal?");
    public string PastePrimary => T("粘贴", "Paste");

    // 功能分组导航
    public string GroupTools => T("工具", "Tools");
    public string GroupServers => T("服务器", "Servers");
    public string NavNetServices => T("网络服务", "Network services");
    public string NavTunneling => T("SSH 隧道（端口转发）", "SSH tunnel (port forwarding)");
    public string NavKeyGen => T("SSH 密钥生成器", "SSH key generator");
    public string NavOpenPorts => T("列出开放端口", "List open ports");
    public string NavWakeOnLan => T("Wake On Lan", "Wake On Lan");
    public string NavNetScan => T("网络扫描", "Network scanner");
    public string NavPortScan => T("端口扫描", "Ports scanner");
    public string NavPacketCapture => T("网络抓包", "Packet capture");
    public string NavXServer => T("X Server", "X Server");
    public string ComingSoon => T("即将推出", "Coming soon");
    public string PlannedFeatures => T("规划中的能力", "Planned capabilities");

    // 网络服务
    public string FeatureNetServicesDesc => T(
        "在本地一键启动常用网络服务，便于调试与文件分发。",
        "Start common network services locally with one click, for debugging and file distribution.");
    public IReadOnlyList<string> FeatureNetServicesItems =>
    [
        T("TFTP / FTP / HTTP 文件服务", "TFTP / FTP / HTTP file services"),
        T("服务端口与运行状态管理", "Service ports and status management"),
    ];

    // SSH 密钥生成器
    public string FeatureKeyGenDesc => T(
        "生成与管理 SSH 密钥对。",
        "Generate and manage SSH key pairs.");
    public IReadOnlyList<string> FeatureKeyGenItems =>
    [
        T("RSA / Ed25519 密钥生成", "RSA / Ed25519 key generation"),
        T("公钥导出（OpenSSH 格式）", "Public key export (OpenSSH format)"),
        T("私钥口令保护", "Private key passphrase protection"),
    ];

    // 列出开放端口
    public string FeatureOpenPortsDesc => T(
        "查看本机哪些进程正在监听或占用端口。",
        "See which processes are listening on or using ports.");
    public IReadOnlyList<string> FeatureOpenPortsItems =>
    [
        T("TCP / UDP 监听与连接列表", "TCP / UDP listeners and connections"),
        T("进程名与 PID 关联", "Process name and PID mapping"),
        T("按端口或进程过滤", "Filter by port or process"),
    ];

    // Wake On Lan
    public string FeatureWakeOnLanDesc => T(
        "通过魔术包唤醒局域网内的设备。",
        "Wake devices on the LAN with magic packets.");
    public IReadOnlyList<string> FeatureWakeOnLanItems =>
    [
        T("按 MAC 地址发送唤醒包", "Send wake-up packets by MAC address"),
        T("常用设备保存", "Save frequently used devices"),
        T("广播地址与端口自定义", "Custom broadcast address and port"),
    ];

    // 网络扫描
    public string FeatureNetScanDesc => T(
        "扫描网段内的在线主机。",
        "Discover live hosts on a network segment.");
    public IReadOnlyList<string> FeatureNetScanItems =>
    [
        T("IP 段主机发现", "Live host discovery over an IP range"),
        T("主机名与 MAC 地址解析", "Hostname and MAC address resolution"),
        T("结果导出", "Result export"),
    ];

    // 端口扫描
    public string FeaturePortScanDesc => T(
        "探测目标主机开放的端口。",
        "Probe open ports on a target host.");
    public IReadOnlyList<string> FeaturePortScanItems =>
    [
        T("常见端口快速扫描", "Quick scan of common ports"),
        T("自定义端口范围", "Custom port ranges"),
        T("服务 Banner 识别", "Service banner grabbing"),
    ];

    // 网络抓包
    public string FeaturePacketCaptureDesc => T(
        "捕获并查看网络数据包。",
        "Capture and inspect network packets.");
    public IReadOnlyList<string> FeaturePacketCaptureItems =>
    [
        T("网卡选择与捕获过滤器", "Adapter selection and capture filters"),
        T("会话列表与十六进制视图", "Session list and hex view"),
        T("导出 pcap 文件", "Export to pcap files"),
    ];

    // 隧道转发
    public string FeatureTunnelDesc => T(
        "基于 SSH 的加密端口转发，把远程服务安全地映射到本地。",
        "Encrypted port forwarding over SSH, securely mapping remote services to local ports.");
    public IReadOnlyList<string> FeatureTunnelItems =>
    [
        T("本地（Local）转发", "Local forwarding"),
        T("远程（Remote）转发", "Remote forwarding"),
        T("动态 SOCKS 代理", "Dynamic SOCKS proxy"),
        T("隧道列表与运行状态监控", "Tunnel list with live status"),
    ];

    // X Server
    public string FeatureXServerDesc => T(
        "在 Windows 桌面上显示远程 Linux/Unix 的 X11 图形界面。",
        "Display remote Linux/Unix X11 graphical applications on the Windows desktop.");
    public IReadOnlyList<string> FeatureXServerItems =>
    [
        T("内嵌 X 服务器，随应用启动", "Built-in X server that starts with the app"),
        T("与 SSH 会话联动的 X11 转发", "X11 forwarding tied to SSH sessions"),
        T("多窗口与根窗口（Rooted）模式", "Multi-window and rooted modes"),
    ];
}
