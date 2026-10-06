using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Identity.Features.AccountChallenges;

internal static class AccountChallengeCredentialHasher
{
    /// <summary>恢复摘要绑定受信用户标识；邮箱归属变化不能把已签发凭据授权给另一账号。</summary>
    public static string HashPasswordRecovery(Guid challengeId, Guid userId, string credential)
    {
        var payload = $"password-recovery:v1:{challengeId:N}:{userId:N}:{credential.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static string Hash(Guid challengeId, string credential)
    {
        var payload = $"{challengeId:N}:{credential.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
