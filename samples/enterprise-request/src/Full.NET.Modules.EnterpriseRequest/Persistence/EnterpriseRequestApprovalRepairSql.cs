using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

/// <summary>父行锁串行化恢复，提交日志、业务终态和恢复记录同事务，不读取 Workflow 表。</summary>
internal static class EnterpriseRequestApprovalRepairSql
{
    internal static readonly SqlStatement LockParentSqlServer = new("enterprise_request.approval.lock_repair.sql_server",
        """
        SELECT Id FROM demo_enterprise_request_enterprise_request WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @Id AND TenantId = @TenantId AND Version = @ExpectedVersion
          AND Status = 'Submitted' AND IsDeleted = 0
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement LockParentMySql = new("enterprise_request.approval.lock_repair.mysql",
        """
        SELECT Id FROM demo_enterprise_request_enterprise_request
        WHERE Id = @Id AND TenantId = @TenantId AND Version = @ExpectedVersion
          AND Status = 'Submitted' AND IsDeleted = 0
        FOR UPDATE
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement InsertRepair = new("enterprise_request.approval.insert_repair",
        """
        INSERT INTO demo_enterprise_request_approval_repair
          (Id, TenantId, RequestId, WorkflowInstanceId, RequestVersion, KindKey, WorkflowStatusKey, ActorUserId, Reason, CreatedAtUtc)
        VALUES (@Id, @TenantId, @RequestId, @WorkflowInstanceId, @RequestVersion, @KindKey, @WorkflowStatusKey, @ActorUserId, @Reason, @CreatedAtUtc)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
