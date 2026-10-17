namespace MoxTerm.Services;

/// <summary>
/// 一个可启动的 Shell 配置。
/// </summary>
/// <param name="Name">显示名称。</param>
/// <param name="Executable">可执行文件名或绝对路径。</param>
/// <param name="Arguments">命令行参数。</param>
/// <param name="WorkingDirectory">工作目录，<c>null</c> 表示沿用当前目录。</param>
public sealed record ShellProfile(string Name, string Executable, string Arguments = "", string? WorkingDirectory = null)
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
