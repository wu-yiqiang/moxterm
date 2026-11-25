using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace MoxTerm.Services;

/// <summary>
/// 通过系统远程桌面客户端（mstsc）建立 RDP 连接。
/// 生成临时 .rdp 配置，按需开启剪贴板与本地驱动器重定向，然后交给 mstsc 打开独立窗口。
/// </summary>
public static class RdpLauncher
{
    /// <summary>
    /// 启动 RDP 会话。<paramref name="drives"/> 为 <c>"*"</c>（全部驱动器）、
    /// 分号分隔的盘符（如 <c>"C;D"</c>）或空字符串（不共享驱动器）。
    /// 失败时返回 <c>false</c> 并给出错误信息。
    /// </summary>
    public static bool TryLaunch(
        string name,
        string host,
        int port,
        string? username,
        bool shareClipboard,
        string? drives,
        out string error)
    {
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "MoxTerm", "rdp");
            Directory.CreateDirectory(directory);

            // 同名会话复用同一个文件，避免临时目录里堆积大量 .rdp
            var file = Path.Combine(directory, SafeFileName(name) + ".rdp");
            File.WriteAllText(file, BuildContent(host, port, username, shareClipboard, drives), new UnicodeEncoding(false, true));

            var startInfo = new ProcessStartInfo("mstsc.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add(file);
            Process.Start(startInfo);

            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string BuildContent(string host, int port, string? username, bool shareClipboard, string? drives)
    {
        var builder = new StringBuilder();
        builder.AppendLine("screen mode id:i:2");
        builder.AppendLine("use multimon:i:1");
        builder.AppendLine("session bpp:i:32");
        builder.AppendLine("compression:i:1");
        builder.AppendLine("networkautodetect:i:1");
        builder.AppendLine("bandwidthautodetect:i:1");
        builder.AppendLine($"full address:s:{host}:{port}");
        builder.AppendLine("prompt for credentials:i:1");
        builder.AppendLine("authentication level:i:2");
        builder.AppendLine("audiomode:i:0");
        builder.AppendLine("redirectprinters:i:0");
        builder.AppendLine("redirectcomports:i:0");
        builder.AppendLine("redirectsmartcards:i:0");
        builder.AppendLine($"redirectclipboard:i:{(shareClipboard ? 1 : 0)}");

        if (!string.IsNullOrWhiteSpace(username))
        {
            builder.AppendLine($"username:s:{username.Trim()}");
        }

        var driveSpec = BuildDriveSpec(drives);
        if (driveSpec.Length > 0)
        {
            builder.AppendLine($"drivestoredirect:s:{driveSpec}");
        }

        return builder.ToString();
    }

    /// <summary>把 <c>"C;D"</c> 或 <c>"*"</c> 转成 mstsc 要求的 <c>C:\;D:\;</c> 形式。</summary>
    private static string BuildDriveSpec(string? drives)
    {
        if (string.IsNullOrWhiteSpace(drives))
        {
            return "";
        }

        if (drives.Trim() == "*")
        {
            return "*";
        }

        var builder = new StringBuilder();
        foreach (var item in drives.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var letter = item.TrimEnd(':').ToUpperInvariant();
            if (letter.Length == 1 && letter[0] is >= 'A' and <= 'Z')
            {
                builder.Append(letter).Append(":\\;");
            }
        }

        return builder.ToString();
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);

        foreach (var ch in name)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? "rdp" : result;
    }
}
