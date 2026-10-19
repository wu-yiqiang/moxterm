using System.IO.Ports;
using System.Text;

namespace MoxTerm.Services;

/// <summary>
/// 串口会话：打开本地 COM 口（8 位数据位、无校验、1 位停止位、无流控），
/// 收到的字节按 UTF-8 解码后交给终端渲染，键盘输入原样写入串口。
/// </summary>
public sealed class SerialTerminalSession : ITerminalSession
{
    private const int DefaultBaudRate = 115200;

    private SerialPort? _port;
    private Thread? _thread;
    private volatile bool _running;
    private bool _disposed;

    public event EventHandler<string>? OutputReceived;

    public event EventHandler<int>? Exited;

    public void Start(ShellProfile profile, int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (profile.SerialPortName is not { Length: > 0 } portName)
        {
            throw new InvalidOperationException("串口会话未指定端口。");
        }

        var port = new SerialPort(
            portName,
            profile.BaudRate > 0 ? profile.BaudRate : DefaultBaudRate,
            Parity.None,
            8,
            StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = 2000,
            DtrEnable = true,
            RtsEnable = true,
        };

        // 打开失败（端口不存在/被占用）直接抛出，由终端把原因显示出来
        port.Open();

        _port = port;
        _running = true;

        _thread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "MoxTerm.Serial.Reader",
        };
        _thread.Start();
    }

    public void Write(string text)
    {
        var port = _port;
        if (!_running || port is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            port.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException)
        {
            // 串口已断开，输入丢弃即可
        }
    }

    /// <summary>串口没有窗口尺寸概念，忽略。</summary>
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

        try
        {
            _port?.Close();
            _port?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // 串口已断开
        }

        _port = null;

        // 关闭串口会让阻塞中的读取抛出异常，线程随之退出
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private void ReadLoop()
    {
        var port = _port;
        if (port is null)
        {
            return;
        }

        var buffer = new byte[4096];
        var characters = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
        var decoder = Encoding.UTF8.GetDecoder();

        try
        {
            while (true)
            {
                var read = port.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                var length = decoder.GetChars(buffer, 0, read, characters, 0, flush: false);
                if (length > 0)
                {
                    OutputReceived?.Invoke(this, new string(characters, 0, length));
                }
            }
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // 会话被释放或串口断开
        }

        _running = false;
        Exited?.Invoke(this, 0);
    }
}
