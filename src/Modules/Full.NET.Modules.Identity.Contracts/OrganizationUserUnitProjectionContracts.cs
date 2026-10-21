using global::MemoryPack;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>机构隶属的可靠投影事件类型。</summary>
public static class IdentityOrganizationUserUnitEventTypes
{
    /// <summary>机构隶属版本快照已提交。</summary>
    public const string Changed = "fullnet.organization.user_unit.changed";
}

/// <summary>用于管理列表筛选的隶属快照；不能作为授权依据。</summary>
/// <param name="TenantId">所属租户。</param>
/// <param name="AssignmentId">隶属标识。</param>
/// <param name="UserId">用户标识。</param>
/// <param name="UnitId">部门标识。</param>
/// <param name="IsActive">是否启用。</param>
/// <param name="Version">单调源版本。</param>
/// <param name="ChangedAtUtc">源更新时间。</param>
[MemoryPackable]
public partial record IdentityOrganizationUserUnitChangedIntegrationEvent(
    Guid TenantId, Guid AssignmentId, Guid UserId, Guid UnitId,
    bool IsActive, long Version, DateTimeOffset ChangedAtUtc);
