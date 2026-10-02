using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>Identity 账号挑战邮件投递意图；明文凭据仅存在于受控投递边界。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Credential 为明文一次性口令，仅允许存在于投递边界，禁止写入日志或缓存。</remarks>
/// <param name="ChallengeId">本次账号挑战的稳定标识，用于幂等与投递追踪。</param>
/// <param name="Purpose">挑战用途机器码，决定邮件文案与链接目标。</param>
/// <param name="NormalizedEmail">已规范化的目标邮箱地址；投递方不再二次校验格式。</param>
/// <param name="Credential">明文一次性口令或令牌；仅允许存在于当前投递边界。</param>
/// <param name="ExpiresAtUtc">凭据过期时间（UTC）；过期后投递仍可发出但链接无效。</param>
/// <param name="IdempotencyKey">投递幂等键；相同键重复发送应合并或拒绝，避免重复邮件。</param>
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
    /// <summary>按意图投递账号挑战邮件；实现方负责凭据隔离与失败重试。</summary>
    /// <param name="intent">账号挑战投递意图，含明文一次性凭据。</param>
    /// <param name="cancellationToken">用于取消投递操作的令牌。</param>
    /// <returns>成功时为 true；失败时返回携带稳定错误码的结果。</returns>
    Task<Result<bool>> SendAsync(
        IdentityChallengeDeliveryIntent intent,
        CancellationToken cancellationToken = default);
}
