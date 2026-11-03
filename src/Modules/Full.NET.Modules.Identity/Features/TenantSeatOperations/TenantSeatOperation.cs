namespace Full.NET.Modules.Identity.Features.TenantSeatOperations;

/// <summary>Identity 自有的席位激活工作项；只保存可信身份、原状态及恢复阶段，不保存密码或邀请令牌。</summary>
internal sealed record TenantSeatOperation(
    Guid Id, string RequestKey, Guid TenantId, Guid UserId, Guid MemberId, Guid? InvitationId,
    string? PreviousRole, string? PreviousStatus, int MemberVersion, int? InvitationVersion,
    string Phase, string? RequestHash, string? ErrorCode, DateTimeOffset CreatedAtUtc,
    DateTimeOffset DueAtUtc, Guid? LeaseId, DateTimeOffset? LeaseUntilUtc, int Version, int Attempt,
    Guid? RegistrationWayId = null);

/// <summary>阶段是后台恢复权威；确认与成员激活分开记账，取消不得把已经消费的额度当预留释放。</summary>
internal static class TenantSeatPhases
{
    internal const string Requested = "Requested";
    internal const string Reserved = "Reserved";
    internal const string Confirmed = "Confirmed";
    internal const string Activated = "Activated";
    internal const string Cancelling = "Cancelling";
    internal const string Cancelled = "Cancelled";
}
