using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Persistence;

/// <summary>租户汇报关系写入先取得自有门闩；同一门闩下检查路径并更新，防止并发生成汇报环。</summary>
internal static class OrganizationReportingLineSql
{
    public static readonly SqlStatement EnsureGuardSqlServer = new("organization.reporting_lines.ensure_guard.sqlserver", """
        IF NOT EXISTS (SELECT 1 FROM fn_organization_reporting_guard WITH (UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId)
            INSERT INTO fn_organization_reporting_guard (Id,TenantId) VALUES (@GuardId,@TenantId);
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement EnsureGuardMySql = new("organization.reporting_lines.ensure_guard.mysql", """
        INSERT INTO fn_organization_reporting_guard (Id,TenantId) VALUES (@GuardId,@TenantId)
        ON DUPLICATE KEY UPDATE Id=Id
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement LockGuardSqlServer = new("organization.reporting_lines.lock_guard.sqlserver", """
        SELECT Id FROM fn_organization_reporting_guard WITH (UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement LockGuardMySql = new("organization.reporting_lines.lock_guard.mysql", """
        SELECT Id FROM fn_organization_reporting_guard WHERE TenantId=@TenantId FOR UPDATE
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement Find = new("organization.reporting_lines.find", """
        SELECT Id,EmployeeUserId,ManagerUserId,Revision,UpdatedById,UpdatedAtUtc
        FROM fn_organization_reporting_line WHERE TenantId=@TenantId AND EmployeeUserId=@EmployeeUserId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement Insert = new("organization.reporting_lines.insert", """
        INSERT INTO fn_organization_reporting_line (Id,TenantId,EmployeeUserId,ManagerUserId,Revision,UpdatedById,UpdatedAtUtc)
        VALUES (@Id,@TenantId,@EmployeeUserId,@ManagerUserId,1,@ActorUserId,@Now)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement Update = new("organization.reporting_lines.update", """
        UPDATE fn_organization_reporting_line SET ManagerUserId=@ManagerUserId,Revision=Revision+1,UpdatedById=@ActorUserId,UpdatedAtUtc=@Now
        WHERE TenantId=@TenantId AND EmployeeUserId=@EmployeeUserId AND Revision=@Revision
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

/// <summary>自有汇报关系事实；身份名称和活动状态不在此重复存储。</summary>
internal sealed record OrganizationReportingLineRecord(Guid Id, Guid EmployeeUserId, Guid? ManagerUserId, long Revision, Guid UpdatedById, DateTimeOffset UpdatedAtUtc);
