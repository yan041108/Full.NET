// 账户挑战凭据哈希；与注册/找回流程共享，禁止明文落库。
using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Identity.Features.AccountChallenges;

/// <summary>账户挑战凭据哈希；与注册/找回流程共享，禁止明文落库。</summary>
internal static class AccountChallengeCredentialHasher
{
    public static string Hash(Guid challengeId, string credential)
    {
        var payload = $"{challengeId:N}:{credential.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
