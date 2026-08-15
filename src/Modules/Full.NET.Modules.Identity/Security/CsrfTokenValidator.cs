using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// Double Submit Cookie 模式 CSRF 校验工具。对 Cookie 与 Header 各自做 SHA-256 哈希后，
/// 使用 CryptographicOperations.FixedTimeEquals 进行固定时间比较，
/// 避免时序差异泄漏真实 Token 值。不生成 Token——Token 由登录/刷新流程通过
/// CryptographicTokenGenerator 产生并随同 RefreshToken 下发。
/// </summary>
internal static class CsrfTokenValidator
{
    /// <summary>
    /// 校验客户端提交的 Cookie Token 与 Header Token 是否匹配（双提交模式）。
    /// </summary>
    /// <param name="cookieToken">从 CSRF Cookie 读取的值。</param>
    /// <param name="headerToken">从 X-CSRF-Token 等头部读取的值。</param>
    public static bool IsValid(string? cookieToken, string? headerToken)
    {
        if (string.IsNullOrEmpty(cookieToken) || string.IsNullOrEmpty(headerToken))
        {
            return false;
        }

        var cookieHash = SHA256.HashData(Encoding.UTF8.GetBytes(cookieToken));
        var headerHash = SHA256.HashData(Encoding.UTF8.GetBytes(headerToken));
        return CryptographicOperations.FixedTimeEquals(cookieHash, headerHash);
    }
}
