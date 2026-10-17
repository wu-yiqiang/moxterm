using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MoxTerm.Services;

/// <summary>
/// 基于重定向标准输入输出的终端会话。
/// <para>
/// 子进程不带控制台，stdin / stdout / stderr 全部接到匿名管道上，stderr 合并进 stdout
/// 以便在终端里按发生顺序显示。相比 <see cref="ConPtyTerminalSession"/> 少了 VT 序列与
/// 窗口尺寸协商，但兼容性最好：任何能读写管道的 Shell 都能跑，输出也是干净的纯文本。
/// </para>
/// <para>
/// Shell 在管道模式下仍会回显用户输入，所以不需要在界面上做本地回显。
/// 输出编码跟随系统 OEM 代码页（中文系统为 936），而不是 UTF-8。
/// </para>
/// </summary>
public sealed class PipeTerminalSession : ITerminalSession
{
    private const uint STARTF_USESTDHANDLES = 0x00000100;

    /// <summary>OEM 代码页只解析一次；<see cref="CodePagesEncodingProvider"/> 在 .NET 10 中已内置。</summary>
    private static readonly Lazy<Encoding> ConsoleEncoding = new(ResolveEncoding, LazyThreadSafetyMode.ExecutionAndPublication);

    private FileStream? _input;
    private FileStream? _output;
    private IntPtr _processHandle = IntPtr.Zero;
    private IntPtr _threadHandle = IntPtr.Zero;
    private Thread? _readerThread;
    private Task? _exitTask;
    private Encoding _encoding = Encoding.UTF8;
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

        _encoding = ConsoleEncoding.Value;

        var inheritable = new NativeMethods.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
            lpSecurityDescriptor = IntPtr.Zero,
            bInheritHandle = 1,
        };

        if (!NativeMethods.CreatePipe(out var stdinRead, out var stdinWrite, ref inheritable, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建标准输入管道失败。");
        }

        if (!NativeMethods.CreatePipe(out var stdoutRead, out var stdoutWrite, ref inheritable, 0))
        {
            CloseHandles(stdinRead, stdinWrite);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建标准输出管道失败。");
        }

        var startupInfo = new NativeMethods.STARTUPINFOEX();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<NativeMethods.STARTUPINFOEX>();
        startupInfo.StartupInfo.dwFlags = (int)STARTF_USESTDHANDLES;
        startupInfo.StartupInfo.hStdInput = stdinRead;
        startupInfo.StartupInfo.hStdOutput = stdoutWrite;
        startupInfo.StartupInfo.hStdError = stdoutWrite;

        var created = false;

        try
        {
            created = NativeMethods.CreateProcess(
                null,
                BuildCommandLine(profile),
                IntPtr.Zero,
                IntPtr.Zero,
                bInheritHandles: true,
                dwCreationFlags: NativeMethods.CREATE_NO_WINDOW,
                IntPtr.Zero,
                profile.WorkingDirectory ?? Environment.CurrentDirectory,
                ref startupInfo,
                out var processInformation);

            if (!created)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"启动 {profile.Name} 失败。");
            }

            _processHandle = processInformation.hProcess;
            _threadHandle = processInformation.hThread;
            ProcessId = processInformation.dwProcessId;
        }
        finally
        {
            // 子进程侧的两端已被子进程持有，父进程立刻关掉
            CloseHandles(stdinRead, stdoutWrite);

            if (!created)
            {
                // 启动失败：父进程侧的另外两端也要回收
                CloseHandles(stdinWrite, stdoutRead);
            }
        }

        _input = new FileStream(new SafeFileHandle(stdinWrite, ownsHandle: true), FileAccess.Write, bufferSize: 1);
        _output = new FileStream(new SafeFileHandle(stdoutRead, ownsHandle: true), FileAccess.Read, bufferSize: 1);

        _running = true;
        _readerThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "MoxTerm.Pipe.Reader",
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
            var bytes = _encoding.GetBytes(text);
            _input.Write(bytes, 0, bytes.Length);
            _input.Flush();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Shell 已退出，输入丢弃即可
        }
    }

    /// <summary>管道没有尺寸概念，无法把行列数告知 Shell；界面侧仍会自行重排。</summary>
    public void Resize(int columns, int rows)
    {
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _running = false;

        // 关闭输入管道会让 Shell 读到 EOF 并退出
        _input?.Dispose();
        _input = null;

        _readerThread?.Join(TimeSpan.FromSeconds(2));
        _exitTask?.Wait(TimeSpan.FromSeconds(2));

        // Shell 没有因为 EOF 退出时兜底结束它，避免残留进程
        if (_processHandle != IntPtr.Zero)
        {
            NativeMethods.TerminateProcess(_processHandle, 0);
        }

        _output?.Dispose();
        _output = null;

        CloseHandles(_processHandle, _threadHandle);
        _processHandle = IntPtr.Zero;
        _threadHandle = IntPtr.Zero;
    }

    private static Encoding ResolveEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            return Encoding.GetEncoding(
                (int)NativeMethods.GetOEMCP(),
                EncoderFallback.ReplacementFallback,
                DecoderFallback.ReplacementFallback);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            System.Diagnostics.Debug.WriteLine($"[MoxTerm] 无法解析 OEM 代码页，回退到 UTF-8: {ex.Message}");
            return Encoding.UTF8;
        }
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

    private void ReadLoop()
    {
        var output = _output;
        if (output is null)
        {
            return;
        }

        var buffer = new byte[8192];
        var characters = new char[_encoding.GetMaxCharCount(buffer.Length)];
        var decoder = _encoding.GetDecoder();

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
