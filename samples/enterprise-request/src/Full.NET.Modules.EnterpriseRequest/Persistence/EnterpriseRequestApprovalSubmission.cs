using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

/// <summary>企业申请提交时固定的流程身份与版本，不读取 Workflow 私有表。</summary>
internal sealed record EnterpriseRequestApprovalSubmission(
    Guid Id, Guid TenantId, Guid RequestId, long RequestVersion,
    Guid WorkflowDefinitionVersionId, Guid WorkflowInstanceId,
    Guid SubmittedById, Guid OrganizationUnitId, string BusinessTitle,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? StartedAtUtc,
    string? FinalStatus, DateTimeOffset? CompletedAtUtc, Guid? LastMessageId);

/// <summary>提交日志只访问本模块表；租户参数由受控执行器绑定。</summary>
internal static class EnterpriseRequestApprovalSql
{
    private const string Projection = "Id, TenantId, RequestId, RequestVersion, WorkflowDefinitionVersionId, WorkflowInstanceId, SubmittedById, OrganizationUnitId, BusinessTitle, CreatedAtUtc, StartedAtUtc, FinalStatus, CompletedAtUtc, LastMessageId";

    public static readonly SqlStatement FindByRequest = new(
        "enterprise_request.approval.find_by_request",
        $"SELECT {Projection} FROM demo_enterprise_request_approval_submission WHERE RequestId = @RequestId AND TenantId = @TenantId",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement Insert = new(
        "enterprise_request.approval.insert",
        """
        INSERT INTO demo_enterprise_request_approval_submission
            (Id, TenantId, RequestId, RequestVersion, WorkflowDefinitionVersionId, WorkflowInstanceId,
             SubmittedById, OrganizationUnitId, BusinessTitle, CreatedAtUtc)
        VALUES (@Id, @TenantId, @RequestId, @RequestVersion, @WorkflowDefinitionVersionId, @WorkflowInstanceId,
             @SubmittedById, @OrganizationUnitId, @BusinessTitle, @CreatedAtUtc)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement MarkStarted = new(
        "enterprise_request.approval.mark_started",
        """
        UPDATE demo_enterprise_request_approval_submission SET StartedAtUtc = @StartedAtUtc
        WHERE Id = @Id AND TenantId = @TenantId AND WorkflowInstanceId = @WorkflowInstanceId AND StartedAtUtc IS NULL
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement MarkFinal = new(
        "enterprise_request.approval.mark_final",
        """
        UPDATE demo_enterprise_request_approval_submission
        SET FinalStatus = @FinalStatus, CompletedAtUtc = @CompletedAtUtc, LastMessageId = @LastMessageId
        WHERE Id = @Id AND TenantId = @TenantId AND WorkflowInstanceId = @WorkflowInstanceId AND FinalStatus IS NULL
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static Dictionary<string, object?> Parameters(params (string Key, object? Value)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
}
