using FluentFTP;

namespace MoxTerm.Services;

/// <summary>
/// FTP 连接封装：基于 FluentFTP 的 <see cref="FtpClient"/>，方法同步阻塞，
/// 调用方负责放到后台线程执行。通用逻辑见 <see cref="RemoteFileSystem"/>。
/// </summary>
public sealed class FtpSession : RemoteFileSystem
{
    private readonly FtpClient _client;
    private string _homePath = "/";
    private bool _disposed;

    public FtpSession(string host, int port, string username, string password)
    {
        _client = new FtpClient
        {
            Host = host,
            Port = port <= 0 ? 21 : port,
            Credentials = new System.Net.NetworkCredential(username, password),
        };
        _client.Config.ConnectTimeout = 15_000;
        _client.Config.ReadTimeout = 15_000;
        _client.Config.DataConnectionConnectTimeout = 15_000;
        _client.Config.DataConnectionReadTimeout = 15_000;
    }

    public override string HomePath => _homePath;

    public override void Connect()
    {
        _client.Connect();

        var working = _client.GetWorkingDirectory();
        _homePath = string.IsNullOrEmpty(working) ? "/" : working;
    }

    public override IReadOnlyList<FileEntry> List(string path)
    {
        var entries = new List<FileEntry>();

        foreach (var item in _client.GetListing(path))
        {
            var isDirectory = item.Type == FtpObjectType.Directory;
            entries.Add(new FileEntry(
                item.Name,
                item.FullName,
                isDirectory,
                isDirectory ? 0 : item.Size,
                item.Modified == DateTime.MinValue ? default : item.Modified));
        }

        return entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    protected override bool DirectoryExists(string path) => _client.DirectoryExists(path);

    protected override void UploadFileCore(string localPath, string remotePath, Action<ulong>? progress)
    {
        _client.UploadFile(
            localPath,
            remotePath,
            FtpRemoteExists.Overwrite,
            createRemoteDir: true,
            FtpVerify.None,
            Progress(progress));
    }

    protected override void DownloadFileCore(string remotePath, string localPath, Action<ulong>? progress)
    {
        _client.DownloadFile(
            localPath,
            remotePath,
            FtpLocalExists.Overwrite,
            FtpVerify.None,
            Progress(progress));
    }

    public override void CreateDirectory(string path) => _client.CreateDirectory(path);

    protected override void DeleteFileCore(string path) => _client.DeleteFile(path);

    protected override void DeleteDirectoryCore(string path) => _client.DeleteDirectory(path, FtpListOption.Recursive);

    protected override void RenameCore(string from, string to) => _client.Rename(from, to);

    /// <summary>把 FluentFTP 的进度对象转成统一的“已传字节数”回调。</summary>
    private static Action<FtpProgress>? Progress(Action<ulong>? report) =>
        report is null ? null : p => report((ulong)Math.Max(0, p.TransferredBytes));

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
