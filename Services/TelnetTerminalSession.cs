using System.Net.Sockets;
using System.Text;

namespace MoxTerm.Services;

/// <summary>
/// Telnet 会话：直连 TCP 端口，只做最小 NVT 协商——一律拒绝对方提出的选项
/// （可按帧回 <c>DONT</c>/<c>WONT</c>），保持纯数据通道，收到的字节按 UTF-8
/// 解码后交给终端渲染。连接在后台线程建立，避免阻塞界面。
/// </summary>
public sealed class TelnetTerminalSession : ITerminalSession
{
    private const byte Iac = 0xFF;  // 命令引导
    private const byte Dont = 0xFE;
    private const byte Do = 0xFD;
    private const byte Wont = 0xFC;
    private const byte Will = 0xFB;
    private const byte Sb = 0xFA;   // 子协商开始
    private const byte Se = 0xF0;   // 子协商结束

    private readonly object _writeLock = new();

    private TcpClient? _client;
    private NetworkStream? _stream;
    private Thread? _thread;
    private volatile bool _running;
    private bool _disposed;

    public event EventHandler<string>? OutputReceived;

    public event EventHandler<int>? Exited;

    public void Start(ShellProfile profile, int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (profile.Host is not { Length: > 0 } host)
        {
            throw new InvalidOperationException("Telnet 会话未指定主机。");
        }

        var port = profile.Port > 0 ? profile.Port : 23;
        _running = true;

        _thread = new Thread(() => Connect(host, port))
        {
            IsBackground = true,
            Name = "MoxTerm.Telnet",
        };
        _thread.Start();
    }

    public void Write(string text)
    {
        if (!_running || string.IsNullOrEmpty(text))
        {
            return;
        }

        Send(ToNvtBytes(text));
    }

    /// <summary>不做窗口尺寸协商（NAWS 在选项请求阶段已被拒绝）。</summary>
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
            _client?.Close();
            _client?.Dispose();
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // 连接已经断开
        }

        _client = null;
        _stream = null;

        // 关闭套接字会让阻塞中的读取立刻返回，线程随之退出
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private void Connect(string host, int port)
    {
        TcpClient client;

        try
        {
            client = new TcpClient();
            client.Connect(host, port);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException or InvalidOperationException)
        {
            _running = false;
            OutputReceived?.Invoke(this, $"\r\n{Loc.Instance.ConnectionFailed($"{host}:{port}", ex.Message)}\r\n");
            return;
        }

        if (_disposed)
        {
            // 连接建立期间标签页已被关闭
            client.Dispose();
            _running = false;
            return;
        }

        _client = client;
        _stream = client.GetStream();

        ReadLoop();

        // 对端关闭连接，会话结束
        _running = false;
        Exited?.Invoke(this, 0);
    }

    private void ReadLoop()
    {
        var stream = _stream;
        if (stream is null)
        {
            return;
        }

        var buffer = new byte[4096];
        var data = new byte[4096];
        var characters = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
        var decoder = Encoding.UTF8.GetDecoder();
        var subnegotiation = false;

        try
        {
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                var count = 0;

                for (var i = 0; i < read; i++)
                {
                    var value = buffer[i];

                    if (value != Iac)
                    {
                        if (!subnegotiation)
                        {
                            data[count++] = value;
                        }

                        continue;
                    }

                    // IAC 之后还有命令字节；本块内取不到就丢弃这一帧（对端会在下一块补齐）
                    if (++i >= read)
                    {
                        break;
                    }

                    switch (buffer[i])
                    {
                        case Iac:
                            // 数据里转义的 0xFF
                            if (!subnegotiation)
                            {
                                data[count++] = Iac;
                            }

                            break;

                        case Will:
                        case Wont:
                        case Do:
                        case Dont:
                            if (++i >= read)
                            {
                                break;
                            }

                            RefuseOption(buffer[i - 1], buffer[i]);
                            break;

                        case Sb:
                            subnegotiation = true;
                            break;

                        case Se:
                            subnegotiation = false;
                            break;
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                var length = decoder.GetChars(data, 0, count, characters, 0, flush: false);
                if (length > 0)
                {
                    OutputReceived?.Invoke(this, new string(characters, 0, length));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
        {
            // 会话被释放或对端断开
        }
    }

    /// <summary>拒绝对方的选项请求：<c>WILL</c> 回 <c>DONT</c>，<c>DO</c> 回 <c>WONT</c>。</summary>
    private void RefuseOption(byte command, byte option)
    {
        byte reply = command switch
        {
            Will => Dont,
            Do => Wont,
            _ => 0,
        };

        if (reply != 0)
        {
            Send([Iac, reply, option]);
        }
    }

    /// <summary>按 NVT 约定编码输入：回车发 CR LF，数据里的 0xFF 转义成 IAC IAC。</summary>
    private static byte[] ToNvtBytes(string text)
    {
        var raw = Encoding.UTF8.GetBytes(text);
        var payload = new List<byte>(raw.Length + 8);

        for (var i = 0; i < raw.Length; i++)
        {
            switch (raw[i])
            {
                case 0x0D:
                    payload.Add(0x0D);
                    payload.Add(0x0A);

                    // 输入是 CRLF 时不要重复发 LF
                    if (i + 1 < raw.Length && raw[i + 1] == 0x0A)
                    {
                        i++;
                    }

                    break;

                case 0x0A:
                    payload.Add(0x0D);
                    payload.Add(0x0A);
                    break;

                case Iac:
                    payload.Add(Iac);
                    payload.Add(Iac);
                    break;

                default:
                    payload.Add(raw[i]);
                    break;
            }
        }

        return [.. payload];
    }

    private void Send(byte[] payload)
    {
        var stream = _stream;
        if (stream is null)
        {
            return;
        }

        // 界面线程发输入、后台线程回协商帧，写入需要串行化
        lock (_writeLock)
        {
            try
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // 连接已断开，输入丢弃即可
            }
        }
    }
}
