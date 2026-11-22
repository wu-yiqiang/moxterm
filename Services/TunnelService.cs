using Renci.SshNet;
using Renci.SshNet.Common;

namespace MoxTerm.Services;

/// <summary>隧道运行状态。</summary>
public enum TunnelState
{
    Stopped,
    Connecting,
    Running,
    Failed,
}

/// <summary>
/// 一条 SSH 隧道的运行时：建立 SSH 连接，并按类型启动本地 / 远程 / 动态端口转发。
/// <see cref="Start"/> 与 <see cref="Stop"/> 都是阻塞调用，调用方需放到后台线程执行。
/// </summary>
public sealed class TunnelRuntime : IDisposable
{
    private readonly TunnelProfile _profile;
    private SshClient? _client;
    private ForwardedPort? _port;
    private bool _disposed;

    /// <summary>正在主动断开，期间底层回调的异常不视为隧道故障。</summary>
    private volatile bool _stopping;

    public TunnelRuntime(TunnelProfile profile) => _profile = profile;

    public TunnelState State { get; private set; } = TunnelState.Stopped;

    /// <summary>最近一次失败的原因。</summary>
    public string? Error { get; private set; }

    /// <summary>状态变化；可能在后台线程上触发。</summary>
    public event EventHandler? StateChanged;

    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        StopCore();
        Error = null;
        SetState(TunnelState.Connecting);

        SshClient? client = null;
        try
        {
            var username = _profile.Username.Trim();
            client = new SshClient(new ConnectionInfo(
                _profile.SshHost.Trim(),
                _profile.SshPort is >= 1 and <= 65535 ? _profile.SshPort : 22,
                username,
                new PasswordAuthenticationMethod(username, SecretProtector.Unprotect(_profile.Password)))
            {
                Timeout = TimeSpan.FromSeconds(15),
            });
            client.KeepAliveInterval = TimeSpan.FromSeconds(30);
            client.ErrorOccurred += OnClientError;
            client.Connect();

            var port = CreatePort();
            port.Exception += OnPortError;
            client.AddForwardedPort(port);
            port.Start();

            _client = client;
            _port = port;
            SetState(TunnelState.Running);
        }
        catch (Exception ex)
        {
            if (client is not null)
            {
                client.ErrorOccurred -= OnClientError;
                try
                {
                    client.Dispose();
                }
                catch (Exception)
                {
                    // 连接失败后的释放失败无需再上报
                }
            }

            Error = Describe(ex);
            SetState(TunnelState.Failed);
        }
    }

    public void Stop()
    {
        if (_disposed || (State == TunnelState.Stopped && _client is null && _port is null))
        {
            return;
        }

        StopCore();
        Error = null;
        SetState(TunnelState.Stopped);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopCore();
        State = TunnelState.Stopped;
    }

    private ForwardedPort CreatePort()
    {
        var bindHost = string.IsNullOrWhiteSpace(_profile.BindHost) ? "127.0.0.1" : _profile.BindHost.Trim();
        var bindPort = (uint)Math.Clamp(_profile.BindPort, 1, 65535);
        var targetHost = string.IsNullOrWhiteSpace(_profile.TargetHost) ? "127.0.0.1" : _profile.TargetHost.Trim();
        var targetPort = (uint)Math.Clamp(_profile.TargetPort, 1, 65535);

        return _profile.Type switch
        {
            TunnelTypes.Remote => new ForwardedPortRemote(bindHost, bindPort, targetHost, targetPort),
            TunnelTypes.Dynamic => new ForwardedPortDynamic(bindHost, bindPort),
            _ => new ForwardedPortLocal(bindHost, bindPort, targetHost, targetPort),
        };
    }

    private void StopCore()
    {
        // 先停转发再断 SSH：取消远程转发需要走现有连接
        _stopping = true;
        try
        {
            var port = _port;
            _port = null;
            if (port is not null)
            {
                port.Exception -= OnPortError;
                try
                {
                    if (port.IsStarted)
                    {
                        port.Stop();
                    }
                }
                catch (Exception)
                {
                    // 连接已断开时停止转发会失败，忽略即可
                }

                try
                {
                    port.Dispose();
                }
                catch (Exception)
                {
                    // 释放失败不影响后续状态
                }
            }

            var client = _client;
            _client = null;
            if (client is not null)
            {
                client.ErrorOccurred -= OnClientError;
                try
                {
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                }
                catch (Exception)
                {
                    // 断开失败时直接释放
                }

                try
                {
                    client.Dispose();
                }
                catch (Exception)
                {
                    // 释放失败不影响后续状态
                }
            }
        }
        finally
        {
            _stopping = false;
        }
    }

    private void OnPortError(object? sender, ExceptionEventArgs e)
    {
        if (_stopping || _disposed)
        {
            return;
        }

        Error = Describe(e.Exception);
        SetState(TunnelState.Failed);
    }

    private void OnClientError(object? sender, ExceptionEventArgs e)
    {
        if (_stopping || _disposed)
        {
            return;
        }

        Error = Describe(e.Exception);
        SetState(TunnelState.Failed);
    }

    private static string Describe(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;

    private void SetState(TunnelState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
