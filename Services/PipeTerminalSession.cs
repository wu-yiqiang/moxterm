using System.Runtime.InteropServices;
using System.Text;

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
public sealed class PipeTerminalSession : ProcessTerminalSession
{
    private const uint STARTF_USESTDHANDLES = 0x00000100;

    /// <summary>OEM 代码页只解析一次；<see cref="CodePagesEncodingProvider"/> 在 .NET 10 中已内置。</summary>
    private static readonly Lazy<Encoding> ConsoleEncoding = new(ResolveEncoding, LazyThreadSafetyMode.ExecutionAndPublication);

    public override void Start(ShellProfile profile, int columns, int rows)
    {
        ValidateStart(profile);

        SessionEncoding = ConsoleEncoding.Value;

        var (stdinRead, stdinWrite) = CreatePipePair("创建标准输入管道失败。");
        IntPtr stdoutRead, stdoutWrite;

        try
        {
            (stdoutRead, stdoutWrite) = CreatePipePair("创建标准输出管道失败。");
        }
        catch
        {
            CloseHandles(stdinRead, stdinWrite);
            throw;
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
            SpawnProcess(profile, NativeMethods.CREATE_NO_WINDOW, ref startupInfo);
            created = true;
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

        RunSession(stdinWrite, stdoutRead, "MoxTerm.Pipe.Reader");
    }

    protected override void BeforeTerminate() =>
        // 先结束输入：Shell 读到 EOF，进程一退输出管道立即断开，
        // 读取线程与退出监听毫秒级返回。顺序不能反过来——PowerShell 不因
        // EOF 自行退出，先等线程会白白拖满超时（关闭窗口要卡好几秒）
        CloseInput();

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
}
