namespace MoxTerm.Services;

/// <summary>新建连接对话框里按协议显示的字段。</summary>
public enum SessionField
{
    Host,
    Port,
    Username,
    Password,
    SerialPort,
    BaudRate,
}

/// <summary>
/// 一种可选的连接协议。<paramref name="Supported"/> 为 <c>false</c> 时仍出现在列表里，
/// 但选中后禁用“连接”并提示尚未提供，避免用户点了没有反馈。
/// </summary>
/// <param name="Key">持久化用的稳定标识（小写）。</param>
/// <param name="Display">界面显示名。多数是产品名，不做翻译。</param>
/// <param name="Supported">本版本是否已实现。</param>
/// <param name="DefaultPort">默认端口，0 表示不使用端口。</param>
/// <param name="Fields">该协议需要填写的字段。</param>
public sealed record SessionProtocol(
    string Key,
    string Display,
    bool Supported,
    int DefaultPort,
    IReadOnlyList<SessionField> Fields)
{
    public const string Ssh = "ssh";
    public const string Telnet = "telnet";
    public const string Sftp = "sftp";
    public const string Ftp = "ftp";
    public const string Serial = "serial";
    public const string Shell = "shell";
    public const string Wsl = "wsl";

    private static readonly SessionField[] HostPortUserPassword =
        [SessionField.Host, SessionField.Port, SessionField.Username, SessionField.Password];

    /// <summary>
    /// 协议清单，顺序与 MobaXterm 的会话类型一致。
    /// 已实现：SSH / Telnet / SFTP / FTP（复用系统客户端）与串口；Shell、WSL 走本地会话。
    /// </summary>
    public static IReadOnlyList<SessionProtocol> All { get; } =
    [
        new(Ssh, "SSH", true, 22, HostPortUserPassword),
        new(Telnet, "Telnet", true, 23, HostPortUserPassword),
        new("rsh", "Rsh", false, 514, []),
        new("xdmcp", "Xdmcp", false, 177, []),
        new("rdp", "RDP", false, 3389, []),
        new("vnc", "VNC", false, 5900, []),
        // ftp.exe 不支持指定端口，固定走 21，因此不提供端口字段
        new(Ftp, "FTP", true, 21, [SessionField.Host, SessionField.Username, SessionField.Password]),
        new(Sftp, "SFTP", true, 22, HostPortUserPassword),
        new(Serial, "Serial", true, 0, [SessionField.SerialPort, SessionField.BaudRate]),
        new("file", "File", false, 0, []),
        new(Shell, "Shell", true, 0, []),
        new("browser", "Browser", false, 0, []),
        new("mosh", "Mosh", false, 60001, []),
        new("awss3", "Aws S3", false, 0, []),
        new(Wsl, "WSL", true, 0, []),
    ];

    /// <summary>默认选中的协议。</summary>
    public static SessionProtocol Default => All[0];

    /// <summary>按标识还原协议；为空或无法识别时回退到默认协议（旧版本数据只存过 SSH）。</summary>
    public static SessionProtocol FromKey(string? key) =>
        All.FirstOrDefault(protocol => protocol.Key == key) ?? Default;
}
