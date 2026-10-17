using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>Identity 账号挑战邮件投递意图；明文凭据仅存在于受控投递边界。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Credential 为明文一次性口令，仅允许存在于投递边界，禁止写入日志或缓存。</remarks>
/// <param name="ChallengeId">本次账号挑战的稳定标识，用于幂等与投递追踪。</param>
/// <param name="Purpose">挑战用途机器码，决定邮件文案与链接目标。</param>
/// <param name="NormalizedEmail">已规范化的目标裸单邮箱地址；投递方仍检查格式，拒绝显示名、注释、控制字符和批量地址。</param>
/// <param name="Credential">明文一次性口令或令牌；仅允许存在于当前投递边界。</param>
/// <param name="ExpiresAtUtc">凭据过期时间（UTC）；已过期的意图不得开始投递，已经开始的 SMTP 调用不因过期强行中断。</param>
/// <param name="IdempotencyKey">非空且无控制字符的稳定投递键；SMTP 用于生成稳定 Message-Id，但不保证服务器去重。</param>
public sealed record IdentityChallengeDeliveryIntent(
    Guid ChallengeId,
    IdentityAccountChallengePurpose Purpose,
    string NormalizedEmail,
    string Credential,
    DateTimeOffset ExpiresAtUtc,
    string IdempotencyKey)
{
    /// <summary>诊断文本只保留挑战标识与用途，避免 record 默认格式化泄露凭据和收件信息。</summary>
    public override string ToString() =>
        $"IdentityChallengeDeliveryIntent {{ ChallengeId = {ChallengeId}, Purpose = {Purpose}, Credential = [redacted] }}";
}

/// <summary>仅供 Identity 调用的账号挑战投递 Port。</summary>
public interface IIdentityChallengeDeliveryPort
{
    /// <summary>按意图投递账号挑战邮件；实现方负责凭据隔离和投递前检查，失败后的重发策略由调用方决定。</summary>
    /// <param name="intent">账号挑战投递意图，含明文一次性凭据。</param>
    /// <param name="cancellationToken">用于取消投递操作的令牌。</param>
    /// <returns>成功时为 true；失败时返回携带稳定错误码的结果。</returns>
    Task<Result<bool>> SendAsync(
        IdentityChallengeDeliveryIntent intent,
        CancellationToken cancellationToken = default);
}

/// <summary>挑战外部投递结果；受理不能证明收件人已收到或验证邮箱。</summary>
public enum IdentityChallengeDeliveryOutcome
{
    /// <summary>提供程序明确受理。</summary>
    Accepted,
    /// <summary>本次未受理或明确拒收；重发必须申请新挑战。</summary>
    Rejected,
    /// <summary>无法确认外部结果；禁止自动重发。</summary>
    Unknown,
}

/// <summary>可选的精确结果扩展，保留既有布尔 Port 实现的兼容性。</summary>
public interface IIdentityChallengeDeliveryOutcomePort : IIdentityChallengeDeliveryPort
{
    /// <summary>投递一次并区分明确受理、拒收和未知结果。</summary>
    /// <param name="intent">受信 Identity 创建的固定挑战意图。</param>
    /// <param name="cancellationToken">取消令牌，调用方取消保持传播。</param>
    /// <returns>不携带凭据、地址或提供程序原始异常的投递结果。</returns>
    Task<IdentityChallengeDeliveryOutcome> SendWithOutcomeAsync(
        IdentityChallengeDeliveryIntent intent, CancellationToken cancellationToken = default);
}
