using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Identity.OAuth;

/// <summary>PKCE 辅助方法。</summary>
internal static class OAuthPkce
{
    /// <summary>生成 PKCE code_verifier。</summary>
    /// <returns>URL 安全的 code_verifier 字符串。</returns>
    public static string CreateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    /// <summary>根据 code_verifier 计算 S256 code_challenge。</summary>
    /// <param name="codeVerifier">PKCE code_verifier。</param>
    /// <returns>URL 安全的 code_challenge 字符串。</returns>
    public static string CreateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
