using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Features.RegistrationAssignments;

/// <summary>组织分配意图仅存储本模块状态；外部成员版本由事务外目录核对。</summary>
internal static class RegistrationAssignmentSql
{
    private const string Columns = "Id,TenantId,MemberId,MemberVersion,UserId,UnitId,PositionId,Status,ErrorCode,CreatedAtUtc,DueAtUtc,Version";
    internal static readonly SqlStatement Enlist = new("organization.registration_assignment.enlist", $"""
        INSERT INTO fn_organization_registration_assignment ({Columns})
        SELECT @Id,@TenantId,@MemberId,@MemberVersion,@UserId,@UnitId,@PositionId,'Pending',NULL,@Now,@Now,1
        WHERE NOT EXISTS (SELECT 1 FROM fn_organization_registration_assignment WHERE Id=@Id AND TenantId=@TenantId)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Find = new("organization.registration_assignment.find", $"""
        SELECT {Columns} FROM fn_organization_registration_assignment WHERE Id=@Id AND TenantId=@TenantId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement DueSqlServer = new("organization.registration_assignment.due.sql_server", $"""
        SELECT TOP (20) {Columns} FROM fn_organization_registration_assignment
        WHERE Status='Pending' AND DueAtUtc<=@Now ORDER BY DueAtUtc,Id
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement DueMySql = new("organization.registration_assignment.due.my_sql", $"""
        SELECT {Columns} FROM fn_organization_registration_assignment
        WHERE Status='Pending' AND DueAtUtc<=@Now ORDER BY DueAtUtc,Id LIMIT 20
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Finish = new("organization.registration_assignment.finish", """
        UPDATE fn_organization_registration_assignment SET Status=@Status,ErrorCode=@ErrorCode,Version=Version+1
        WHERE TenantId=@TenantId AND Id=@Id AND Status='Pending' AND Version=@Version AND DueAtUtc<=@Now
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Retry = new("organization.registration_assignment.retry", """
        UPDATE fn_organization_registration_assignment SET ErrorCode=@ErrorCode,DueAtUtc=@DueAtUtc,Version=Version+1
        WHERE TenantId=@TenantId AND Id=@Id AND Status='Pending' AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

/// <summary>与显式投影列序一致的本地恢复记录。</summary>
internal sealed record RegistrationAssignmentRecord(Guid Id, Guid TenantId, Guid MemberId, int MemberVersion,
    Guid UserId, Guid UnitId, Guid? PositionId, string Status, string? ErrorCode,
    DateTimeOffset CreatedAtUtc, DateTimeOffset DueAtUtc, long Version)
{
    // 为 MySQL 的 datetime 与驱动数值类型保留逐属性物化路径。
    public RegistrationAssignmentRecord() : this(Guid.Empty, Guid.Empty, Guid.Empty, 0, Guid.Empty,
        Guid.Empty, null, string.Empty, null, default, default, 0) { }
}
