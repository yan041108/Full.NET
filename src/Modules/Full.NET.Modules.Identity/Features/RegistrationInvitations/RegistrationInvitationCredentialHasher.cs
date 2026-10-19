// 注册邀请凭据哈希；邀请令牌校验与一次性消费语义。
using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Identity.Features.RegistrationInvitations;

/// <summary>注册邀请凭据哈希；邀请令牌校验与一次性消费语义。</summary>
internal static class RegistrationInvitationCredentialHasher
{
    public static string Hash(Guid invitationId, string token) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes($"{invitationId:N}:{token.Trim()}")))
            .ToLowerInvariant();
}
