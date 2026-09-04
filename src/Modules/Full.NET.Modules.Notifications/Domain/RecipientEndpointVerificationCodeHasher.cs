using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Notifications.Domain;

/// <summary>
/// 将一次性验证码哈希为固定长度十六进制摘要；挑战行只保存哈希，禁止落库明文验证码。
/// </summary>
internal static class RecipientEndpointVerificationCodeHasher
{
    /// <summary>对挑战标识与验证码原文计算 SHA-256 十六进制摘要。</summary>
    /// <param name="challengeId">挑战逻辑主键，用作哈希盐边界。</param>
    /// <param name="code">用户提交的验证码原文。</param>
    /// <returns>64 字符小写十六进制摘要。</returns>
    public static string Hash(Guid challengeId, string code)
    {
        var payload = $"{challengeId:N}:{code.Trim()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
