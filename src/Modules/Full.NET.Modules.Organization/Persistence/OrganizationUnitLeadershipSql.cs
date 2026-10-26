using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Persistence;

/// <summary>机构领导权由 Organization 自有关系维护；变更先锁定机构，避免禁用或移动后沿用旧授权范围。</summary>
internal static class OrganizationUnitLeadershipSql
{
    private const string UnitProjection = "SELECT Id, IsActive, Version FROM fn_organization_unit";
    public static readonly SqlStatement LockUnitSqlServer = new("organization.unit_leadership.lock_unit.sqlserver",
        UnitProjection + " WITH (UPDLOCK,HOLDLOCK) WHERE Id=@UnitId AND TenantId=@TenantId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement LockUnitMySql = new("organization.unit_leadership.lock_unit.mysql",
        UnitProjection + " WHERE Id=@UnitId AND TenantId=@TenantId FOR UPDATE", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement Find = new("organization.unit_leadership.find", """
        SELECT Id, LeaderUserId, Revision, UpdatedById, UpdatedAtUtc
        FROM fn_organization_unit_leadership WHERE Id=@UnitId AND TenantId=@TenantId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement Insert = new("organization.unit_leadership.insert", """
        INSERT INTO fn_organization_unit_leadership (Id,TenantId,LeaderUserId,Revision,UpdatedById,UpdatedAtUtc)
        SELECT unit.Id,unit.TenantId,@LeaderUserId,1,@ActorUserId,@Now
        FROM fn_organization_unit AS unit WHERE unit.Id=@UnitId AND unit.TenantId=@TenantId AND unit.IsActive=1 AND unit.Version=@UnitVersion
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement Update = new("organization.unit_leadership.update", """
        UPDATE fn_organization_unit_leadership SET LeaderUserId=@LeaderUserId, Revision=Revision+1,UpdatedById=@ActorUserId,UpdatedAtUtc=@Now
        WHERE Id=@UnitId AND TenantId=@TenantId AND Revision=@Revision
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ReadSourceUnit = new("organization.unit_leadership.read_source_unit", """
        SELECT unit.Id,unit.ParentId,leadership.LeaderUserId
        FROM fn_organization_unit AS unit
        LEFT JOIN fn_organization_unit_leadership AS leadership ON leadership.Id=unit.Id AND leadership.TenantId=unit.TenantId
        WHERE unit.Id=@UnitId AND unit.TenantId=@TenantId AND unit.IsActive=1
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

/// <summary>锁定的机构状态及版本，只用于同模块本地事务。</summary>
internal sealed record OrganizationLeadershipUnitRecord(Guid Id, bool IsActive, int Version);
/// <summary>一对一的负责人权威配置，不从岗位排序或隶属人员重建。</summary>
internal sealed record OrganizationUnitLeadershipRecord(Guid Id, Guid? LeaderUserId, long Revision, Guid UpdatedById, DateTimeOffset UpdatedAtUtc);
/// <summary>有机构但无负责人属于正常无人；缺行表示来源无效。</summary>
internal sealed record OrganizationLeaderSourceUnitRecord(Guid Id, Guid? ParentId, Guid? LeaderUserId);
