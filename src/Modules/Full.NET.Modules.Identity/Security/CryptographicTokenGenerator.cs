using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// IRandomTokenGenerator 的加密安全随机实现。基于 RandomNumberGenerator.GetBytes
/// 输出 Base64Url 编码字符串；最小长度 16 bytes（128 bit）用于防止可预测的暴力枚举，
/// 适用于 Refresh Token、CSRF Token 与 API Key Secret。
/// </summary>
internal sealed class CryptographicTokenGenerator : IRandomTokenGenerator
{
    /// <summary>
    /// 生成指定字节长度的加密安全随机令牌并输出为 URL 安全的 Base64 字符串。
    /// </summary>
    /// <param name="byteCount">随机字节数；不得小于 16。</param>
    public string Generate(int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(byteCount, 16);
        return Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(byteCount));
    }
}
