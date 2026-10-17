namespace MoxTerm.Services;

/// <summary>终端后端类型。</summary>
public enum TerminalBackendKind
{
    /// <summary>重定向标准输入输出，兼容性最好。</summary>
    Pipe,

    /// <summary>Windows 伪控制台，支持 VT 序列与窗口尺寸协商。</summary>
    ConPty,
}

/// <summary>
/// 一种终端后端：决定会话如何驱动 Shell 进程，以及终端能提供多少能力。
/// </summary>
/// <param name="Kind">后端类型。</param>
/// <param name="Name">显示名称。</param>
/// <param name="Description">给用户看的能力说明。</param>
public sealed record TerminalBackend(TerminalBackendKind Kind, string Name, string Description)
{
    /// <summary>
    /// 管道后端：默认选项。任何能读写管道的 Shell 都能跑，输出为纯文本，
    /// 但无法协商窗口尺寸，也不解析 ANSI 控制序列。
    /// </summary>
    public static TerminalBackend Pipe { get; } = new(
        TerminalBackendKind.Pipe,
        "管道（兼容模式）",
        "重定向标准输入输出，兼容性最好；输出为纯文本，不支持窗口尺寸协商与 ANSI 颜色。");

    /// <summary>
    /// ConPTY 后端：能力更完整，但依赖系统允许子进程挂载到伪控制台。
    /// </summary>
    public static TerminalBackend ConPty { get; } = new(
        TerminalBackendKind.ConPty,
        "伪控制台（ConPTY）",
        "使用 Windows 伪控制台，支持 VT 序列与窗口尺寸协商；部分受限环境下子进程可能无法挂载。");

    /// <summary>全部后端。每次访问都重建，让名称与说明跟随当前界面语言。</summary>
    public static IReadOnlyList<TerminalBackend> All =>
    [
        Pipe with { Name = Loc.Instance.BackendPipe, Description = Loc.Instance.BackendPipeDesc },
        ConPty with { Name = Loc.Instance.BackendConPty, Description = Loc.Instance.BackendConPtyDesc },
    ];

    /// <summary>按持久化的枚举名还原后端，无法识别时回退到管道后端。</summary>
    public static TerminalBackend FromName(string? name) =>
        All.FirstOrDefault(backend => backend.Kind.ToString() == name) ?? Pipe;

    /// <summary>创建该后端对应的终端会话。</summary>
    public ITerminalSession CreateSession() => Kind switch
    {
        TerminalBackendKind.ConPty => new ConPtyTerminalSession(),
        _ => new PipeTerminalSession(),
    };
}
