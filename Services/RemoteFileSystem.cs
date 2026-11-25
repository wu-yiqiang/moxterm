namespace MoxTerm.Services;

/// <summary>把字节数格式化成人类可读文本，本地与远程文件列表共用。</summary>
internal static class FileSize
{
    public static string Format(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };
}

/// <summary>文件项：本地与远程列表共用，供文件面板绑定。</summary>
public sealed record FileEntry(string Name, string FullPath, bool IsDirectory, long Size, DateTime Modified)
{
    /// <summary>目录名带尾随斜杠，便于与文件区分。</summary>
    public string DisplayName => IsDirectory ? $"{Name}/" : Name;

    public string SizeText => IsDirectory ? string.Empty : FileSize.Format(Size);

    public string ModifiedText => Modified == default ? string.Empty : Modified.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>
/// 远程文件系统抽象（SFTP / FTP 共用）。高层操作（上传、递归上传目录、下载、递归删除、
/// 重命名）在这里实现一次，具体协议只需提供列目录、创建目录、读/写单个文件等原语。
/// 所有方法都是同步阻塞的，调用方负责放到后台线程执行。
/// </summary>
public abstract class RemoteFileSystem : IDisposable
{
    /// <summary>登录后的初始目录（SFTP 通常是用户主目录，FTP 为登录后的当前目录）。</summary>
    public abstract string HomePath { get; }

    public abstract void Connect();

    /// <summary>列出目录内容：目录在前、同类按名称排序。</summary>
    public abstract IReadOnlyList<FileEntry> List(string path);

    public abstract void CreateDirectory(string path);

    protected abstract bool DirectoryExists(string path);

    /// <summary>把本地文件写到指定的远程完整路径。</summary>
    protected abstract void UploadFileCore(string localPath, string remotePath, Action<ulong>? progress);

    /// <summary>把远程文件读到指定的本地完整路径。</summary>
    protected abstract void DownloadFileCore(string remotePath, string localPath, Action<ulong>? progress);

    protected abstract void DeleteFileCore(string path);

    protected abstract void DeleteDirectoryCore(string path);

    protected abstract void RenameCore(string from, string to);

    public void Upload(string localPath, string remoteDirectory, Action<ulong>? progress = null) =>
        UploadFileCore(localPath, CombineRemote(remoteDirectory, Path.GetFileName(localPath)), progress);

    public void Download(string remotePath, string localDirectory, Action<ulong>? progress = null) =>
        DownloadFileCore(remotePath, Path.Combine(localDirectory, Path.GetFileName(remotePath)), progress);

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
            UploadFileCore(file, CombineRemote(target, name), progress);
        }

        foreach (var directory in Directory.EnumerateDirectories(localDirectory))
        {
            UploadDirectory(directory, target, onFile, progress);
        }
    }

    private void EnsureDirectory(string path)
    {
        if (!DirectoryExists(path))
        {
            CreateDirectory(path);
        }
    }

    /// <summary>删除文件；目录则递归删除，避免遗留空壳或失败。</summary>
    public void Delete(FileEntry entry)
    {
        if (!entry.IsDirectory)
        {
            DeleteFileCore(entry.FullPath);
            return;
        }

        foreach (var child in List(entry.FullPath))
        {
            Delete(child);
        }

        DeleteDirectoryCore(entry.FullPath);
    }

    public void Rename(FileEntry entry, string newName)
    {
        var parent = Path.GetDirectoryName(entry.FullPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(parent))
        {
            parent = "/";
        }

        RenameCore(entry.FullPath, CombineRemote(parent, newName));
    }

    /// <summary>拼接远程路径：统一使用正斜杠，避免根目录出现双斜杠。</summary>
    public static string CombineRemote(string directory, string name) =>
        directory.EndsWith('/') ? directory + name : $"{directory}/{name}";

    public abstract void Dispose();
}

/// <summary>按协议创建远程文件系统实例。</summary>
public static class RemoteFileSystemFactory
{
    public static RemoteFileSystem Create(ShellProfile profile, string protocolKey) =>
        string.Equals(protocolKey, SessionProtocol.Ftp, StringComparison.OrdinalIgnoreCase)
            ? new FtpSession(profile.Host ?? string.Empty, profile.Port, profile.Username ?? string.Empty, profile.Password ?? string.Empty)
            : new SftpSession(profile.Host ?? string.Empty, profile.Port, profile.Username ?? string.Empty, profile.Password ?? string.Empty);
}
