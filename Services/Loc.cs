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
    public string NavTerminal => T("会话", "Sessions");
    public string NavShortcuts => T("快捷键", "Shortcuts");

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
    public string SessionGroupLabel => T("分组", "Group");
    public string SessionGroupPlaceholder => T("可选，用于给会话归类", "Optional, groups saved sessions");
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
    public string WorkingDirReset => T("恢复默认（使用进程当前目录）", "Reset to default (process directory)");
    public string Browse => T("浏览", "Browse");

    // SFTP 面板
    public string SftpLocal => T("本地", "Local");
    public string SftpRemote => T("远程", "Remote");
    public string SftpUpload => T("上传", "Upload");
    public string SftpDownload => T("下载", "Download");
    public string SftpNewFolder => T("新建文件夹", "New folder");
    public string SftpRename => T("重命名", "Rename");
    public string SftpRefresh => T("刷新", "Refresh");
    public string SftpUp => T("上级", "Up");
    public string SftpCreate => T("创建", "Create");
    public string SftpConnecting => T("正在连接…", "Connecting…");
    public string SftpConnectFailed => T("连接失败：", "Connection failed: ");
    public string SftpAuthFailed => T("用户名或密码错误。请编辑该连接，重新输入密码后再试。", "Wrong username or password. Edit this connection and re-enter the password.");
    public string SftpFailed => T("操作失败：", "Operation failed: ");
    public string SftpPathNotFound => T("目录不存在。", "The directory doesn't exist.");
    public string SftpNoFileSelected => T("请先选择文件。", "Select a file first.");
    public string SftpSelectOne => T("请先选择一个条目。", "Select an item first.");
    public string SftpUploading => T("正在上传", "Uploading");
    public string SftpDropToUpload => T("释放以上传到当前目录", "Drop to upload here");
    public string SftpBusy => T("正在传输，请稍候再试。", "A transfer is already running, please wait.");
    public string SftpDownloading => T("正在下载", "Downloading");
    public string SftpFolderName => T("文件夹名称", "Folder name");
    public string SftpNewName => T("新名称", "New name");
    public string SftpInvalidName => T("名称不能为空，且不能包含 / 或 \\。", "The name must not be empty or contain / or \\.");
    public string SftpDeleteConfirm => T("确定删除以下条目？", "Delete the following items?");
    public string Scrollback => T("回滚缓冲行数", "Scrollback lines");
    public string ScrollbackDesc => T("保留的历史输出行数，取值 200 – 100000", "History lines kept, 200 – 100000");
    public string CursorShape => T("光标形状", "Cursor shape");
    public string CursorShapeDesc => T("终端光标的显示方式", "How the terminal cursor is displayed");
    public string CursorBlink => T("光标闪烁", "Cursor blinking");
    public string CursorBlinkDesc => T("仅在终端获得焦点时闪烁", "Blink only while the terminal is focused");
    public string Backend => T("终端后端", "Terminal backend");

    // VNC 远程桌面
    public string VncReconnect => T("重新连接", "Reconnect");
    public string VncConnecting => T("正在连接…", "Connecting…");
    public string VncLoadingFrame => T("正在加载画面…", "Loading screen…");
    public string VncConnected(int width, int height) => T($"已连接（{width} × {height}）", $"Connected ({width} × {height})");
    public string VncConnectFailed => T("连接失败：", "Connection failed: ");
    public string VncDisconnected => T("连接已断开：", "Disconnected: ");

    // RDP（经系统远程桌面客户端打开）
    public string RdpClipboard => T("共享剪贴板", "Share clipboard");
    public string RdpAllDrives => T("共享所有本地驱动器", "Share all local drives");
    public string RdpDrivesLabel => T("共享驱动器", "Shared drives");
    public string RdpDriveHint => T("RDP 按驱动器共享，无法只共享单个文件夹。", "RDP shares whole drives; individual folders can't be shared.");
    public string RdpExternalHint => T("RDP 会话将在系统远程桌面窗口中打开。", "RDP sessions open in the Remote Desktop window.");
    public string RdpLaunchFailed => T("无法启动远程桌面客户端：", "Failed to launch Remote Desktop: ");

    // 窗口
    public string GroupWindow => T("窗口", "Window");
    public string HideOnMinimize => T("最小化时在通知区域隐藏终端", "Hide the terminal in the notification area when minimized");
    public string HideOnMinimizeDesc => T(
        "最小化后隐藏窗口与任务栏按钮，应用继续在后台运行；点击通知区域图标可恢复窗口",
        "Hide the window and taskbar button when minimized while the app keeps running; click the notification area icon to restore it");
    public string TrayShow => T("显示主窗口", "Show main window");
    public string TrayExit => T("退出", "Exit");

    // 输入
    public string CopyOnSelect => T("选中即复制", "Copy on select");
    public string CopyOnSelectDesc => T(
        "在终端中拖选文本后自动复制到剪贴板，无需再按复制快捷键",
        "Automatically copy selected terminal text to the clipboard without pressing a shortcut");
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
    public string OpenLinkHint => T("打开链接", "Open link");

    // 终端右键菜单（分屏）
    public string Copy => T("复制", "Copy");
    public string SplitLeftRight => T("左右分屏", "Split left/right");
    public string SplitTopBottom => T("上下分屏", "Split top/bottom");
    public string ClosePane => T("关闭此分屏", "Close pane");
    public string KeyboardShortcuts => T("键盘快捷键", "Keyboard shortcuts");
    public string ShortcutCopy => T("复制选中内容", "Copy selection");
    public string ShortcutInterrupt => T("中断当前命令", "Interrupt current command");
    public string ShortcutPaste => T("粘贴", "Paste");
    public string ShortcutClear => T("清屏", "Clear screen");

    public string PasteTitle => T("粘贴多行文本", "Paste multiple lines");
    public string PasteContent(int lines) => T(
        $"剪贴板中包含 {lines} 行文本，确认要粘贴到终端吗？",
        $"The clipboard contains {lines} lines of text. Paste into the terminal?");
    public string PastePrimary => T("粘贴", "Paste");

    // 功能分组导航
    public string GroupTools => T("工具", "Tools");
    public string GroupServers => T("服务器", "Servers");
    public string NavNetServices => T("网络服务", "Network services");
    public string NavTunneling => T("隧道", "Tunnel");
    public string NavKeyGen => T("SSH 密钥生成器", "SSH key generator");
    public string NavOpenPorts => T("列出开放端口", "List open ports");
    public string NavWakeOnLan => T("Wake On Lan", "Wake On Lan");
    public string NavNetScan => T("网络扫描", "Network scanner");
    public string NavPortScan => T("端口扫描", "Ports scanner");
    public string NavPacketCapture => T("网络抓包", "Packet capture");
    public string NavDiagnostics => T("网络诊断", "Network diagnostics");
    public string NavPingTraceroute => T("连通与追踪", "Ping / Traceroute");
    public string NavXServer => T("X Server", "X Server");
    public string ComingSoon => T("即将推出", "Coming soon");
    public string PlannedFeatures => T("规划中的能力", "Planned capabilities");

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

    // SSH 隧道（端口转发）页面
    public string TunnelSubtitle => T(
        "通过 SSH 建立加密隧道：把远程服务映射到本地，或把本地服务发布到远程。",
        "Encrypted tunnels over SSH: map remote services locally, or publish local services remotely.");
    public string TunnelNew => T("新建隧道", "New tunnel");
    public string TunnelStart => T("启动", "Start");
    public string TunnelStop => T("停止", "Stop");
    public string TunnelEmpty => T(
        "还没有隧道。点击“新建隧道”添加一条。",
        "No tunnels yet. Click \"New tunnel\" to add one.");
    public string TunnelTypeLocal => T("本地转发", "Local");
    public string TunnelTypeRemote => T("远程转发", "Remote");
    public string TunnelTypeDynamic => T("动态 SOCKS", "Dynamic SOCKS");
    public string TunnelStateStopped => T("已停止", "Stopped");
    public string TunnelStateConnecting => T("正在连接…", "Connecting…");
    public string TunnelStateRunning => T("运行中", "Running");
    public string TunnelStateFailed => T("失败", "Failed");
    public string TunnelStarted(string name) => T($"隧道“{name}”已启动。", $"Tunnel \"{name}\" started.");
    public string TunnelStopped(string name) => T($"隧道“{name}”已停止。", $"Tunnel \"{name}\" stopped.");
    public string TunnelStartFailed(string message) => T($"启动隧道失败：{message}", $"Failed to start the tunnel: {message}");
    public string TunnelDeleteConfirm(string name) => T($"确定删除隧道“{name}”吗？", $"Delete tunnel \"{name}\"?");
    public string TunnelDialogNew => T("新建隧道", "New tunnel");
    public string TunnelDialogEdit => T("编辑隧道", "Edit tunnel");
    public string TunnelNameLabel => T("名称", "Name");
    public string TunnelNamePlaceholder => T("可选，默认使用监听端口", "Optional, defaults to the listen port");
    public string TunnelTypeLabel => T("类型", "Type");
    public string TunnelSshHostLabel => T("SSH 服务器", "SSH server");
    public string TunnelSshPortLabel => T("SSH 端口", "SSH port");
    public string TunnelBindHostLabel => T("本地监听地址", "Local listen address");
    public string TunnelBindHostRemoteLabel => T("远程监听地址", "Remote listen address");
    public string TunnelBindPortLabel => T("监听端口", "Listen port");
    public string TunnelTargetHostLabel => T("目标地址", "Target host");
    public string TunnelTargetPortLabel => T("目标端口", "Target port");
    public string TunnelInvalid => T(
        "请填写 SSH 服务器、用户名，以及 1–65535 范围内的端口。",
        "Fill in the SSH server, username, and ports in the 1–65535 range.");

    public string TunnelTypeName(string key) => key switch
    {
        TunnelTypes.Remote => TunnelTypeRemote,
        TunnelTypes.Dynamic => TunnelTypeDynamic,
        _ => TunnelTypeLocal,
    };

    public string TunnelStateName(TunnelState state) => state switch
    {
        TunnelState.Connecting => TunnelStateConnecting,
        TunnelState.Running => TunnelStateRunning,
        TunnelState.Failed => TunnelStateFailed,
        _ => TunnelStateStopped,
    };

    // 网络抓包页面
    public string PktSubtitle => T(
        "选择网卡并设置过滤表达式，实时查看经过本机的数据包，可导出为 pcap 文件。",
        "Pick an adapter and a filter expression to watch packets in real time, then export them as a pcap file.");
    public string PktAdapter => T("网卡", "Adapter");
    public string PktAdapterPlaceholder => T("选择网卡", "Select an adapter");
    public string PktRefresh => T("刷新网卡列表", "Refresh adapters");
    public string PktFilter => T("过滤器", "Filter");
    public string PktFilterPlaceholder => T("BPF 表达式，例如 tcp port 443", "BPF expression, e.g. tcp port 443");
    public string PktStart => T("开始", "Start");
    public string PktStop => T("停止", "Stop");
    public string PktClear => T("清空", "Clear");
    public string PktExport => T("导出 pcap", "Export pcap");
    public string PktStarting => T("正在开始捕获…", "Starting capture…");
    public string PktCapturing(string adapter) => T($"正在捕获：{adapter}", $"Capturing on {adapter}");
    public string PktStopped => T("已停止捕获。", "Capture stopped.");
    public string PktStatus(long count, long dropped) => T(
        $"已捕获 {count} 个数据包 · 丢弃 {dropped}",
        $"{count} packets captured · {dropped} dropped");
    public string PktEmpty => T("尚未捕获数据包。", "No packets captured yet.");
    public string PktNoAdapters => T("未找到可用的网卡。", "No adapters found.");
    public string PktNoSelection => T(
        "在列表中选择一个数据包查看十六进制内容。",
        "Select a packet to view its hex dump.");
    public string PktDriverMissingTitle => T("未检测到 Npcap", "Npcap not detected");
    public string PktDriverMissing => T(
        "抓包需要先安装 Npcap（安装时勾选“WinPcap API 兼容模式”），安装后请重启 MoxTerm。",
        "Packet capture requires Npcap. Install it (enable \"WinPcap API compatible mode\") and restart MoxTerm.");
    public string PktDownloadNpcap => T("下载 Npcap", "Download Npcap");
    public string PktStartFailedTitle => T("无法开始捕获", "Capture failed");
    public string PktAdminRequired => T(
        "需要管理员权限才能抓包，请以管理员身份运行 MoxTerm 后重试。",
        "Administrator rights are required for packet capture. Run MoxTerm as administrator and try again.");
    public string PktExportFailedTitle => T("导出失败", "Export failed");
    public string PktExportDone(string name) => T($"已导出到 {name}。", $"Exported to {name}.");
    public string PktHexTruncated(int bytes) => T(
        $"数据包过长，仅显示前 {bytes} 字节。",
        $"Packet is too long; showing the first {bytes} bytes.");
    public string PktColTime => T("时间", "Time");
    public string PktColSource => T("源", "Source");
    public string PktColDestination => T("目的", "Destination");
    public string PktColProtocol => T("协议", "Protocol");
    public string PktColLength => T("长度", "Length");
    public string PktColInfo => T("摘要", "Info");

    // 端口扫描页面
    public string PortScanSubtitle => T(
        "扫描目标主机开放的 TCP 端口，单个连接超时 800 毫秒，结果只列出开放端口。",
        "Scan a host for open TCP ports. Each connection times out after 800 ms; only open ports are listed.");
    public string PortScanHostLabel => T("目标主机", "Target host");
    public string PortScanHostPlaceholder => T("主机名或 IP", "Hostname or IP");
    public string PortScanPortsLabel => T("端口范围", "Ports");
    public string PortScanPortsPlaceholder => T("例如 1-1024 或 22,80,443", "e.g. 1-1024 or 22,80,443");
    public string PortScanPresetCommon => T("常用端口", "Common ports");
    public string PortScanPresetLow => T("1 – 1024", "1 – 1024");
    public string PortScanPresetAll => T("1 – 65535", "1 – 65535");
    public string PortScanStart => T("开始扫描", "Start scan");
    public string PortScanStop => T("停止", "Stop");
    public string PortScanClear => T("清空", "Clear");
    public string PortScanHostRequired => T("请填写要扫描的主机名或 IP。", "Enter a hostname or IP to scan.");
    public string PortScanPortsInvalid(string token) => T(
        $"端口表达式“{token}”无效，请使用 1-1024 或 22,80,443 这样的格式。",
        $"Invalid port expression \"{token}\". Use a format like 1-1024 or 22,80,443.");
    public string PortScanPortsEmpty => T("请填写端口或端口范围。", "Enter ports or a port range.");
    public string PortScanResolving(string host) => T($"正在解析 {host}…", $"Resolving {host}…");
    public string PortScanProgress(int scanned, int total, int open) => T(
        $"正在扫描 {scanned}/{total} · 开放 {open}",
        $"Scanning {scanned}/{total} · {open} open");
    public string PortScanDone(int open, int total, double seconds) => T(
        $"扫描完成：{total} 个端口中开放 {open} 个，用时 {seconds:F1} 秒。",
        $"Scan complete: {open} open of {total} ports in {seconds:F1} s.");
    public string PortScanNoOpen => T("扫描完成，没有发现开放的端口。", "Scan finished; no open ports found.");
    public string PortScanCancelled(int open) => T(
        $"扫描已停止，已发现 {open} 个开放端口。",
        $"Scan stopped; {open} open ports found.");
    public string PortScanErrorTitle => T("无法扫描", "Cannot scan");
    public string PortScanFailed(string message) => T($"扫描失败：{message}", $"Scan failed: {message}");
    public string PortScanEmpty => T(
        "尚未扫描。填写目标主机后点击“开始扫描”。",
        "No scan yet. Enter a host and click \"Start scan\".");
    public string PortScanColPort => T("端口", "Port");
    public string PortScanColService => T("服务", "Service");
    public string PortScanColBanner => T("标识", "Banner");

    // 网络诊断页面（Ping / Traceroute）
    public string DiagSubtitle => T(
        "检测目标主机的连通性与网络路径：连通性检测统计延迟与丢包，路由追踪逐跳显示路由。",
        "Check reachability and the network path: Ping reports latency and loss, Traceroute lists every hop.");
    public string DiagHostLabel => T("目标主机", "Target host");
    public string DiagHostPlaceholder => T("主机名或 IP", "Hostname or IP");
    public string DiagPing => T("连通性检测", "Ping");
    public string DiagTraceroute => T("路由追踪", "Traceroute");
    public string DiagStop => T("停止", "Stop");
    public string DiagClear => T("清空", "Clear");
    public string DiagHostRequired => T(
        "请填写要诊断的主机名或 IP。",
        "Enter a hostname or IP to diagnose.");
    public string DiagRunning(string mode, string host) => T(
        $"正在对 {host} 执行 {mode}…",
        $"Running {mode} against {host}…");
    public string DiagPingReply(string address, int bytes, long milliseconds, int ttl) => T(
        $"来自 {address} 的回复：字节={bytes} 时间={milliseconds}ms TTL={ttl}",
        $"Reply from {address}: bytes={bytes} time={milliseconds}ms TTL={ttl}");
    public string DiagTimedOut => T("请求超时。", "Request timed out.");
    public string DiagRequestFailed(string status) => T($"请求失败：{status}", $"Request failed: {status}");
    public string DiagPingSummary(int sent, int received, int lossPercent, long min, long average, long max) => T(
        $"已发送 {sent}，已接收 {received}，丢失 {lossPercent}%；最短 {min}ms，平均 {average}ms，最长 {max}ms。",
        $"Sent {sent}, received {received}, lost {lossPercent}%; min {min}ms, avg {average}ms, max {max}ms.");
    public string DiagPingNoReply => T(
        "全部请求均未收到回复，目标可能不可达。",
        "No replies received; the target may be unreachable.");
    public string DiagTracerouteHeader(string host) => T(
        $"正在跟踪到 {host} 的路由，最多 30 跳…",
        $"Tracing route to {host} over a maximum of 30 hops…");
    public string DiagFinished => T("诊断完成。", "Diagnosis finished.");
    public string DiagCancelled => T("诊断已停止。", "Diagnosis stopped.");
    public string DiagFailed(string message) => T($"诊断失败：{message}", $"Diagnosis failed: {message}");
    public string DiagFailedTitle => T("无法诊断", "Cannot diagnose");
    public string DiagEmpty => T(
        "尚未诊断。填写目标主机后点击“连通性检测”或“路由追踪”。",
        "No diagnosis yet. Enter a host and click Ping or Traceroute.");

    // 网络扫描页面
    public string NetScanSubtitle => T(
        "通过 ICMP 与 ARP 发现网段内的在线主机，解析主机名与 MAC，并检测常见服务是否开放。",
        "Discover live hosts with ICMP and ARP, resolve host names and MAC addresses, and detect common services.");
    public string NetScanRangeLabel => T("扫描范围", "Range");
    public string NetScanRangePlaceholder => T("例如 192.168.1.0/24", "e.g. 192.168.1.0/24");
    public string NetScanLocalSubnet => T("本机网段", "Local subnet");
    public string NetScanNoLocalSubnet => T("未找到可用的本机网段。", "No local subnet found.");
    public string NetScanStart => T("开始扫描", "Start scan");
    public string NetScanStop => T("停止", "Stop");
    public string NetScanClear => T("清空", "Clear");
    public string NetScanExport => T("导出 CSV", "Export CSV");
    public string NetScanRangeInvalid => T(
        "扫描范围无效，请填写 CIDR（192.168.1.0/24）或地址区间（192.168.1.1-254）。",
        "Invalid range. Use a CIDR (192.168.1.0/24) or an address range (192.168.1.1-254).");
    public string NetScanRangeTooLarge => T(
        "扫描范围过大，单次最多支持 65536 个地址。",
        "The range is too large; up to 65536 addresses per scan.");
    public string NetScanProgress(int scanned, int total, int alive) => T(
        $"正在扫描 {scanned}/{total} · 在线 {alive}",
        $"Scanning {scanned}/{total} · {alive} online");
    public string NetScanDone(int alive, int total, double seconds) => T(
        $"扫描完成：{total} 个地址中在线 {alive} 个，用时 {seconds:F1} 秒。",
        $"Scan complete: {alive} online of {total} addresses in {seconds:F1} s.");
    public string NetScanNoHosts => T("扫描完成，没有发现在线主机。", "Scan finished; no live hosts found.");
    public string NetScanCancelled(int alive) => T(
        $"扫描已停止，已发现在线主机 {alive} 个。",
        $"Scan stopped; {alive} live hosts found.");
    public string NetScanErrorTitle => T("无法扫描", "Cannot scan");
    public string NetScanFailed(string message) => T($"扫描失败：{message}", $"Scan failed: {message}");
    public string NetScanEmpty => T(
        "尚未扫描。填写网段后点击“开始扫描”。",
        "No scan yet. Enter a range and click \"Start scan\".");
    public string NetScanColAddress => T("IP 地址", "IP address");
    public string NetScanColHostName => T("主机名", "Host name");
    public string NetScanColMac => T("MAC 地址", "MAC address");
    public string NetScanColLatency => T("延迟", "Latency");
    public string NetScanExportTitle => T("无法导出", "Cannot export");
    public string NetScanExportDone(string name) => T($"已导出到 {name}。", $"Exported to {name}.");

    // 开放端口页面
    public string OpenPortsSubtitle => T(
        "列出本机正在监听的端口与活动连接，并关联所属进程与 PID。",
        "List listening ports and active connections on this machine, with their processes and PIDs.");
    public string OpenPortsFilterPlaceholder => T(
        "按端口、地址、进程名或 PID 过滤",
        "Filter by port, address, process, or PID");
    public string OpenPortsRefresh => T("刷新", "Refresh");
    public string OpenPortsEmpty => T("没有读取到端口信息。", "No port information available.");
    public string OpenPortsNoMatch => T("没有匹配的条目。", "No matching entries.");
    public string OpenPortsStatus(int total, int tcp, int udp) => T(
        $"共 {total} 个条目 · TCP {tcp} · UDP {udp}",
        $"{total} entries · TCP {tcp} · UDP {udp}");
    public string OpenPortsErrorTitle => T("无法读取端口列表", "Cannot read the port list");
    public string OpenPortsFailed(string message) => T(
        $"读取端口列表失败：{message}",
        $"Failed to read the port list: {message}");
    public string OpenPortsColProtocol => T("协议", "Protocol");
    public string OpenPortsColLocal => T("本地地址", "Local address");
    public string OpenPortsColRemote => T("远端地址", "Remote address");
    public string OpenPortsColState => T("状态", "State");
    public string OpenPortsColPid => T("PID", "PID");
    public string OpenPortsColProcess => T("进程", "Process");

    // 网络服务页面
    public string NetSvcSubtitle => T(
        "在本地启动只读的 HTTP / FTP / TFTP 文件服务，便于在局域网内浏览与分发文件。",
        "Start read-only HTTP / FTP / TFTP file services locally to browse and share files on the LAN.");
    public string NetSvcFolderLabel => T("共享目录", "Shared folder");
    public string NetSvcFolderPlaceholder => T("选择要共享的文件夹", "Choose a folder to share");
    public string NetSvcBrowse => T("浏览", "Browse");
    public string NetSvcHttpName => T("HTTP 文件服务", "HTTP file service");
    public string NetSvcHttpDesc => T("浏览器打开即可浏览目录、下载文件", "Browse directories and download files in a browser");
    public string NetSvcFtpName => T("FTP 文件服务", "FTP file service");
    public string NetSvcFtpDesc => T("FTP 客户端匿名只读访问", "Anonymous read-only access from FTP clients");
    public string NetSvcTftpName => T("TFTP 文件服务", "TFTP file service");
    public string NetSvcTftpDesc => T("TFTP 客户端读取文件（UDP）", "Read files from TFTP clients (UDP)");
    public string NetSvcPortLabel => T("端口", "Port");
    public string NetSvcStart => T("启动", "Start");
    public string NetSvcStop => T("停止", "Stop");
    public string NetSvcStopped => T("已停止", "Stopped");
    public string NetSvcRunning(string url) => T($"运行中 · {url}", $"Running · {url}");
    public string NetSvcErrorTitle => T("无法启动服务", "Cannot start the service");
    public string NetSvcFolderInvalid => T("请选择一个存在的共享目录。", "Choose a shared folder that exists.");
    public string NetSvcPortInvalid => T("端口必须是 1–65535 之间的整数。", "The port must be an integer between 1 and 65535.");
    public string NetSvcStartFailed(string protocol, string message) => T(
        $"启动 {protocol} 服务失败：{message}",
        $"Failed to start the {protocol} service: {message}");

    // Wake On Lan 页面
    public string WolSubtitle => T(
        "向目标设备的 MAC 地址发送魔术包，把它从关机或休眠状态唤醒。",
        "Send a magic packet to a device's MAC address to wake it from sleep or power off.");
    public string WolWake => T("唤醒", "Wake");
    public string WolNew => T("新建设备", "New device");
    public string WolEdit => T("编辑", "Edit");
    public string WolDelete => T("删除", "Delete");
    public string WolEmpty => T(
        "还没有设备。点击“新建设备”添加要唤醒的计算机。",
        "No devices yet. Click \"New device\" to add a computer to wake.");
    public string WolDialogNew => T("新建设备", "New device");
    public string WolDialogEdit => T("编辑设备", "Edit device");
    public string WolNameLabel => T("名称", "Name");
    public string WolNamePlaceholder => T("可选，默认使用 MAC 地址", "Optional, defaults to the MAC address");
    public string WolMacLabel => T("MAC 地址", "MAC address");
    public string WolMacPlaceholder => T("例如 AA:BB:CC:DD:EE:FF", "e.g. AA:BB:CC:DD:EE:FF");
    public string WolBroadcastLabel => T("广播地址", "Broadcast address");
    public string WolPortLabel => T("端口", "Port");
    public string WolInvalid => T(
        "请填写有效的 MAC 地址、IPv4 广播地址，以及 1–65535 范围内的端口。",
        "Enter a valid MAC address, IPv4 broadcast address, and a port in the 1–65535 range.");
    public string WolDeleteConfirm(string name) => T(
        $"确定删除设备“{name}”吗？",
        $"Delete device \"{name}\"?");
    public string WolSending(string name) => T($"正在唤醒 {name}…", $"Waking {name}…");
    public string WolSent(string name) => T($"已向 {name} 发送唤醒包。", $"Wake-up packet sent to {name}.");
    public string WolSendFailed(string message) => T(
        $"发送唤醒包失败：{message}",
        $"Failed to send the wake-up packet: {message}");

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
