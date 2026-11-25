using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace MoxTerm.Services;

/// <summary>本地只读文件服务的统一接口。</summary>
public interface IFileServer
{
    bool IsRunning { get; }

    int Port { get; }

    void Start(int port, string rootDirectory);

    void Stop();
}

/// <summary>共享服务的公共辅助：路径安全解析与本机地址。</summary>
internal static class FileServerHelper
{
    /// <summary>把请求路径映射到根目录内；越界或非法路径返回 null。</summary>
    public static string? Resolve(string root, string requestPath)
    {
        try
        {
            var relative = Uri.UnescapeDataString(requestPath)
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(root, relative));
            var rootFull = Path.GetFullPath(root);

            return string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? full
                : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>基于 TcpListener 的只读 HTTP 文件服务：目录浏览 + 文件下载。</summary>
public sealed class HttpFileServer : IFileServer
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".htm"] = "text/html; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".xml"] = "application/xml; charset=utf-8",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".7z"] = "application/x-7z-compressed",
        [".gz"] = "application/gzip",
        [".mp3"] = "audio/mpeg",
        [".mp4"] = "video/mp4",
    };

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _root = string.Empty;

    public bool IsRunning { get; private set; }

    public int Port { get; private set; }

    public void Start(int port, string rootDirectory)
    {
        if (IsRunning)
        {
            return;
        }

        _root = rootDirectory;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Port = port;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        IsRunning = false;
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener is { } listener)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(client, token), CancellationToken.None);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[8192];
                var read = await stream.ReadAsync(buffer, token);
                if (read <= 0)
                {
                    return;
                }

                var requestLine = Encoding.UTF8.GetString(buffer, 0, read).Split("\r\n", 2)[0];
                var parts = requestLine.Split(' ');
                if (parts.Length < 2)
                {
                    await WriteAsync(stream, 400, "text/plain; charset=utf-8", "Bad request"u8.ToArray(), token);
                    return;
                }

                var method = parts[0].ToUpperInvariant();
                if (method is not ("GET" or "HEAD"))
                {
                    await WriteAsync(stream, 405, "text/plain; charset=utf-8", "Method not allowed"u8.ToArray(), token);
                    return;
                }

                var urlPath = parts[1].Split('?', 2)[0];
                var target = FileServerHelper.Resolve(_root, urlPath);
                if (target is null)
                {
                    await WriteAsync(stream, 403, "text/plain; charset=utf-8", "Forbidden"u8.ToArray(), token);
                    return;
                }

                if (Directory.Exists(target))
                {
                    var html = BuildListing(target, urlPath.StartsWith('/') ? urlPath : "/" + urlPath);
                    await WriteAsync(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html), token, method == "HEAD");
                    return;
                }

                if (!File.Exists(target))
                {
                    await WriteAsync(stream, 404, "text/plain; charset=utf-8", "Not found"u8.ToArray(), token);
                    return;
                }

                var info = new FileInfo(target);
                var type = ContentTypes.TryGetValue(info.Extension, out var known)
                    ? known
                    : "application/octet-stream";
                var header = BuildHeader(200, info.Length, type);

                await stream.WriteAsync(Encoding.UTF8.GetBytes(header), token);
                if (method == "HEAD")
                {
                    return;
                }

                await using var file = File.OpenRead(target);
                await file.CopyToAsync(stream, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // 单个连接异常不影响服务整体
            }
        }
    }

    private static async Task WriteAsync(
        NetworkStream stream,
        int status,
        string contentType,
        byte[] body,
        CancellationToken token,
        bool headOnly = false)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(BuildHeader(status, body.Length, contentType)), token);
        if (!headOnly)
        {
            await stream.WriteAsync(body, token);
        }
    }

    private static string BuildHeader(int status, long length, string contentType)
    {
        var reason = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "Error",
        };

        return $"HTTP/1.1 {status} {reason}\r\nContent-Length: {length}\r\nContent-Type: {contentType}\r\nConnection: close\r\n\r\n";
    }

    /// <summary>生成与当前路径对应的目录索引页。</summary>
    private static string BuildListing(string directory, string urlPath)
    {
        var basePath = urlPath.EndsWith('/') ? urlPath : urlPath + "/";
        var builder = new StringBuilder();
        builder.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        builder.Append($"<title>Index of {WebUtility.HtmlEncode(basePath)}</title>");
        builder.Append("<style>body{font-family:Segoe UI,sans-serif;margin:32px;color:#1f1f1f}");
        builder.Append("h1{font-size:20px;font-weight:600}table{border-collapse:collapse;min-width:480px}");
        builder.Append("th,td{text-align:left;padding:6px 16px 6px 0;font-size:14px}");
        builder.Append("a{color:#0f6cbd;text-decoration:none}a:hover{text-decoration:underline}");
        builder.Append("td.size{color:#666;text-align:right}</style></head><body>");
        builder.Append($"<h1>Index of {WebUtility.HtmlEncode(basePath)}</h1><table>");
        builder.Append("<tr><th>名称</th><th>大小</th><th>修改时间</th></tr>");

        if (basePath != "/")
        {
            var parent = basePath.TrimEnd('/');
            parent = parent[..(parent.LastIndexOf('/') + 1)];
            builder.Append($"<tr><td><a href=\"{WebUtility.HtmlEncode(parent)}\">../</a></td><td></td><td></td></tr>");
        }

        var entries = new DirectoryInfo(directory)
            .EnumerateFileSystemInfos()
            .OrderBy(entry => entry is FileInfo)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var isDirectory = entry is DirectoryInfo;
            var name = entry.Name + (isDirectory ? "/" : string.Empty);
            var href = basePath + Uri.EscapeDataString(entry.Name) + (isDirectory ? "/" : string.Empty);
            var size = entry is FileInfo file
                ? FileSize.Format(file.Length)
                : string.Empty;
            var modified = entry.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            builder.Append($"<tr><td><a href=\"{WebUtility.HtmlEncode(href)}\">{WebUtility.HtmlEncode(name)}</a></td>");
            builder.Append($"<td class=\"size\">{size}</td><td>{modified}</td></tr>");
        }

        builder.Append("</table></body></html>");
        return builder.ToString();
    }
}

/// <summary>
/// 精简 FTP 服务：支持匿名登录、PASV / EPSV 被动模式、目录浏览与文件下载，只读。
/// </summary>
public sealed class FtpFileServer : IFileServer
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<TcpClient, byte> _sessions = new();
    private string _root = string.Empty;

    public bool IsRunning { get; private set; }

    public int Port { get; private set; }

    public void Start(int port, string rootDirectory)
    {
        if (IsRunning)
        {
            return;
        }

        _root = rootDirectory;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Port = port;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        IsRunning = false;
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;

        // 已建立的控制连接也要断开，否则读写线程会一直挂着
        foreach (var client in _sessions.Keys)
        {
            try
            {
                client.Close();
            }
            catch
            {
            }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener is { } listener)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch
            {
                break;
            }

            _sessions[client] = 0;
            _ = Task.Run(() => ServeAsync(client, token), CancellationToken.None);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\r\n",
                };

                await writer.WriteLineAsync("220 MoxTerm FTP server ready");

                var currentDirectory = "/";
                TcpListener? dataListener = null;

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line is null)
                        {
                            break;
                        }

                        var separator = line.IndexOf(' ');
                        var command = (separator < 0 ? line : line[..separator]).ToUpperInvariant();
                        var argument = separator < 0 ? string.Empty : line[(separator + 1)..].Trim();

                        switch (command)
                        {
                            case "USER":
                                await writer.WriteLineAsync("331 Password required");
                                break;
                            case "PASS":
                                await writer.WriteLineAsync("230 Logged in");
                                break;
                            case "SYST":
                                await writer.WriteLineAsync("215 UNIX Type: L8");
                                break;
                            case "FEAT":
                                await writer.WriteLineAsync("211-Features:\r\n UTF8\r\n EPSV\r\n SIZE\r\n211 End");
                                break;
                            case "OPTS":
                                await writer.WriteLineAsync("200 OK");
                                break;
                            case "TYPE":
                                await writer.WriteLineAsync("200 Type set");
                                break;
                            case "NOOP":
                                await writer.WriteLineAsync("200 OK");
                                break;
                            case "PWD":
                            case "XPWD":
                                await writer.WriteLineAsync($"257 \"{currentDirectory}\" is the current directory");
                                break;
                            case "CWD":
                            {
                                var next = CombineFtpPath(currentDirectory, argument);
                                var target = FileServerHelper.Resolve(_root, next);
                                if (target is null || !Directory.Exists(target))
                                {
                                    await writer.WriteLineAsync("550 Directory not found");
                                    break;
                                }

                                currentDirectory = next.EndsWith('/') ? next : next + "/";
                                await writer.WriteLineAsync("250 Directory changed");
                                break;
                            }
                            case "PASV":
                            case "EPSV":
                            {
                                dataListener?.Stop();
                                dataListener = new TcpListener(IPAddress.Any, 0);
                                dataListener.Start();
                                var dataPort = ((IPEndPoint)dataListener.LocalEndpoint).Port;

                                if (command == "EPSV")
                                {
                                    await writer.WriteLineAsync($"229 Entering Extended Passive Mode (|||{dataPort}|)");
                                }
                                else
                                {
                                    var local = ((IPEndPoint)client.Client.LocalEndPoint!).Address.GetAddressBytes();
                                    await writer.WriteLineAsync(
                                        $"227 Entering Passive Mode ({local[0]},{local[1]},{local[2]},{local[3]},{dataPort / 256},{dataPort % 256})");
                                }

                                break;
                            }
                            case "LIST":
                            case "NLST":
                            {
                                var target = FileServerHelper.Resolve(_root, CombineFtpPath(currentDirectory, argument));
                                if (target is null || !Directory.Exists(target))
                                {
                                    await writer.WriteLineAsync("550 Path not found");
                                    break;
                                }

                                if (dataListener is null)
                                {
                                    await writer.WriteLineAsync("425 Use PASV first");
                                    break;
                                }

                                await writer.WriteLineAsync("150 Opening data connection");
                                dataListener = await SendListingAsync(writer, dataListener, target, command == "NLST", token);
                                break;
                            }
                            case "RETR":
                            {
                                var target = FileServerHelper.Resolve(_root, CombineFtpPath(currentDirectory, argument));
                                if (target is null || !File.Exists(target))
                                {
                                    await writer.WriteLineAsync("550 File not found");
                                    break;
                                }

                                if (dataListener is null)
                                {
                                    await writer.WriteLineAsync("425 Use PASV first");
                                    break;
                                }

                                await writer.WriteLineAsync("150 Opening data connection");
                                dataListener = await SendFileAsync(writer, dataListener, target, token);
                                break;
                            }
                            case "SIZE":
                            {
                                var target = FileServerHelper.Resolve(_root, CombineFtpPath(currentDirectory, argument));
                                if (target is null || !File.Exists(target))
                                {
                                    await writer.WriteLineAsync("550 File not found");
                                    break;
                                }

                                await writer.WriteLineAsync($"213 {new FileInfo(target).Length}");
                                break;
                            }
                            case "QUIT":
                                await writer.WriteLineAsync("221 Bye");
                                return;
                            default:
                                await writer.WriteLineAsync("502 Command not implemented");
                                break;
                        }
                    }
                }
                finally
                {
                    dataListener?.Stop();
                }
            }
        }
        catch
        {
        }
        finally
        {
            _sessions.TryRemove(client, out _);
        }
    }

    /// <summary>接受一次数据连接并发送目录列表，失败返回 null（下次需重新 PASV）。</summary>
    private static async Task<TcpListener?> SendListingAsync(
        StreamWriter writer,
        TcpListener dataListener,
        string directory,
        bool namesOnly,
        CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            using var dataClient = await dataListener.AcceptTcpClientAsync(timeout.Token);
            await using (var dataStream = dataClient.GetStream())
            using (var dataWriter = new StreamWriter(dataStream, new UTF8Encoding(false), 4096, leaveOpen: true))
            {
                dataWriter.NewLine = "\r\n";

                foreach (var entry in new DirectoryInfo(directory)
                    .EnumerateFileSystemInfos()
                    .OrderBy(item => item is FileInfo)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (namesOnly)
                    {
                        await dataWriter.WriteLineAsync(entry.Name);
                        continue;
                    }

                    var permissions = entry is DirectoryInfo ? "drwxr-xr-x" : "-rw-r--r--";
                    var size = entry is FileInfo file ? file.Length : 0;
                    var modified = entry.LastWriteTime.ToString("MMM dd HH:mm", CultureInfo.InvariantCulture);
                    await dataWriter.WriteLineAsync($"{permissions} 1 owner group {size,12} {modified} {entry.Name}");
                }

                await dataWriter.FlushAsync();
            }

            await writer.WriteLineAsync("226 Transfer complete");
            dataListener.Stop();
            return null;
        }
        catch (OperationCanceledException)
        {
            dataListener.Stop();
            return null;
        }
        catch (Exception ex)
        {
            dataListener.Stop();
            try
            {
                await writer.WriteLineAsync($"426 Transfer failed: {ex.Message}");
            }
            catch
            {
            }

            return null;
        }
    }

    private static async Task<TcpListener?> SendFileAsync(
        StreamWriter writer,
        TcpListener dataListener,
        string filePath,
        CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            using var dataClient = await dataListener.AcceptTcpClientAsync(timeout.Token);
            var dataStream = dataClient.GetStream();

            await using (var file = File.OpenRead(filePath))
            {
                await file.CopyToAsync(dataStream, token);
            }

            await dataStream.FlushAsync(token);
            await writer.WriteLineAsync("226 Transfer complete");
            dataListener.Stop();
            return null;
        }
        catch (OperationCanceledException)
        {
            dataListener.Stop();
            return null;
        }
        catch (Exception ex)
        {
            dataListener.Stop();
            try
            {
                await writer.WriteLineAsync($"426 Transfer failed: {ex.Message}");
            }
            catch
            {
            }

            return null;
        }
    }

    /// <summary>把客户端参数合并到当前 FTP 目录（参数可为相对或绝对路径）。</summary>
    private static string CombineFtpPath(string currentDirectory, string argument)
    {
        if (string.IsNullOrWhiteSpace(argument) || argument == "-")
        {
            return currentDirectory;
        }

        // LIST 的 -la 之类的选项忽略
        if (argument.StartsWith('-'))
        {
            return currentDirectory;
        }

        return argument.StartsWith('/')
            ? argument
            : currentDirectory.TrimEnd('/') + "/" + argument;
    }
}

/// <summary>只读 TFTP 服务：支持 RRQ（octet 模式），按 512 字节分块并可重传。</summary>
public sealed class TftpFileServer : IFileServer
{
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private string _root = string.Empty;

    public bool IsRunning { get; private set; }

    public int Port { get; private set; }

    public void Start(int port, string rootDirectory)
    {
        if (IsRunning)
        {
            return;
        }

        _root = rootDirectory;
        _listener = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        Port = port;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        _ = ReceiveLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        IsRunning = false;
        _cts?.Cancel();
        _listener?.Dispose();
        _listener = null;
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener is { } listener)
        {
            UdpReceiveResult result;

            try
            {
                result = await listener.ReceiveAsync(token);
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(result.Buffer, result.RemoteEndPoint, token), CancellationToken.None);
        }
    }

    private async Task HandleRequestAsync(byte[] request, IPEndPoint client, CancellationToken token)
    {
        if (request.Length < 4)
        {
            return;
        }

        var opcode = (request[0] << 8) | request[1];
        if (opcode != 1)
        {
            // 只读服务：WRQ 等其它操作一律拒绝
            await SendErrorAsync(client, 2, "Access violation");
            return;
        }

        var nameEnd = Array.IndexOf(request, (byte)0, 2);
        if (nameEnd < 0)
        {
            return;
        }

        var fileName = Encoding.UTF8.GetString(request, 2, nameEnd - 2);
        var target = FileServerHelper.Resolve(_root, fileName);
        if (target is null || !File.Exists(target))
        {
            await SendErrorAsync(client, 1, "File not found");
            return;
        }

        try
        {
            using var data = new UdpClient(0);
            await using var file = File.OpenRead(target);

            ushort block = 1;
            var buffer = new byte[4 + 512];

            while (!token.IsCancellationRequested)
            {
                var read = await file.ReadAsync(buffer.AsMemory(4, 512), token);
                buffer[0] = 0;
                buffer[1] = 3;
                buffer[2] = (byte)(block >> 8);
                buffer[3] = (byte)block;

                var acknowledged = false;
                for (var attempt = 0; attempt < 3 && !acknowledged; attempt++)
                {
                    await data.SendAsync(buffer.AsMemory(0, read + 4), client, token);

                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));

                    try
                    {
                        var reply = await data.ReceiveAsync(timeout.Token);
                        acknowledged = reply.Buffer.Length >= 4
                            && reply.Buffer[1] == 4
                            && ((reply.Buffer[2] << 8) | reply.Buffer[3]) == block;
                    }
                    catch (OperationCanceledException)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }
                    }
                }

                if (!acknowledged || read < 512)
                {
                    return;
                }

                block++;
            }
        }
        catch
        {
            // 单个传输失败不影响服务
        }
    }

    private static async Task SendErrorAsync(IPEndPoint client, ushort code, string message)
    {
        using var udp = new UdpClient(0);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var packet = new byte[4 + messageBytes.Length + 1];
        packet[0] = 0;
        packet[1] = 5;
        packet[2] = (byte)(code >> 8);
        packet[3] = (byte)code;
        messageBytes.CopyTo(packet, 4);
        await udp.SendAsync(packet, packet.Length, client);
    }
}
