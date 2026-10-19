using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MoxTerm.Services;

/// <summary>
/// 基于 Windows 伪控制台（ConPTY）的终端会话实现。
/// 通过一对匿名管道与 <c>CreatePseudoConsole</c> 通信，子进程以普通控制台程序方式运行。
/// </summary>
public sealed class ConPtyTerminalSession : ProcessTerminalSession
{
    private IntPtr _pseudoConsole = IntPtr.Zero;

    public override void Start(ShellProfile profile, int columns, int rows)
    {
        ValidateStart(profile);

        columns = Math.Clamp(columns, 1, 1000);
        rows = Math.Clamp(rows, 1, 1000);

        var size = new NativeMethods.COORD((short)columns, (short)rows);

        // consoleInput  -> ConPTY 读，clientInput  -> 我们写
        var (consoleInput, clientInput) = CreatePipePair("创建 ConPTY 输入管道失败。");
        IntPtr clientOutput, consoleOutput;

        try
        {
            // clientOutput -> 我们读，consoleOutput -> ConPTY 写
            (clientOutput, consoleOutput) = CreatePipePair("创建 ConPTY 输出管道失败。");
        }
        catch
        {
            CloseHandles(consoleInput, clientInput);
            throw;
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

            SpawnProcess(profile, NativeMethods.EXTENDED_STARTUPINFO_PRESENT, ref startupInfo);

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

        RunSession(clientInput, clientOutput, "MoxTerm.ConPTY.Reader");
    }

    public override void Resize(int columns, int rows)
    {
        if (_pseudoConsole == IntPtr.Zero)
        {
            return;
        }

        columns = Math.Clamp(columns, 1, 1000);
        rows = Math.Clamp(rows, 1, 1000);

        NativeMethods.ResizePseudoConsole(_pseudoConsole, new NativeMethods.COORD((short)columns, (short)rows));
    }

    protected override void BeforeTerminate()
    {
        // 关闭伪控制台会让 Shell 进程终止、输出管道断开，读取线程随之退出
        if (_pseudoConsole != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(_pseudoConsole);
            _pseudoConsole = IntPtr.Zero;
        }
    }
}
