using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Identity.Features.AccountChallenges;

internal static class AccountChallengeCredentialHasher
{
    public static string Hash(Guid challengeId, string credential)
    {
        var payload = $"{challengeId:N}:{credential.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
