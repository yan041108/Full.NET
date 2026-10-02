namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// Host 运维链路读取指定租户的活动成员数，用于 Tenancy 席位配额 UsedValue 基线对账。
/// </summary>
public interface ITenantActiveMemberCountPort
{
    /// <summary>统计租户内状态为 <see cref="TenantMemberStatuses.Active"/> 的成员数量。</summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>租户内状态为 Active 的成员数量；租户不存在时返回 0。</returns>
    Task<long> CountActiveMembersAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
