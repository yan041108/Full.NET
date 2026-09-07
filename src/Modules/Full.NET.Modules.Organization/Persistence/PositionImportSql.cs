using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Persistence;

/// <summary>岗位导入成功回执归 Organization 所有，任务标识只是跨模块关联值，禁止跨模块外键。</summary>
internal static class PositionImportSql
{
    internal static readonly SqlStatement Find = new("organization.position_import.find", """
        SELECT PositionId, PayloadHash FROM fn_organization_position_import
        WHERE TenantId = @TenantId AND TaskId = @TaskId AND LineNumber = @LineNumber
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement Insert = new("organization.position_import.insert", """
        INSERT INTO fn_organization_position_import (Id, TenantId, TaskId, LineNumber, PayloadHash, PositionId, CreatedAtUtc)
        VALUES (@Id, @TenantId, @TaskId, @LineNumber, @PayloadHash, NULL, @CreatedAtUtc)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement Complete = new("organization.position_import.complete", """
        UPDATE fn_organization_position_import SET PositionId = @PositionId
        WHERE TenantId = @TenantId AND Id = @Id AND PositionId IS NULL
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
