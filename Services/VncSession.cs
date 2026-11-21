using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace MoxTerm.Services;

/// <summary>一块被更新的画面区域（BGRA 32bpp，行优先）。</summary>
public readonly record struct VncRect(int X, int Y, int Width, int Height, byte[] Pixels);

/// <summary>
/// 最小可用的 VNC（RFB 3.3 / 3.7 / 3.8）客户端：协商 None / VNC 认证，
/// 只请求 Raw、CopyRect 与 DesktopSize 编码，按区域回调解出的画面。
/// 读取在后台线程进行，发送操作线程安全。
/// </summary>
public sealed class VncSession : IDisposable
{
    private const int EncodingRaw = 0;
    private const int EncodingCopyRect = 1;
    private const int EncodingDesktopSize = -223;

    private readonly string _host;
    private readonly int _port;
    private readonly string _password;
    private readonly object _writeLock = new();

    private TcpClient? _client;
    private NetworkStream? _stream;
    private Thread? _reader;
    private byte[] _framebuffer = [];
    private volatile bool _disposed;

    /// <summary>Apple 屏幕共享（RFB 003.889）实际按 G/B 交换的顺序发像素，需要本地纠正。</summary>
    private bool _swapGreenBlue;

    public VncSession(string host, int port, string password)
    {
        _host = host;
        _port = port <= 0 ? 5900 : port;
        _password = password;
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>服务器名称（ServerInit 里的桌面名）。</summary>
    public string DesktopName { get; private set; } = string.Empty;

    /// <summary>画面区域更新；在后台读取线程上触发。</summary>
    public event Action<VncRect>? RectUpdated;

    /// <summary>服务器要求改变画面尺寸（DesktopSize）；在后台读取线程上触发。</summary>
    public event Action<int, int>? Resized;

    /// <summary>连接意外断开；参数为原因。</summary>
    public event Action<string>? Disconnected;

    public void Connect()
    {
        _client = new TcpClient { NoDelay = true };
        _client.Connect(_host, _port);
        _stream = _client.GetStream();

        Handshake();
    }

    /// <summary>
    /// 开始接收画面。必须在订阅 <see cref="RectUpdated"/> 之后调用：
    /// 服务器对首次整屏请求的响应可能立刻到达，提前读取会把首帧丢掉。
    /// </summary>
    public void Start()
    {
        if (_reader is not null)
        {
            return;
        }

        _reader = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "VncReader",
        };
        _reader.Start();
    }

    // ---------- 握手 ----------

    private void Handshake()
    {
        var stream = _stream!;

        var versionText = Encoding.ASCII.GetString(ReadExact(12));
        if (!versionText.StartsWith("RFB ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"不是 VNC 服务：{versionText.Trim()}");
        }

        var minor = int.Parse(versionText.Substring(8, 3));
        _swapGreenBlue = minor == 889;
        var chosenMinor = minor >= 8 ? 8 : minor >= 7 ? 7 : 3;
        Write(Encoding.ASCII.GetBytes($"RFB 003.00{chosenMinor}\n"));

        byte securityType;
        if (chosenMinor >= 7)
        {
            securityType = NegotiateSecurity(stream, chosenMinor);
        }
        else
        {
            securityType = (byte)ReadInt32(stream);
            if (securityType == 2)
            {
                RequestAuth(stream);
                if (ReadInt32(stream) != 0)
                {
                    throw new InvalidOperationException("认证失败：密码错误");
                }
            }
            else if (securityType != 1)
            {
                throw new InvalidOperationException($"服务器要求不支持的认证方式（{securityType}）");
            }
        }

        _ = securityType;

        // ClientInit：shared = 1，表示允许与其他客户端同时连接
        Write([1]);

        Width = ReadUInt16(stream);
        Height = ReadUInt16(stream);
        _ = ReadExact(16); // 服务器的 PixelFormat，随后会被我们覆盖

        var nameLength = ReadInt32(stream);
        DesktopName = nameLength > 0 ? Encoding.UTF8.GetString(ReadExact(nameLength)) : string.Empty;

        _framebuffer = new byte[Width * Height * 4];

        // SetPixelFormat：消息类型 + 3 字节 padding + 16 字节像素格式（32bpp BGRA、
        // 小端、true color，红移 16 / 绿 8 / 蓝 0）。padding 必须在格式之前，否则服务器会认为格式非法。
        Write(
        [
            0,
            0, 0, 0,
            32, 24, 0, 1,
            0, 255, 0, 255, 0, 255,
            16, 0, 8, 0, 0, 0,
        ]);

        // SetEncodings：消息头（类型 + padding + 编码数量 u16）+ 3 个编码
        var encodings = new List<byte> { 2, 0, 0, 3 };
        encodings.AddRange(BitConverter.GetBytes(EncodingRaw));
        encodings.AddRange(BitConverter.GetBytes(EncodingCopyRect));
        encodings.AddRange(BitConverter.GetBytes(EncodingDesktopSize));
        Write(encodings.ToArray());
    }

    /// <summary>3.7+ 的认证协商，返回选中的认证类型。</summary>
    private byte NegotiateSecurity(NetworkStream stream, int chosenMinor)
    {
        var count = ReadExact(1)[0];
        if (count == 0)
        {
            throw new InvalidOperationException(ReadReason(stream));
        }

        var types = ReadExact(count);
        byte chosen = 0;

        if (_password.Length > 0 && types.Contains((byte)2))
        {
            chosen = 2;
        }
        else if (types.Contains((byte)1))
        {
            chosen = 1;
        }
        else if (types.Contains((byte)2))
        {
            chosen = 2;
        }

        if (chosen == 0)
        {
            throw new InvalidOperationException("服务器没有可用的认证方式");
        }

        Write([chosen]);

        if (chosen == 2)
        {
            RequestAuth(stream);
        }

        // 3.8 必然返回 SecurityResult；3.7 只在 VNC 认证时返回
        if (chosen == 2 || chosenMinor >= 8)
        {
            var result = ReadInt32(stream);
            if (result != 0)
            {
                throw new InvalidOperationException(result == 1 ? "认证失败：密码错误" : ReadReason(stream));
            }
        }

        return chosen;
    }

    private void RequestAuth(NetworkStream stream)
    {
        var challenge = ReadExact(16);
        Write(VncAuth.Encrypt(challenge, _password));
    }

    private static string ReadReason(NetworkStream stream)
    {
        var length = ReadInt32(stream);
        return length > 0 ? Encoding.UTF8.GetString(ReadExact(stream, length)) : "服务器拒绝连接";
    }

    // ---------- 读取循环 ----------

    private void ReadLoop()
    {
        SendUpdateRequest(incremental: false);

        try
        {
            while (!_disposed)
            {
                var messageType = ReadExact(1)[0];
                switch (messageType)
                {
                    case 0:
                        ReadFramebufferUpdate();
                        SendUpdateRequest(incremental: true);
                        break;

                    case 1:
                        _ = ReadExact(3);
                        var colours = ReadUInt16(_stream!);
                        _ = ReadExact(colours * 6);
                        break;

                    case 2:
                        // Bell：忽略
                        break;

                    case 3:
                        _ = ReadExact(3);
                        var textLength = ReadInt32(_stream!);
                        if (textLength > 0)
                        {
                            _ = ReadExact(textLength);
                        }
                        break;

                    default:
                        throw new InvalidOperationException("收到无法识别的服务器消息");
                }
            }
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                Disconnected?.Invoke(ex.Message);
            }
        }
    }

    private void ReadFramebufferUpdate()
    {
        var stream = _stream!;
        _ = ReadExact(1);
        var rectangleCount = ReadUInt16(stream);

        for (var i = 0; i < rectangleCount; i++)
        {
            var x = ReadUInt16(stream);
            var y = ReadUInt16(stream);
            var width = ReadUInt16(stream);
            var height = ReadUInt16(stream);
            var encoding = ReadInt32(stream);

            switch (encoding)
            {
                case EncodingRaw:
                    var pixels = ReadExact(width * height * 4);
                    ApplyRaw(x, y, width, height, pixels);
                    RectUpdated?.Invoke(new VncRect(x, y, width, height, pixels));
                    break;

                case EncodingCopyRect:
                    var sourceX = ReadUInt16(stream);
                    var sourceY = ReadUInt16(stream);
                    ApplyCopyRect(x, y, width, height, sourceX, sourceY);
                    break;

                case EncodingDesktopSize:
                    Resize(width, height);
                    break;

                default:
                    throw new InvalidOperationException($"不支持的画面编码 {encoding}");
            }
        }
    }

    private void ApplyRaw(int x, int y, int width, int height, byte[] pixels)
    {
        // Apple 的服务器无视 SetPixelFormat 的通道位移，实际发来的是 G/B 对调的顺序，
        // 这里交换前两个字节，否则绿色和蓝色会互换（画面偏黄/偏紫）。
        if (_swapGreenBlue)
        {
            for (var index = 0; index + 2 < pixels.Length; index += 4)
            {
                (pixels[index], pixels[index + 1]) = (pixels[index + 1], pixels[index]);
            }
        }

        // 写死不透明，避免 WriteableBitmap 因 alpha=0 把区域渲染成透明
        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
        }

        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(
                pixels,
                row * width * 4,
                _framebuffer,
                ((y + row) * Width + x) * 4,
                width * 4);
        }
    }

    private void ApplyCopyRect(int x, int y, int width, int height, int sourceX, int sourceY)
    {
        var copy = new byte[width * height * 4];

        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(
                _framebuffer,
                ((sourceY + row) * Width + sourceX) * 4,
                copy,
                row * width * 4,
                width * 4);
        }

        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(
                copy,
                row * width * 4,
                _framebuffer,
                ((y + row) * Width + x) * 4,
                width * 4);
        }

        RectUpdated?.Invoke(new VncRect(x, y, width, height, copy));
    }

    private void Resize(int width, int height)
    {
        Width = width;
        Height = height;
        _framebuffer = new byte[width * height * 4];
        Resized?.Invoke(width, height);
    }

    // ---------- 发送 ----------

    private void SendUpdateRequest(bool incremental)
    {
        // 显式请求整块区域：部分服务器（例如 macOS 屏幕共享）会把宽高为 0 的请求
        // 当成 0×0 区域，只返回零星小矩形，必须传真实分辨率才能拿到整屏。
        var buffer = new byte[10];
        buffer[0] = 3;
        buffer[1] = incremental ? (byte)1 : (byte)0;
        buffer[2] = 0;
        buffer[3] = 0;
        buffer[4] = 0;
        buffer[5] = 0;
        buffer[6] = (byte)(Width >> 8);
        buffer[7] = (byte)Width;
        buffer[8] = (byte)(Height >> 8);
        buffer[9] = (byte)Height;
        Write(buffer);
    }

    /// <summary>鼠标事件：<paramref name="buttons"/> 为 RFB 的按键位图（左 1 / 中 2 / 右 4）。</summary>
    public void SendPointer(int x, int y, int buttons)
    {
        if (x < 0 || y < 0 || x > ushort.MaxValue || y > ushort.MaxValue)
        {
            return;
        }

        Write(
        [
            5,
            (byte)buttons,
            (byte)(x >> 8), (byte)x,
            (byte)(y >> 8), (byte)y,
        ]);
    }

    /// <summary>键盘事件：keysym 为 X11 keysym，<paramref name="down"/> 区分按下/抬起。</summary>
    public void SendKey(uint keysym, bool down)
    {
        Write(
        [
            4,
            down ? (byte)1 : (byte)0,
            0, 0,
            (byte)(keysym >> 24), (byte)(keysym >> 16), (byte)(keysym >> 8), (byte)keysym,
        ]);
    }

    private void Write(byte[] data)
    {
        var stream = _stream;
        if (stream is null || _disposed)
        {
            return;
        }

        try
        {
            lock (_writeLock)
            {
                stream.Write(data, 0, data.Length);
            }
        }
        catch (Exception)
        {
            // 连接已断开：忽略发送失败，读取线程会报告断开
        }
    }

    // ---------- 读取辅助 ----------

    private byte[] ReadExact(int count) => ReadExact(_stream!, count);

    private static byte[] ReadExact(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read <= 0)
            {
                throw new IOException("连接已被服务器关闭");
            }

            offset += read;
        }

        return buffer;
    }

    private static ushort ReadUInt16(NetworkStream stream)
    {
        var bytes = ReadExact(stream, 2);
        return (ushort)((bytes[0] << 8) | bytes[1]);
    }

    private static int ReadInt32(NetworkStream stream)
    {
        var bytes = ReadExact(stream, 4);
        return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _stream?.Dispose();
        }
        catch (Exception)
        {
            // 忽略关闭异常
        }

        _client?.Dispose();
    }
}

/// <summary>VNC 认证：把密码按位反转后作为 DES 密钥加密挑战。</summary>
internal static class VncAuth
{
    public static byte[] Encrypt(byte[] challenge, string password)
    {
        var key = new byte[8];
        var passwordBytes = Encoding.ASCII.GetBytes(password);

        for (var i = 0; i < key.Length; i++)
        {
            var value = i < passwordBytes.Length ? passwordBytes[i] : (byte)0;
            key[i] = ReverseBits(value);
        }

        using var des = DES.Create();
        des.Key = key;
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;
        return des.CreateEncryptor().TransformFinalBlock(challenge, 0, challenge.Length);
    }

    private static byte ReverseBits(byte value)
    {
        var result = 0;
        for (var bit = 0; bit < 8; bit++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }

        return (byte)result;
    }
}
