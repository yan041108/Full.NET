namespace Full.NET.Modules.Organization.Contracts;

/// <summary>供其他模块判断租户机构成员关系的只读端口。</summary>
public interface ITenantOrganizationUserMembershipReader
{
    /// <summary>判断用户是否为指定租户机构单元的活动成员。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="organizationUnitId">机构单元标识。</param>
    /// <param name="userId">用户标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>活动成员时返回 <see langword="true"/>。</returns>
    Task<bool> IsActiveMemberAsync(
        Guid tenantId,
        Guid organizationUnitId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>列出用户当前全部活动机构隶属。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>租户与机构单元键集合。</returns>
    Task<IReadOnlyList<TenantOrganizationMembershipEntry>> ListActiveMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>统计多个机构受众目标的去重活动成员数。</summary>
    /// <param name="organizationTargets">机构受众目标集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>去重后的活动成员计数。</returns>
    Task<long> CountDistinctActiveMembersAsync(
        IReadOnlyList<TenantOrganizationMembershipTarget> organizationTargets,
        CancellationToken cancellationToken = default);
}

/// <summary>用户机构隶属只读投影。</summary>
/// <param name="TenantId">租户标识。</param>
/// <param name="OrganizationUnitId">机构单元标识。</param>
public sealed record TenantOrganizationMembershipEntry(
    Guid TenantId,
    Guid OrganizationUnitId);

/// <summary>机构受众统计目标。</summary>
/// <param name="TenantId">租户标识。</param>
/// <param name="OrganizationUnitId">机构单元标识。</param>
public sealed record TenantOrganizationMembershipTarget(
    Guid TenantId,
    Guid OrganizationUnitId);
