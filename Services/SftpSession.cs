using Renci.SshNet;

namespace MoxTerm.Services;

/// <summary>文件项：本地与远程列表共用，供 SFTP 面板绑定。</summary>
public sealed record FileEntry(string Name, string FullPath, bool IsDirectory, long Size, DateTime Modified)
{
    /// <summary>目录名带尾随斜杠，便于与文件区分。</summary>
    public string DisplayName => IsDirectory ? $"{Name}/" : Name;

    public string SizeText => IsDirectory ? string.Empty : FormatSize(Size);

    public string ModifiedText => Modified == default ? string.Empty : Modified.ToString("yyyy-MM-dd HH:mm");

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };
}

/// <summary>
/// SFTP 连接封装：基于 SSH.NET 的 <see cref="SftpClient"/>，所有方法都是同步阻塞的，
/// 调用方负责放到后台线程执行。实例不可重入，创建后连接一次，释放时断开。
/// </summary>
public sealed class SftpSession : IDisposable
{
    private readonly SftpClient _client;
    private bool _disposed;

    public SftpSession(string host, int port, string username, string password)
    {
        _client = new SftpClient(host, port <= 0 ? 22 : port, username, password);
        _client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
    }

    /// <summary>登录后的初始目录（通常是用户主目录）。</summary>
    public string HomePath => _client.WorkingDirectory;

    public void Connect() => _client.Connect();

    /// <summary>列出目录内容：目录在前、同类按名称排序，跳过 <c>.</c> 与 <c>..</c>。</summary>
    public IReadOnlyList<FileEntry> List(string path)
    {
        var entries = new List<FileEntry>();

        foreach (var item in _client.ListDirectory(path))
        {
            if (item.Name is "." or "..")
            {
                continue;
            }

            entries.Add(new FileEntry(
                item.Name,
                item.FullName,
                item.IsDirectory,
                item.Length,
                item.LastWriteTimeUtc.ToLocalTime()));
        }

        return entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void Upload(string localPath, string remoteDirectory, Action<ulong>? progress = null)
    {
        var remotePath = CombineRemote(remoteDirectory, Path.GetFileName(localPath));
        using var input = File.OpenRead(localPath);
        _client.UploadFile(input, remotePath, canOverride: true, uploadCallback: progress);
    }

    public void Download(string remotePath, string localDirectory, Action<ulong>? progress = null)
    {
        var localPath = Path.Combine(localDirectory, Path.GetFileName(remotePath));
        using var output = File.Create(localPath);
        _client.DownloadFile(remotePath, output, progress);
    }

    /// <summary>
    /// 递归上传本地目录到远程目录：自动创建缺失的子目录，逐个上传其中的文件。
    /// <paramref name="onFile"/> 在每个文件开始前回调（文件名、字节数），<paramref name="progress"/> 汇报当前文件已传字节。
    /// </summary>
    public void UploadDirectory(
        string localDirectory,
        string remoteDirectory,
        Action<string, long>? onFile = null,
        Action<ulong>? progress = null)
    {
        var target = CombineRemote(remoteDirectory, Path.GetFileName(localDirectory));
        EnsureDirectory(target);

        foreach (var file in Directory.EnumerateFiles(localDirectory))
        {
            var name = Path.GetFileName(file);
            onFile?.Invoke(name, new FileInfo(file).Length);

            using var input = File.OpenRead(file);
            _client.UploadFile(input, CombineRemote(target, name), canOverride: true, uploadCallback: progress);
        }

        foreach (var directory in Directory.EnumerateDirectories(localDirectory))
        {
            UploadDirectory(directory, target, onFile, progress);
        }
    }

    private void EnsureDirectory(string path)
    {
        if (!_client.Exists(path))
        {
            _client.CreateDirectory(path);
        }
    }

    public void CreateDirectory(string path) => _client.CreateDirectory(path);

    /// <summary>删除文件；目录则递归删除，避免遗留空壳或失败。</summary>
    public void Delete(FileEntry entry)
    {
        if (!entry.IsDirectory)
        {
            _client.DeleteFile(entry.FullPath);
            return;
        }

        foreach (var child in List(entry.FullPath))
        {
            Delete(child);
        }

        _client.DeleteDirectory(entry.FullPath);
    }

    public void Rename(FileEntry entry, string newName)
    {
        var target = CombineRemote(Path.GetDirectoryName(entry.FullPath)?.Replace('\\', '/') ?? "/", newName);
        _client.RenameFile(entry.FullPath, target);
    }

    /// <summary>拼接远程路径：统一使用正斜杠，避免根目录出现双斜杠。</summary>
    public static string CombineRemote(string directory, string name) =>
        directory.EndsWith('/') ? directory + name : $"{directory}/{name}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
        }
        catch (Exception)
        {
            // 断开失败不影响释放
        }

        _client.Dispose();
    }
}
