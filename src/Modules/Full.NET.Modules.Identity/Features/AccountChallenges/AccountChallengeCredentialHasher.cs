using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Identity.Features.AccountChallenges;

internal static class AccountChallengeCredentialHasher
{
    /// <summary>恢复摘要同时绑定账号和签发时安全戳；改密或禁用后的旧凭据不能复活。</summary>
    public static string HashPasswordRecovery(Guid challengeId, Guid userId, string securityStamp, string credential)
    {
        var payload = $"password-recovery:v2:{challengeId:N}:{userId:N}:{securityStamp.Length}:{securityStamp}:{credential.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static string Hash(Guid challengeId, string credential)
    {
        var payload = $"{challengeId:N}:{credential.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
