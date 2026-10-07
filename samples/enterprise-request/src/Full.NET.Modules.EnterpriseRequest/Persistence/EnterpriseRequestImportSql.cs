using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

/// <summary>样例自有回执，任务关联只保存标识，不访问 ImportExport 的表。</summary>
internal static class EnterpriseRequestImportSql
{
    internal static readonly SqlStatement Find = new("enterprise_request.import.find", """
        SELECT EntityId, PayloadHash FROM demo_enterprise_request_import_receipt
        WHERE TenantId = @TenantId AND TaskId = @TaskId AND LineNumber = @LineNumber
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Insert = new("enterprise_request.import.insert", """
        INSERT INTO demo_enterprise_request_import_receipt (Id, TenantId, TaskId, LineNumber, PayloadHash, EntityId, CreatedAtUtc)
        VALUES (@Id, @TenantId, @TaskId, @LineNumber, @PayloadHash, NULL, @CreatedAtUtc)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Complete = new("enterprise_request.import.complete", """
        UPDATE demo_enterprise_request_import_receipt SET EntityId = @EntityId
        WHERE TenantId = @TenantId AND Id = @Id AND EntityId IS NULL
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

/// <summary>未完成回执不得独立提交；摘要防止同一任务行改变载荷。</summary>
internal sealed record EnterpriseRequestImportReceiptRecord(Guid? EntityId, string PayloadHash);
