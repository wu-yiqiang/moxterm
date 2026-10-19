using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MoxTerm.Services;

/// <summary>
/// 本地进程会话的基类：<see cref="ConPtyTerminalSession"/> 与 <see cref="PipeTerminalSession"/>
/// 共享进程启动、输入写入、输出读取、退出监听与资源回收，派生类只负责
/// 把子进程接到自己的承载方式上（伪控制台或重定向管道）。
/// </summary>
public abstract class ProcessTerminalSession : ITerminalSession
{
    private FileStream? _input;
    private FileStream? _output;
    private IntPtr _processHandle;
    private IntPtr _threadHandle;
    private Thread? _readerThread;
    private Task? _exitTask;
    private volatile bool _running;
    private bool _disposed;

    /// <summary>会话的输入输出编码。ConPTY 固定 UTF-8；管道后端在 Start 时改为 OEM 代码页。</summary>
    protected Encoding SessionEncoding { get; set; } = Encoding.UTF8;

    public event EventHandler<string>? OutputReceived;

    public event EventHandler<int>? Exited;

    public abstract void Start(ShellProfile profile, int columns, int rows);

    /// <summary>管道没有尺寸概念，无法把行列数告知 Shell；只有 ConPTY 后端需要重写。</summary>
    public virtual void Resize(int columns, int rows)
    {
    }

    public void Write(string text)
    {
        if (!_running || string.IsNullOrEmpty(text) || _input is null)
        {
            return;
        }

        try
        {
            var bytes = SessionEncoding.GetBytes(text);
            _input.Write(bytes, 0, bytes.Length);
            _input.Flush();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Shell 已退出，输入被丢弃即可
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _running = false;

        // 先做必须先于终止的清理（ConPTY 关伪控制台 / 管道关输入让 Shell 读到 EOF）
        BeforeTerminate();

        // 补一刀立即终止：不等 Shell 自行清理，确保下面的等待毫秒级返回，窗口关闭不被拖慢
        if (_processHandle != IntPtr.Zero)
        {
            NativeMethods.TerminateProcess(_processHandle, 0);
        }

        _readerThread?.Join(TimeSpan.FromSeconds(2));
        _exitTask?.Wait(TimeSpan.FromSeconds(2));

        _input?.Dispose();
        _output?.Dispose();
        _input = null;
        _output = null;

        CloseHandles(_processHandle, _threadHandle);
        _processHandle = IntPtr.Zero;
        _threadHandle = IntPtr.Zero;
    }

    /// <summary>终止子进程前的准备，由派生类按承载方式实现。</summary>
    protected abstract void BeforeTerminate();

    /// <summary>启动前的公共校验：未释放、参数非空、且尚未启动过。</summary>
    protected void ValidateStart(ShellProfile profile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(profile);

        if (_running)
        {
            throw new InvalidOperationException("该会话已经启动。");
        }
    }

    /// <summary>创建一对可继承的匿名管道，失败时抛出带说明的 Win32 异常。</summary>
    protected static (IntPtr Read, IntPtr Write) CreatePipePair(string failureMessage)
    {
        var inheritable = new NativeMethods.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
            lpSecurityDescriptor = IntPtr.Zero,
            bInheritHandle = 1,
        };

        if (!NativeMethods.CreatePipe(out var read, out var write, ref inheritable, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), failureMessage);
        }

        return (read, write);
    }

    /// <summary>启动子进程并接管其进程与线程句柄；失败时抛出异常，由调用方回收自己的管道端。</summary>
    private protected void SpawnProcess(
        ShellProfile profile,
        uint creationFlags,
        ref NativeMethods.STARTUPINFOEX startupInfo)
    {
        if (!NativeMethods.CreateProcess(
                null,
                BuildCommandLine(profile),
                IntPtr.Zero,
                IntPtr.Zero,
                bInheritHandles: true,
                creationFlags,
                IntPtr.Zero,
                profile.WorkingDirectory ?? Environment.CurrentDirectory,
                ref startupInfo,
                out var processInformation))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"启动 {profile.Name} 失败。");
        }

        _processHandle = processInformation.hProcess;
        _threadHandle = processInformation.hThread;
    }

    /// <summary>把管道两端包成 FileStream 并启动读取线程与退出监听，Start 成功路径的最后一步。</summary>
    protected void RunSession(IntPtr inputPipe, IntPtr outputPipe, string readerThreadName)
    {
        _input = new FileStream(new SafeFileHandle(inputPipe, ownsHandle: true), FileAccess.Write, bufferSize: 1);
        _output = new FileStream(new SafeFileHandle(outputPipe, ownsHandle: true), FileAccess.Read, bufferSize: 1);

        _running = true;
        _readerThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = readerThreadName,
        };
        _readerThread.Start();
        _exitTask = Task.Run(WaitForExit);
    }

    /// <summary>关闭输入管道（管道后端终止进程前调用，让 Shell 读到 EOF）。</summary>
    protected void CloseInput()
    {
        _input?.Dispose();
        _input = null;
    }

    protected static void CloseHandles(params IntPtr[] handles)
    {
        foreach (var handle in handles)
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(handle);
            }
        }
    }

    protected static string BuildCommandLine(ShellProfile profile) =>
        string.IsNullOrWhiteSpace(profile.Arguments)
            ? $"\"{profile.Executable}\""
            : $"\"{profile.Executable}\" {profile.Arguments}";

    /// <summary>
    /// 读取管道并解码成文本，用 <see cref="Decoder"/> 保持跨块的多字节字符不被打断。
    /// </summary>
    private void ReadLoop()
    {
        var output = _output;
        if (output is null)
        {
            return;
        }

        var buffer = new byte[8192];
        var characters = new char[SessionEncoding.GetMaxCharCount(buffer.Length)];
        var decoder = SessionEncoding.GetDecoder();

        try
        {
            while (true)
            {
                var read = output.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                var count = decoder.GetChars(buffer, 0, read, characters, 0, flush: false);
                if (count > 0)
                {
                    OutputReceived?.Invoke(this, new string(characters, 0, count));
                }
            }

            // 冲掉末尾不完整的多字节序列
            var tail = decoder.GetChars(Array.Empty<byte>(), 0, 0, characters, 0, flush: true);
            if (tail > 0)
            {
                OutputReceived?.Invoke(this, new string(characters, 0, tail));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or UnauthorizedAccessException)
        {
            // 会话被释放或 Shell 退出，读取循环正常结束
        }
    }

    private void WaitForExit()
    {
        var handle = _processHandle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.WaitForSingleObject(handle, NativeMethods.INFINITE);

        var exitCode = 0;
        if (NativeMethods.GetExitCodeProcess(handle, out var code))
        {
            exitCode = unchecked((int)code);
        }

        _running = false;
        Exited?.Invoke(this, exitCode);
    }
}
