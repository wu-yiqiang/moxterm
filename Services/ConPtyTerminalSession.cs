using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MoxTerm.Services;

/// <summary>
/// 基于 Windows 伪控制台（ConPTY）的终端会话实现。
/// 通过一对匿名管道与 <c>CreatePseudoConsole</c> 通信，子进程以普通控制台程序方式运行。
/// </summary>
public sealed class ConPtyTerminalSession : ITerminalSession
{
    private IntPtr _pseudoConsole = IntPtr.Zero;
    private IntPtr _processHandle = IntPtr.Zero;
    private IntPtr _threadHandle = IntPtr.Zero;
    private FileStream? _input;
    private FileStream? _output;
    private Thread? _readerThread;
    private Task? _exitTask;
    private volatile bool _running;
    private bool _disposed;

    public event EventHandler<string>? OutputReceived;

    public event EventHandler<int>? Exited;

    public bool IsRunning => _running;

    public int ProcessId { get; private set; }

    public void Start(ShellProfile profile, int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(profile);

        if (_running)
        {
            throw new InvalidOperationException("该会话已经启动。");
        }

        columns = Math.Clamp(columns, 1, 1000);
        rows = Math.Clamp(rows, 1, 1000);

        var size = new NativeMethods.COORD((short)columns, (short)rows);
        var inheritable = new NativeMethods.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
            lpSecurityDescriptor = IntPtr.Zero,
            bInheritHandle = 1,
        };

        // consoleInput  -> ConPTY 读，clientInput  -> 我们写
        if (!NativeMethods.CreatePipe(out var consoleInput, out var clientInput, ref inheritable, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建 ConPTY 输入管道失败。");
        }

        // clientOutput -> 我们读，consoleOutput -> ConPTY 写
        if (!NativeMethods.CreatePipe(out var clientOutput, out var consoleOutput, ref inheritable, 0))
        {
            CloseHandles(consoleInput, clientInput);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建 ConPTY 输出管道失败。");
        }

        var pseudoConsole = IntPtr.Zero;
        var attributeList = IntPtr.Zero;

        try
        {
            var hr = NativeMethods.CreatePseudoConsole(size, consoleInput, consoleOutput, 0, out pseudoConsole);
            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            // ConPTY 已持有自己的句柄副本，父进程侧这两端可以立即关闭
            NativeMethods.CloseHandle(consoleInput);
            NativeMethods.CloseHandle(consoleOutput);
            consoleInput = IntPtr.Zero;
            consoleOutput = IntPtr.Zero;

            var startupInfo = new NativeMethods.STARTUPINFOEX();
            startupInfo.StartupInfo.cb = Marshal.SizeOf<NativeMethods.STARTUPINFOEX>();

            // 首次调用只用于查询所需的缓冲区大小，返回 false 属预期行为
            var required = IntPtr.Zero;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref required);
            if (required == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法计算进程属性列表大小。");
            }

            attributeList = Marshal.AllocHGlobal(required);
            if (!NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref required))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "初始化进程属性列表失败。");
            }

            if (!NativeMethods.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    new IntPtr(NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE),
                    pseudoConsole,
                    new IntPtr(IntPtr.Size),
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "把伪控制台绑定到子进程失败。");
            }

            startupInfo.lpAttributeList = attributeList;

            if (!NativeMethods.CreateProcess(
                    null,
                    BuildCommandLine(profile),
                    IntPtr.Zero,
                    IntPtr.Zero,
                    bInheritHandles: true,
                    NativeMethods.EXTENDED_STARTUPINFO_PRESENT,
                    IntPtr.Zero,
                    profile.WorkingDirectory ?? Environment.CurrentDirectory,
                    ref startupInfo,
                    out var processInformation))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"启动 {profile.Name} 失败。");
            }

            _processHandle = processInformation.hProcess;
            _threadHandle = processInformation.hThread;
            ProcessId = processInformation.dwProcessId;

            // 所有权转移给实例字段，由 Dispose 负责关闭
            _pseudoConsole = pseudoConsole;
            pseudoConsole = IntPtr.Zero;
        }
        finally
        {
            CloseHandles(consoleInput, consoleOutput);

            if (attributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (pseudoConsole != IntPtr.Zero)
            {
                // 中途失败：连同管道一起回收，避免句柄泄漏
                NativeMethods.ClosePseudoConsole(pseudoConsole);
                CloseHandles(clientInput, clientOutput);
            }
        }

        _input = new FileStream(new SafeFileHandle(clientInput, ownsHandle: true), FileAccess.Write, bufferSize: 1);
        _output = new FileStream(new SafeFileHandle(clientOutput, ownsHandle: true), FileAccess.Read, bufferSize: 1);

        _running = true;
        _readerThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "MoxTerm.ConPTY.Reader",
        };
        _readerThread.Start();
        _exitTask = Task.Run(WaitForExit);
    }

    public void Write(string text)
    {
        if (!_running || string.IsNullOrEmpty(text) || _input is null)
        {
            return;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            _input.Write(bytes, 0, bytes.Length);
            _input.Flush();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Shell 已退出，输入被丢弃即可
        }
    }

    public void Resize(int columns, int rows)
    {
        if (_pseudoConsole == IntPtr.Zero)
        {
            return;
        }

        columns = Math.Clamp(columns, 1, 1000);
        rows = Math.Clamp(rows, 1, 1000);

        NativeMethods.ResizePseudoConsole(_pseudoConsole, new NativeMethods.COORD((short)columns, (short)rows));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _running = false;

        // 关闭伪控制台会让 Shell 进程终止、输出管道断开，读取线程随之退出
        if (_pseudoConsole != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(_pseudoConsole);
            _pseudoConsole = IntPtr.Zero;
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

    private static string BuildCommandLine(ShellProfile profile) =>
        string.IsNullOrWhiteSpace(profile.Arguments)
            ? $"\"{profile.Executable}\""
            : $"\"{profile.Executable}\" {profile.Arguments}";

    private static void CloseHandles(params IntPtr[] handles)
    {
        foreach (var handle in handles)
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(handle);
            }
        }
    }

    /// <summary>
    /// 读取管道并解码成文本。ConPTY 固定输出 UTF-8，
    /// 这里用 <see cref="Decoder"/> 保持跨块的多字节字符不被打断。
    /// </summary>
    private void ReadLoop()
    {
        var output = _output;
        if (output is null)
        {
            return;
        }

        var buffer = new byte[8192];
        var characters = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
        var decoder = Encoding.UTF8.GetDecoder();

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
