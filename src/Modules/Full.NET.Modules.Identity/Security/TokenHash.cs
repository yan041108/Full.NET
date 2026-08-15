using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// Refresh Token 与 API Key 的单向哈希计算工具。使用 SHA-256 + 小写十六进制输出，
/// 使数据库中不落任何明文敏感令牌；比较时需先对客户端提交的原始 token
/// 做同算法 Compute 后再与存储值进行固定时间相等比较（由 SQL 层或调用方负责）。
/// </summary>
internal static class TokenHash
{
    /// <summary>
    /// 计算给定令牌的 SHA-256 小写十六进制哈希。
    /// </summary>
    /// <param name="token">原始明文令牌；不能为空或空白。</param>
    public static string Compute(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
