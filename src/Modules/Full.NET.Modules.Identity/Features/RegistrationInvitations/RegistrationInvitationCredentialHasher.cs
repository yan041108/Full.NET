using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Identity.Features.RegistrationInvitations;

internal static class RegistrationInvitationCredentialHasher
{
    public static string Hash(Guid invitationId, string token) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes($"{invitationId:N}:{token.Trim()}")))
            .ToLowerInvariant();
}
