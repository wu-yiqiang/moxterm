using System.Security.Cryptography;
using System.Text;

namespace MoxTerm.Services;

/// <summary>
/// 一条已保存的连接配置，覆盖所有已实现协议。
/// </summary>
/// <param name="Name">会话名称（标签页标题）。</param>
/// <param name="Protocol">协议标识，对应 <see cref="SessionProtocol.Key"/>。</param>
/// <param name="Host">主机名或 IP；串口会话不使用。</param>
/// <param name="Port">端口；串口会话不使用。</param>
/// <param name="Username">用户名，空表示由对端询问。</param>
/// <param name="Password">密码，经 <see cref="SecretProtector"/> 加密后的存储形态。</param>
/// <param name="Notes">备注。</param>
/// <param name="SerialPortName">串口名，如 <c>COM3</c>。</param>
/// <param name="BaudRate">串口波特率。</param>
public sealed record SavedSession(
    string Name,
    string Protocol,
    string Host,
    int Port,
    string Username,
    string Password,
    string Notes,
    string SerialPortName = "",
    int BaudRate = 0);

/// <summary>
/// 会话密码加解密：用 DPAPI（当前用户范围）保护，settings.json 里不落明文。
/// </summary>
public static class SecretProtector
{
    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return "";
        }

        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return "";
        }

        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // 密文损坏（如换了系统用户）时返回空，退回手动输入密码
            return "";
        }
    }
}
