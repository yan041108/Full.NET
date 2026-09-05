using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.DataApproval.Persistence;

/// <summary>DataApproval 模块 Dapper SQL 语句集合。</summary>
internal static class DataApprovalSql
{
    public static readonly SqlStatement FindRequestById = new(
        "data_approval.request.find_by_id",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ScenarioKey, TargetEntityId,
               StatusKey, BeforeSnapshotJson, AfterSnapshotJson, WorkflowInstanceId,
               WorkflowRevision, WorkflowDefinitionVersionId, SubmittedByUserId,
               SubmittedAtUtc, ResolvedAtUtc, IdempotencyKey, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_data_approval_request
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindRequestByIdempotency = new(
        "data_approval.request.find_by_idempotency",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ScenarioKey, TargetEntityId,
               StatusKey, BeforeSnapshotJson, AfterSnapshotJson, WorkflowInstanceId,
               WorkflowRevision, WorkflowDefinitionVersionId, SubmittedByUserId,
               SubmittedAtUtc, ResolvedAtUtc, IdempotencyKey, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_data_approval_request
        WHERE TenantScopeKey = @TenantScopeKey
          AND IdempotencyKey = @IdempotencyKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindRequestByBusinessId = new(
        "data_approval.request.find_by_business_id",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ScenarioKey, TargetEntityId,
               StatusKey, BeforeSnapshotJson, AfterSnapshotJson, WorkflowInstanceId,
               WorkflowRevision, WorkflowDefinitionVersionId, SubmittedByUserId,
               SubmittedAtUtc, ResolvedAtUtc, IdempotencyKey, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_data_approval_request
        WHERE Id = @BusinessId
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertRequest = new(
        "data_approval.request.insert",
        """
        INSERT INTO fn_data_approval_request
            (Id, TenantId, ScopeKey, TenantScopeKey, ScenarioKey, TargetEntityId,
             StatusKey, BeforeSnapshotJson, AfterSnapshotJson, WorkflowInstanceId,
             WorkflowRevision, WorkflowDefinitionVersionId, SubmittedByUserId,
             SubmittedAtUtc, ResolvedAtUtc, IdempotencyKey, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @ScopeKey, @TenantScopeKey, @ScenarioKey, @TargetEntityId,
             @StatusKey, @BeforeSnapshotJson, @AfterSnapshotJson, @WorkflowInstanceId,
             @WorkflowRevision, @WorkflowDefinitionVersionId, @SubmittedByUserId,
             @SubmittedAtUtc, @ResolvedAtUtc, @IdempotencyKey, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement LinkWorkflowInstance = new(
        "data_approval.request.link_workflow",
        """
        UPDATE fn_data_approval_request
        SET WorkflowInstanceId = @WorkflowInstanceId,
            WorkflowRevision = @WorkflowRevision,
            StatusKey = @StatusKey,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @ExpectedVersion
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateStatus = new(
        "data_approval.request.update_status",
        """
        UPDATE fn_data_approval_request
        SET StatusKey = @StatusKey,
            ResolvedAtUtc = @ResolvedAtUtc,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND StatusKey = @ExpectedStatusKey
          AND Version = @ExpectedVersion
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountRequests = new(
        "data_approval.request.count",
        """
        SELECT COUNT(1)
        FROM fn_data_approval_request
        WHERE TenantScopeKey = @TenantScopeKey
          AND (@ScenarioKey IS NULL OR ScenarioKey = @ScenarioKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement PageRequestsSqlServer = new(
        "data_approval.request.page.sqlserver",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ScenarioKey, TargetEntityId,
               StatusKey, BeforeSnapshotJson, AfterSnapshotJson, WorkflowInstanceId,
               WorkflowRevision, WorkflowDefinitionVersionId, SubmittedByUserId,
               SubmittedAtUtc, ResolvedAtUtc, IdempotencyKey, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_data_approval_request
        WHERE TenantScopeKey = @TenantScopeKey
          AND (@ScenarioKey IS NULL OR ScenarioKey = @ScenarioKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey)
        ORDER BY SubmittedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

        SELECT COUNT(1)
        FROM fn_data_approval_request
        WHERE TenantScopeKey = @TenantScopeKey
          AND (@ScenarioKey IS NULL OR ScenarioKey = @ScenarioKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey);
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement PageRequestsMySql = new(
        "data_approval.request.page.mysql",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ScenarioKey, TargetEntityId,
               StatusKey, BeforeSnapshotJson, AfterSnapshotJson, WorkflowInstanceId,
               WorkflowRevision, WorkflowDefinitionVersionId, SubmittedByUserId,
               SubmittedAtUtc, ResolvedAtUtc, IdempotencyKey, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_data_approval_request
        WHERE TenantScopeKey = @TenantScopeKey
          AND (@ScenarioKey IS NULL OR ScenarioKey = @ScenarioKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey)
        ORDER BY SubmittedAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset;

        SELECT COUNT(1)
        FROM fn_data_approval_request
        WHERE TenantScopeKey = @TenantScopeKey
          AND (@ScenarioKey IS NULL OR ScenarioKey = @ScenarioKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey);
        """,
        SqlDataScope.Global);
}

/// <summary>DataApproval 请求持久化投影。</summary>
internal sealed record DataApprovalRequestRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    string ScenarioKey,
    Guid TargetEntityId,
    string StatusKey,
    string? BeforeSnapshotJson,
    string AfterSnapshotJson,
    Guid? WorkflowInstanceId,
    long? WorkflowRevision,
    Guid WorkflowDefinitionVersionId,
    Guid SubmittedByUserId,
    DateTimeOffset SubmittedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    string IdempotencyKey,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);
