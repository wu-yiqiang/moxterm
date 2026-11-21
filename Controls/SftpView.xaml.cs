using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MoxTerm.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace MoxTerm.Controls;

/// <summary>
/// 远程文件面板（SFTP / FTP 共用）：左侧本地目录、右侧远程目录，支持进入目录、上传、下载、
/// 新建文件夹、重命名与删除。所有网络操作在后台线程执行，避免阻塞界面。
/// </summary>
public sealed partial class SftpView : UserControl
{
    private RemoteFileSystem? _session;
    private bool _busy;
    private bool _disposed;
    private string _localPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _remotePath = "/";

    /// <summary>正在从本地列表拖出的条目（应用内拖拽，松开即上传）。</summary>
    private IReadOnlyList<FileEntry>? _draggedLocalItems;

    /// <summary>连接参数，由调用方在加入可视树之前赋值。</summary>
    public ShellProfile Profile { get; set; } = new("SFTP", "");

    /// <summary>远程协议标识：<c>sftp</c>（默认）或 <c>ftp</c>。</summary>
    public string ProtocolKey { get; set; } = SessionProtocol.Sftp;

    /// <summary>界面文案源。</summary>
    private Loc Loc => Loc.Instance;

    public SftpView()
    {
        InitializeComponent();

        LocalPathBox.Text = _localPath;
        RemotePathBox.Text = _remotePath;

        // 整个面板都是拖放区：子控件（路径文本框、列表）即使把拖放事件标记为已处理，
        // 这里仍然会收到，避免用户没正好落在列表上就“拖不进去”。
        AddHandler(DragOverEvent, new DragEventHandler(OnRemoteDragOver), true);
        AddHandler(DropEvent, new DragEventHandler(OnRemoteDrop), true);

        // 两侧列表都支持鼠标拖框多选；本地列表还可以整行拖出到右侧上传
        _ = new RubberBandSelector(LocalList, LocalBandLayer, dragItems: true);
        _ = new RubberBandSelector(RemoteList, RemoteBandLayer);

        Loaded += OnLoaded;
    }

    /// <summary>关闭标签页时断开连接。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session?.Dispose();
        _session = null;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_session is not null || _disposed)
        {
            return;
        }

        SetBusy(true);
        SetStatus(Loc.SftpConnecting);

        try
        {
            var session = RemoteFileSystemFactory.Create(Profile, ProtocolKey);

            await Task.Run(session.Connect);

            if (_disposed)
            {
                session.Dispose();
                return;
            }

            _session = session;
            _remotePath = session.HomePath;

            await RefreshLocalAsync();
            await RefreshRemoteAsync();
            SetStatus(string.Empty);
        }
        catch (Exception ex) when (ex is Renci.SshNet.Common.SshAuthenticationException or FluentFTP.Exceptions.FtpAuthenticationException)
        {
            // 服务器拒绝认证：通常是保存的密码已过期，需要编辑该连接重新输入
            SetStatus(Loc.SftpAuthFailed);
        }
        catch (Exception ex)
        {
            SetStatus($"{Loc.SftpConnectFailed} {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------- 刷新与导航 ----------

    private async Task RefreshLocalAsync()
    {
        try
        {
            var path = _localPath;
            var entries = await Task.Run(() => ListLocal(path));

            if (_disposed)
            {
                return;
            }

            LocalList.ItemsSource = entries;
            LocalPathBox.Text = path;
        }
        catch (Exception ex)
        {
            SetStatus($"{Loc.SftpFailed} {ex.Message}");
        }
    }

    private async Task RefreshRemoteAsync()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        try
        {
            var path = _remotePath;
            var entries = await Task.Run(() => session.List(path));

            if (_disposed)
            {
                return;
            }

            RemoteList.ItemsSource = entries;
            RemotePathBox.Text = path;
        }
        catch (Exception ex)
        {
            SetStatus($"{Loc.SftpFailed} {ex.Message}");
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        await RefreshLocalAsync();
        await RefreshRemoteAsync();
        SetBusy(false);
    }

    private async void OnLocalPathKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        var path = LocalPathBox.Text.Trim();
        if (!Directory.Exists(path))
        {
            SetStatus(Loc.SftpPathNotFound);
            return;
        }

        _localPath = path;
        await RefreshLocalAsync();
    }

    private async void OnRemotePathKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var session = _session;
        if (e.Key != VirtualKey.Enter || session is null)
        {
            return;
        }

        var path = RemotePathBox.Text.Trim();
        if (path.Length == 0)
        {
            return;
        }

        _remotePath = path;
        await RefreshRemoteAsync();
    }

    private async void OnLocalUpClick(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_localPath);
        if (parent is null)
        {
            return;
        }

        _localPath = parent.FullName;
        await RefreshLocalAsync();
    }

    private async void OnRemoteUpClick(object sender, RoutedEventArgs e)
    {
        if (_remotePath is "" or "/")
        {
            return;
        }

        var trimmed = _remotePath.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        _remotePath = index <= 0 ? "/" : trimmed[..index];
        await RefreshRemoteAsync();
    }

    private async void OnLocalDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (GetEntry(e.OriginalSource) is not { IsDirectory: true } entry)
        {
            return;
        }

        _localPath = entry.FullPath;
        await RefreshLocalAsync();
    }

    private async void OnRemoteDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (GetEntry(e.OriginalSource) is not { IsDirectory: true } entry || _session is null)
        {
            return;
        }

        _remotePath = entry.FullPath;
        await RefreshRemoteAsync();
    }

    // ---------- 文件操作 ----------

    private async void OnUploadClick(object sender, RoutedEventArgs e)
    {
        var items = LocalList.SelectedItems.OfType<FileEntry>().ToList();
        if (items.Count == 0)
        {
            SetStatus(Loc.SftpNoFileSelected);
            return;
        }

        await UploadAsync(items);
    }

    /// <summary>把本地文件/文件夹上传到当前远程目录（工具栏按钮与拖放共用）。</summary>
    private async Task UploadAsync(IReadOnlyList<FileEntry> items)
    {
        var session = _session;
        if (session is null || items.Count == 0 || _busy)
        {
            return;
        }

        var remoteDirectory = _remotePath;
        var currentTotal = 0UL;
        IProgress<ulong> byteProgress = new Progress<ulong>(sent => UpdateProgress(sent, currentTotal));
        IProgress<(string Name, long Size)> fileProgress = new Progress<(string Name, long Size)>(info =>
        {
            SetStatus($"{Loc.SftpUploading} {info.Name}");
            currentTotal = (ulong)Math.Max(info.Size, 0);
            TransferBar.Value = 0;
            TransferBar.IsIndeterminate = currentTotal == 0;
        });

        SetBusy(true);
        SetTransferActive(true);

        try
        {
            foreach (var entry in items)
            {
                var captured = entry;

                if (captured.IsDirectory)
                {
                    // 文件夹递归上传：自动创建远程子目录并逐个传其中的文件
                    SetStatus($"{Loc.SftpUploading} {captured.Name}");
                    await Task.Run(() => session.UploadDirectory(
                        captured.FullPath,
                        remoteDirectory,
                        (name, size) => fileProgress.Report((name, size)),
                        byteProgress.Report));
                }
                else
                {
                    SetStatus($"{Loc.SftpUploading} {captured.Name}");
                    currentTotal = (ulong)Math.Max(captured.Size, 0);

                    await Task.Run(() => session.Upload(captured.FullPath, remoteDirectory, byteProgress.Report));
                }
            }

            await RefreshRemoteAsync();
            SetStatus(string.Empty);
        }
        catch (Exception ex)
        {
            SetStatus($"{Loc.SftpFailed} {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            SetTransferActive(false);
        }
    }

    // ---------- 拖放上传 ----------

    /// <summary>本地列表开始拖出条目：记录待上传项，并给拖拽数据附上路径文本。</summary>
    private void OnLocalDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var entries = e.Items.OfType<FileEntry>().ToList();
        if (entries.Count == 0 || _session is null)
        {
            e.Cancel = true;
            return;
        }

        _draggedLocalItems = entries;
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.SetText(string.Join(Environment.NewLine, entries.Select(entry => entry.FullPath)));
    }

    private void OnLocalDragItemsCompleted(object sender, DragItemsCompletedEventArgs e) => _draggedLocalItems = null;

    /// <summary>把资源管理器或本地列表里的文件/文件夹拖入时显示“复制”效果。</summary>
    private void OnRemoteDragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        var hasStorageItems = e.DataView.Contains(StandardDataFormats.StorageItems);
        if (_session is null || _busy || (!hasStorageItems && _draggedLocalItems is null))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = Loc.SftpDropToUpload;
        e.DragUIOverride.IsCaptionVisible = true;
        e.Handled = true;
    }

    /// <summary>释放后把拖入的文件/文件夹上传到当前远程目录。</summary>
    private async void OnRemoteDrop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        var dragged = _draggedLocalItems;
        var hasStorageItems = e.DataView.Contains(StandardDataFormats.StorageItems);

        if (_session is null || (dragged is null && !hasStorageItems))
        {
            return;
        }

        e.Handled = true;

        if (_busy)
        {
            SetStatus(Loc.SftpBusy);
            return;
        }

        // 从本地列表拖过来：直接用拖拽开始时记下的条目
        if (dragged is { Count: > 0 })
        {
            await UploadAsync(dragged);
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var entries = new List<FileEntry>();

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Path))
            {
                continue;
            }

            if (item is StorageFolder folder)
            {
                entries.Add(new FileEntry(folder.Name, folder.Path, true, 0, DateTime.Now));
            }
            else if (item is StorageFile file)
            {
                long size = 0;
                try
                {
                    size = new FileInfo(file.Path).Length;
                }
                catch (Exception)
                {
                    // 无法读取大小时按未知处理，进度条使用不确定状态
                }

                entries.Add(new FileEntry(file.Name, file.Path, false, size, DateTime.Now));
            }
        }

        await UploadAsync(entries);
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        var files = RemoteList.SelectedItems.OfType<FileEntry>().Where(entry => !entry.IsDirectory).ToList();
        if (files.Count == 0)
        {
            SetStatus(Loc.SftpNoFileSelected);
            return;
        }

        var localDirectory = _localPath;
        SetBusy(true);
        SetTransferActive(true);

        try
        {
            foreach (var file in files)
            {
                SetStatus($"{Loc.SftpDownloading} {file.Name}");

                var total = (ulong)Math.Max(file.Size, 0);
                IProgress<ulong> progress = new Progress<ulong>(received => UpdateProgress(received, total));
                var captured = file;

                await Task.Run(() => session.Download(captured.FullPath, localDirectory, progress.Report));
            }

            await RefreshLocalAsync();
            SetStatus(string.Empty);
        }
        catch (Exception ex)
        {
            SetStatus($"{Loc.SftpFailed} {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            SetTransferActive(false);
        }
    }

    private async void OnNewFolderClick(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        var name = await PromptTextAsync(Loc.SftpNewFolder, Loc.SftpFolderName, string.Empty, Loc.SftpCreate);
        if (name is null)
        {
            return;
        }

        if (!IsValidName(name))
        {
            SetStatus(Loc.SftpInvalidName);
            return;
        }

        var path = RemoteFileSystem.CombineRemote(_remotePath, name);
        SetBusy(true);

        try
        {
            await Task.Run(() => session.CreateDirectory(path));
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"{Loc.SftpFailed} {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        if (RemoteList.SelectedItems.OfType<FileEntry>().ToList() is not { Count: 1 } selected)
        {
            SetStatus(Loc.SftpSelectOne);
            return;
        }

        var entry = selected[0];
        var name = await PromptTextAsync(Loc.SftpRename, Loc.SftpNewName, entry.Name, Loc.SaveChanges);
        if (name is null || name == entry.Name)
        {
            return;
        }

        if (!IsValidName(name))
        {
            SetStatus(Loc.SftpInvalidName);
            return;
        }

        SetBusy(true);

        try
        {
            await Task.Run(() => session.Rename(entry, name));
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"{Loc.SftpFailed} {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        var entries = RemoteList.SelectedItems.OfType<FileEntry>().ToList();
        if (entries.Count == 0)
        {
            SetStatus(Loc.SftpSelectOne);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = Loc.DeleteSession,
            Content = $"{Loc.SftpDeleteConfirm}\n{string.Join('\n', entries.Select(entry => entry.DisplayName))}",
            PrimaryButtonText = Loc.DeleteSession,
            CloseButtonText = Loc.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        dialog.Opened += (dialogSender, dialogArgs) => DialogHelper.SwapButtons(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        SetBusy(true);

        try
        {
            foreach (var entry in entries)
            {
                SetStatus($"{Loc.DeleteSession} {entry.DisplayName}");
                var captured = entry;
                await Task.Run(() => session.Delete(captured));
            }

            await RefreshRemoteAsync();
            SetStatus(string.Empty);
        }
        catch (Exception ex)
        {
            SetStatus($"{Loc.SftpFailed} {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------- 界面状态 ----------

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void SetStatus(string text) => StatusText.Text = text;

    private void SetBusy(bool busy)
    {
        _busy = busy;
        TransferBar.IsIndeterminate = busy;
        TransferBar.Value = 0;
        UpdateButtons();
    }

    /// <summary>进度条只在上传/下载期间出现，其它操作（连接、刷新、删除等）不显示。</summary>
    private void SetTransferActive(bool active)
    {
        TransferBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

        if (!active)
        {
            TransferBar.Value = 0;
        }
    }

    private void UpdateButtons()
    {
        var connected = _session is not null && !_busy;
        var localSelected = LocalList.SelectedItems.Count > 0;
        var remoteFiles = RemoteList.SelectedItems.OfType<FileEntry>().Any(entry => !entry.IsDirectory);
        var remoteCount = RemoteList.SelectedItems.Count;

        UploadButton.IsEnabled = connected && localSelected;
        DownloadButton.IsEnabled = connected && remoteFiles;
        NewFolderButton.IsEnabled = connected;
        RenameButton.IsEnabled = connected && remoteCount == 1;
        DeleteButton.IsEnabled = connected && remoteCount > 0;
        RefreshButton.IsEnabled = connected;
    }

    private void UpdateProgress(ulong done, ulong total)
    {
        if (_disposed)
        {
            return;
        }

        if (total == 0)
        {
            TransferBar.IsIndeterminate = true;
            return;
        }

        TransferBar.IsIndeterminate = false;
        TransferBar.Value = Math.Min(100d, done * 100d / total);
    }

    // ---------- 辅助方法 ----------

    private static FileEntry? GetEntry(object source) =>
        source is FrameworkElement element ? element.DataContext as FileEntry : null;

    /// <summary>名称不能为空，也不能包含路径分隔符。</summary>
    private static bool IsValidName(string name) =>
        name.Length > 0 && name.IndexOfAny(['/', '\\']) < 0;

    private static IReadOnlyList<FileEntry> ListLocal(string path)
    {
        var directory = new DirectoryInfo(path);
        var entries = new List<FileEntry>();

        foreach (var info in directory.EnumerateFileSystemInfos())
        {
            var isDirectory = (info.Attributes & System.IO.FileAttributes.Directory) != 0;
            entries.Add(new FileEntry(
                info.Name,
                info.FullName,
                isDirectory,
                isDirectory ? 0 : ((FileInfo)info).Length,
                info.LastWriteTime));
        }

        return entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>单行文本输入对话框；取消时返回 <c>null</c>。</summary>
    private async Task<string?> PromptTextAsync(string title, string placeholder, string initial, string primaryText)
    {
        var input = new TextBox
        {
            Text = initial,
            PlaceholderText = placeholder,
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = input,
            PrimaryButtonText = primaryText,
            CloseButtonText = Loc.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        dialog.Opened += (dialogSender, dialogArgs) =>
        {
            input.Focus(FocusState.Programmatic);
            input.SelectAll();
            DialogHelper.SwapButtons(dialog);
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text.Trim() : null;
    }
}
