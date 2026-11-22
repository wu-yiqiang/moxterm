namespace MoxTerm.Services;

/// <summary>隧道类型标识（持久化用的稳定字符串）。</summary>
public static class TunnelTypes
{
    /// <summary>本地转发：本地监听端口 → 远端目标。</summary>
    public const string Local = "local";

    /// <summary>远程转发：远端监听端口 → 本地目标。</summary>
    public const string Remote = "remote";

    /// <summary>动态转发：本地 SOCKS5 代理。</summary>
    public const string Dynamic = "dynamic";

    public static IReadOnlyList<string> All { get; } = [Local, Remote, Dynamic];

    public static int IndexOf(string? type)
    {
        for (var index = 0; index < All.Count; index++)
        {
            if (All[index] == type)
            {
                return index;
            }
        }

        return 0;
    }
}

/// <summary>
/// 一条 SSH 隧道（端口转发）配置，随设置一起持久化。
/// </summary>
public sealed class TunnelProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>显示名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>隧道类型，取值见 <see cref="TunnelTypes"/>。</summary>
    public string Type { get; set; } = TunnelTypes.Local;

    /// <summary>SSH 服务器地址。</summary>
    public string SshHost { get; set; } = "";

    public int SshPort { get; set; } = 22;

    public string Username { get; set; } = "";

    /// <summary>SSH 密码，经 <see cref="SecretProtector"/> 加密后的存储形态。</summary>
    public string Password { get; set; } = "";

    /// <summary>监听地址；远程转发时是服务器侧的监听地址。</summary>
    public string BindHost { get; set; } = "127.0.0.1";

    public int BindPort { get; set; }

    /// <summary>转发目标地址（动态转发不使用）。</summary>
    public string TargetHost { get; set; } = "127.0.0.1";

    public int TargetPort { get; set; }

    /// <summary>复制一份，供编辑对话框在确认前不污染原配置。</summary>
    public TunnelProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Type = Type,
        SshHost = SshHost,
        SshPort = SshPort,
        Username = Username,
        Password = Password,
        BindHost = BindHost,
        BindPort = BindPort,
        TargetHost = TargetHost,
        TargetPort = TargetPort,
    };
}
