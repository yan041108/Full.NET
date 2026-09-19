using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>Identity 账号挑战邮件投递意图；明文凭据仅存在于受控投递边界。</summary>
public sealed record IdentityChallengeDeliveryIntent(
    Guid ChallengeId,
    IdentityAccountChallengePurpose Purpose,
    string NormalizedEmail,
    string Credential,
    DateTimeOffset ExpiresAtUtc,
    string IdempotencyKey);

/// <summary>仅供 Identity 调用的账号挑战投递 Port。</summary>
public interface IIdentityChallengeDeliveryPort
{
    Task<Result<bool>> SendAsync(
        IdentityChallengeDeliveryIntent intent,
        CancellationToken cancellationToken = default);
}
