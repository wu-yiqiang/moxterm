namespace MoxTerm.Services;

/// <summary>
/// 一个终端会话（本地进程 / Telnet / 串口）。
/// </summary>
public interface ITerminalSession : IDisposable
{
    /// <summary>收到 Shell 输出。可能在后台线程触发，订阅方负责切回 UI 线程。</summary>
    event EventHandler<string>? OutputReceived;

    /// <summary>Shell 进程退出。</summary>
    event EventHandler<int>? Exited;

    /// <summary>按指定终端尺寸启动 Shell。</summary>
    void Start(ShellProfile profile, int columns, int rows);

    /// <summary>把用户输入写入 Shell。</summary>
    void Write(string text);

    /// <summary>终端窗口尺寸变化时通知 Shell。</summary>
    void Resize(int columns, int rows);
}
