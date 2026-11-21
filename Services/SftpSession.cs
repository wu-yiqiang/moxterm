using Renci.SshNet;

namespace MoxTerm.Services;

/// <summary>
/// SFTP 连接封装：基于 SSH.NET 的 <see cref="SftpClient"/>，所有方法都是同步阻塞的，
/// 调用方负责放到后台线程执行。通用逻辑见 <see cref="RemoteFileSystem"/>。
/// </summary>
public sealed class SftpSession : RemoteFileSystem
{
    private readonly SftpClient _client;
    private string _homePath = "/";
    private bool _disposed;

    public SftpSession(string host, int port, string username, string password)
    {
        _client = new SftpClient(host, port <= 0 ? 22 : port, username, password);
        _client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
    }

    public override string HomePath => _homePath;

    public override void Connect()
    {
        _client.Connect();
        _homePath = _client.WorkingDirectory;
    }

    public override IReadOnlyList<FileEntry> List(string path)
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

    protected override bool DirectoryExists(string path) => _client.Exists(path);

    protected override void UploadFileCore(string localPath, string remotePath, Action<ulong>? progress)
    {
        using var input = File.OpenRead(localPath);
        _client.UploadFile(input, remotePath, canOverride: true, uploadCallback: progress);
    }

    protected override void DownloadFileCore(string remotePath, string localPath, Action<ulong>? progress)
    {
        using var output = File.Create(localPath);
        _client.DownloadFile(remotePath, output, progress);
    }

    public override void CreateDirectory(string path) => _client.CreateDirectory(path);

    protected override void DeleteFileCore(string path) => _client.DeleteFile(path);

    protected override void DeleteDirectoryCore(string path) => _client.DeleteDirectory(path);

    protected override void RenameCore(string from, string to) => _client.RenameFile(from, to);

    public override void Dispose()
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
