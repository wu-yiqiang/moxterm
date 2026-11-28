using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;

namespace MoxTerm.Services;

/// <summary>单次 Ping 回复：成功时带地址、耗时与 TTL，失败时只带状态。</summary>
public sealed record PingReplyInfo(bool Success, string Address, int Bytes, long Milliseconds, int Ttl, string Status);

/// <summary>Ping 汇总统计。</summary>
public sealed record PingSummary(int Sent, int Received, int LossPercent, long Min, long Average, long Max);

/// <summary>网络诊断：Ping 与 Traceroute，供网络诊断页面调用。</summary>
public static class NetworkDiagnostics
{
    private const int PingCount = 4;
    private const int PingTimeoutMilliseconds = 2000;
    private const int PingBufferSize = 32;
    private const int PingIntervalMilliseconds = 400;

    /// <summary>连续发送 4 个 ICMP 回显请求，每次回复逐条回调。</summary>
    public static async Task<PingSummary> PingAsync(
        string host,
        Action<PingReplyInfo> onReply,
        CancellationToken token)
    {
        using var ping = new Ping();
        var buffer = new byte[PingBufferSize];
        var options = new PingOptions(64, true);
        var times = new List<long>();

        for (var i = 0; i < PingCount; i++)
        {
            var reply = await ping.SendPingAsync(
                host,
                TimeSpan.FromMilliseconds(PingTimeoutMilliseconds),
                buffer,
                options,
                token);

            if (reply.Status == IPStatus.Success)
            {
                times.Add(reply.RoundtripTime);
                onReply(new PingReplyInfo(true, reply.Address.ToString(), reply.Buffer.Length, reply.RoundtripTime, reply.Options?.Ttl ?? 0, nameof(IPStatus.Success)));
            }
            else
            {
                onReply(new PingReplyInfo(false, string.Empty, 0, 0, 0, reply.Status.ToString()));
            }

            if (i < PingCount - 1)
            {
                await Task.Delay(PingIntervalMilliseconds, token);
            }
        }

        return new PingSummary(
            PingCount,
            times.Count,
            (PingCount - times.Count) * 100 / PingCount,
            times.Count == 0 ? 0 : times.Min(),
            times.Count == 0 ? 0 : (long)times.Average(),
            times.Count == 0 ? 0 : times.Max());
    }

    /// <summary>
    /// 调用系统 tracert 输出逐跳路由，每读到一行回调一次。
    /// 先用 chcp 65001 把控制台切到 UTF-8，避免中文系统下输出乱码。
    /// </summary>
    public static async Task TracerouteAsync(string host, Action<string> onLine, CancellationToken token)
    {
        var startInfo = new ProcessStartInfo("cmd.exe", $"/c chcp 65001>nul & tracert -d -h 30 -w 1000 {host}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动路由追踪程序");

        // 停止时连同 cmd 与 tracert 子进程一起结束
        using var registration = token.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // 进程已退出，无需处理
            }
        });

        while (await process.StandardOutput.ReadLineAsync(token) is { } line)
        {
            onLine(line);
        }

        token.ThrowIfCancellationRequested();
        await process.WaitForExitAsync(token);
    }
}
