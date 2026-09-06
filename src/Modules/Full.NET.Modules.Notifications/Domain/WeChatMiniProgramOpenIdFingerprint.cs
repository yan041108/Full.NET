using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Notifications.Domain;

/// <summary>为微信小程序 OpenId 生成稳定 SHA-256 指纹，用于唯一索引而不暴露原值。</summary>
internal static class WeChatMiniProgramOpenIdFingerprint
{
    /// <summary>返回小写十六进制 SHA-256 摘要。</summary>
    public static string Compute(string openId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(openId);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(openId.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
