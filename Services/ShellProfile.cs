namespace MoxTerm.Services;

/// <summary>会话的承载方式。</summary>
public enum SessionTransport
{
    /// <summary>本地进程：经 ConPTY 或管道承载 <c>Executable</c>。</summary>
    Process,

    /// <summary>Telnet：直连 TCP 端口。</summary>
    Telnet,

    /// <summary>串口：读写本地 COM 口。</summary>
    Serial,
}

/// <summary>
/// 一个可启动的会话配置。多数情况下是本地 Shell（<paramref name="Executable"/> +
/// <paramref name="Arguments"/> 由 ConPTY 承载），也可以是 Telnet（<paramref name="Host"/>
/// + <paramref name="Port"/>）或串口（<paramref name="SerialPortName"/> + <paramref name="BaudRate"/>）。
/// </summary>
/// <param name="Name">显示名称。</param>
/// <param name="Executable">可执行文件名或绝对路径；非进程会话留空。</param>
/// <param name="Arguments">命令行参数。</param>
/// <param name="WorkingDirectory">工作目录，<c>null</c> 表示沿用当前目录。</param>
/// <param name="Password">登录密码；非空时终端检测到密码提示会自动回填。</param>
/// <param name="Username">登录用户名；供 FTP / Telnet 这类由对端询问用户名的协议自动回填。</param>
/// <param name="Transport">承载方式。</param>
/// <param name="Host">Telnet 主机名或 IP。</param>
/// <param name="Port">Telnet 端口；0 表示使用协议默认端口。</param>
/// <param name="SerialPortName">串口名，如 <c>COM3</c>。</param>
/// <param name="BaudRate">串口波特率。</param>
public sealed record ShellProfile(
    string Name,
    string Executable,
    string Arguments = "",
    string? WorkingDirectory = null,
    string? Password = null,
    string? Username = null,
    SessionTransport Transport = SessionTransport.Process,
    string? Host = null,
    int Port = 0,
    string? SerialPortName = null,
    int BaudRate = 115200)
{
    public static ShellProfile CommandPrompt { get; } = new("命令提示符", "cmd.exe");

    public static ShellProfile WindowsPowerShell { get; } = new("Windows PowerShell", "powershell.exe", "-NoLogo");

    public static ShellProfile PowerShell7 { get; } = new("PowerShell 7", "pwsh.exe", "-NoLogo");

    public static ShellProfile Wsl { get; } = new("WSL", "wsl.exe");

    /// <summary>探测本机可用的 Shell，按偏好排序。至少返回一项。</summary>
    public static IReadOnlyList<ShellProfile> Discover()
    {
        ShellProfile[] candidates = [PowerShell7, WindowsPowerShell, CommandPrompt, Wsl];

        var available = new List<ShellProfile>();
        foreach (var candidate in candidates)
        {
            if (Exists(candidate.Executable))
            {
                available.Add(candidate);
            }
        }

        // 极端情况下（PATH 异常）也保证有一个可用项
        return available.Count > 0 ? available : [CommandPrompt];
    }

    private static bool Exists(string executable)
    {
        if (Path.IsPathRooted(executable))
        {
            return File.Exists(executable);
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(directory, executable)))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // PATH 里混入了非法字符的目录项，跳过即可
            }
        }

        return false;
    }
}
