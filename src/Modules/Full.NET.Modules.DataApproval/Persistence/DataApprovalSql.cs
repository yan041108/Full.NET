using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.DataApproval.Persistence;

/// <summary>DataApproval 模块 Dapper SQL 语句集合。</summary>
internal static class DataApprovalSql
{
    private const string RequestSelectColumns = """
        Id, TenantId, ScopeKey, TenantScopeKey, ScenarioKey, TargetEntityId,
        StatusKey, BeforeSnapshotJson, AfterSnapshotJson, WorkflowInstanceId,
        WorkflowRevision, WorkflowDefinitionVersionId, SubmittedByUserId,
        SubmittedAtUtc, ResolvedAtUtc, IdempotencyKey, RecoveryStatusKey,
        LastFailureCode, LastFailureMessage, LastRecoveryAttemptAtUtc,
        RecoveryAttemptCount, ApplicationStatusKey, LastApplicationFailureCode,
        LastApplicationFailureMessage, LastApplicationAttemptAtUtc, ApplicationAttemptCount,
        CreatedAtUtc, UpdatedAtUtc, Version
        """;

    public static readonly SqlStatement FindRequestById = new(
        "data_approval.request.find_by_id",
        $"""
        SELECT {RequestSelectColumns}
        FROM fn_dataapproval_request
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindRequestByIdempotency = new(
        "data_approval.request.find_by_idempotency",
        $"""
        SELECT {RequestSelectColumns}
        FROM fn_dataapproval_request
        WHERE TenantScopeKey = @TenantScopeKey
          AND IdempotencyKey = @IdempotencyKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindRequestByBusinessId = new(
        "data_approval.request.find_by_business_id",
        $"""
        SELECT {RequestSelectColumns}
        FROM fn_dataapproval_request
        WHERE Id = @BusinessId
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertRequest = new(
        "data_approval.request.insert",
        """
        INSERT INTO fn_dataapproval_request
            (Id, TenantId, ScopeKey, TenantScopeKey, ScenarioKey, TargetEntityId,
             StatusKey, BeforeSnapshotJson, AfterSnapshotJson, WorkflowInstanceId,
             WorkflowRevision, WorkflowDefinitionVersionId, SubmittedByUserId,
             SubmittedAtUtc, ResolvedAtUtc, IdempotencyKey, RecoveryStatusKey,
             LastFailureCode, LastFailureMessage, LastRecoveryAttemptAtUtc,
             RecoveryAttemptCount, ApplicationStatusKey, LastApplicationFailureCode,
             LastApplicationFailureMessage, LastApplicationAttemptAtUtc, ApplicationAttemptCount,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @ScopeKey, @TenantScopeKey, @ScenarioKey, @TargetEntityId,
             @StatusKey, @BeforeSnapshotJson, @AfterSnapshotJson, @WorkflowInstanceId,
             @WorkflowRevision, @WorkflowDefinitionVersionId, @SubmittedByUserId,
             @SubmittedAtUtc, @ResolvedAtUtc, @IdempotencyKey, @RecoveryStatusKey,
             @LastFailureCode, @LastFailureMessage, @LastRecoveryAttemptAtUtc,
             @RecoveryAttemptCount, @ApplicationStatusKey, @LastApplicationFailureCode,
             @LastApplicationFailureMessage, @LastApplicationAttemptAtUtc, @ApplicationAttemptCount,
             @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement LinkWorkflowInstance = new(
        "data_approval.request.link_workflow",
        """
        UPDATE fn_dataapproval_request
        SET WorkflowInstanceId = @WorkflowInstanceId,
            WorkflowRevision = @WorkflowRevision,
            StatusKey = @StatusKey,
            RecoveryStatusKey = @RecoveryStatusKey,
            LastFailureCode = NULL,
            LastFailureMessage = NULL,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @ExpectedVersion
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement RecordRecoveryFailure = new(
        "data_approval.request.record_recovery_failure",
        """
        UPDATE fn_dataapproval_request
        SET RecoveryStatusKey = @RecoveryStatusKey,
            LastFailureCode = @LastFailureCode,
            LastFailureMessage = @LastFailureMessage,
            LastRecoveryAttemptAtUtc = @LastRecoveryAttemptAtUtc,
            RecoveryAttemptCount = RecoveryAttemptCount + 1,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @ExpectedVersion
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement MarkApplicationPending = new(
        "data_approval.request.mark_application_pending",
        """
        UPDATE fn_dataapproval_request
        SET ApplicationStatusKey = @ApplicationStatusKey,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND StatusKey = @ExpectedStatusKey
          AND ApplicationStatusKey IN ('none', 'failed_retryable', 'pending_apply')
          AND Version = @ExpectedVersion
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement RecordApplicationFailure = new(
        "data_approval.request.record_application_failure",
        """
        UPDATE fn_dataapproval_request
        SET ApplicationStatusKey = @ApplicationStatusKey,
            LastApplicationFailureCode = @LastApplicationFailureCode,
            LastApplicationFailureMessage = @LastApplicationFailureMessage,
            LastApplicationAttemptAtUtc = @LastApplicationAttemptAtUtc,
            ApplicationAttemptCount = ApplicationAttemptCount + 1,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND StatusKey = @ExpectedStatusKey
          AND Version = @ExpectedVersion
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CompleteApprovedApplication = new(
        "data_approval.request.complete_approved_application",
        """
        UPDATE fn_dataapproval_request
        SET StatusKey = @StatusKey,
            ApplicationStatusKey = @ApplicationStatusKey,
            LastApplicationFailureCode = NULL,
            LastApplicationFailureMessage = NULL,
            LastApplicationAttemptAtUtc = CASE
                WHEN @ApplicationAttemptIncrement = 1 THEN @LastApplicationAttemptAtUtc
                ELSE LastApplicationAttemptAtUtc
            END,
            ApplicationAttemptCount = ApplicationAttemptCount + @ApplicationAttemptIncrement,
            ResolvedAtUtc = @ResolvedAtUtc,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND StatusKey = @ExpectedStatusKey
          AND Version = @ExpectedVersion
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListPendingApplicationSqlServer = new(
        "data_approval.request.list_pending_application.sqlserver",
        $"""
        SELECT TOP (@BatchSize)
               {RequestSelectColumns}
        FROM fn_dataapproval_request WITH (READPAST)
        WHERE StatusKey = 'in_review'
          AND ApplicationStatusKey IN ('pending_apply', 'failed_retryable')
          AND (LastApplicationAttemptAtUtc IS NULL OR LastApplicationAttemptAtUtc < @NotBeforeUtc)
        ORDER BY SubmittedAtUtc ASC, Id ASC
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListPendingApplicationMySql = new(
        "data_approval.request.list_pending_application.mysql",
        $"""
        SELECT {RequestSelectColumns}
        FROM fn_dataapproval_request
        WHERE StatusKey = 'in_review'
          AND ApplicationStatusKey IN ('pending_apply', 'failed_retryable')
          AND (LastApplicationAttemptAtUtc IS NULL OR LastApplicationAttemptAtUtc < @NotBeforeUtc)
        ORDER BY SubmittedAtUtc ASC, Id ASC
        LIMIT @BatchSize
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListPendingRecoverySqlServer = new(
        "data_approval.request.list_pending_recovery.sqlserver",
        $"""
        SELECT TOP (@BatchSize)
               {RequestSelectColumns}
        FROM fn_dataapproval_request WITH (READPAST)
        WHERE StatusKey = 'pending'
          AND WorkflowInstanceId IS NULL
          AND RecoveryStatusKey IN ('pending_link', 'failed_retryable', 'none')
          AND (LastRecoveryAttemptAtUtc IS NULL OR LastRecoveryAttemptAtUtc < @NotBeforeUtc)
        ORDER BY SubmittedAtUtc ASC, Id ASC
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListPendingRecoveryMySql = new(
        "data_approval.request.list_pending_recovery.mysql",
        $"""
        SELECT {RequestSelectColumns}
        FROM fn_dataapproval_request
        WHERE StatusKey = 'pending'
          AND WorkflowInstanceId IS NULL
          AND RecoveryStatusKey IN ('pending_link', 'failed_retryable', 'none')
          AND (LastRecoveryAttemptAtUtc IS NULL OR LastRecoveryAttemptAtUtc < @NotBeforeUtc)
        ORDER BY SubmittedAtUtc ASC, Id ASC
        LIMIT @BatchSize
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateStatus = new(
        "data_approval.request.update_status",
        """
        UPDATE fn_dataapproval_request
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
        FROM fn_dataapproval_request
        WHERE TenantScopeKey = @TenantScopeKey
          AND (@ScenarioKey IS NULL OR ScenarioKey = @ScenarioKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement PageRequestsSqlServer = new(
        "data_approval.request.page.sqlserver",
        $"""
        SELECT {RequestSelectColumns}
        FROM fn_dataapproval_request
        WHERE TenantScopeKey = @TenantScopeKey
          AND (@ScenarioKey IS NULL OR ScenarioKey = @ScenarioKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey)
        ORDER BY SubmittedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

        SELECT COUNT(1)
        FROM fn_dataapproval_request
        WHERE TenantScopeKey = @TenantScopeKey
          AND (@ScenarioKey IS NULL OR ScenarioKey = @ScenarioKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey);
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement PageRequestsMySql = new(
        "data_approval.request.page.mysql",
        $"""
        SELECT {RequestSelectColumns}
        FROM fn_dataapproval_request
        WHERE TenantScopeKey = @TenantScopeKey
          AND (@ScenarioKey IS NULL OR ScenarioKey = @ScenarioKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey)
        ORDER BY SubmittedAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset;

        SELECT COUNT(1)
        FROM fn_dataapproval_request
        WHERE TenantScopeKey = @TenantScopeKey
          AND (@ScenarioKey IS NULL OR ScenarioKey = @ScenarioKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey);
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindScenarioByKey = new(
        "data_approval.scenario.find_by_key",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ScenarioKey, IsEnabled,
               WorkflowDefinitionKey, WorkflowDefinitionVersionId,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_dataapproval_scenario
        WHERE TenantScopeKey = @TenantScopeKey
          AND ScenarioKey = @ScenarioKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListScenarios = new(
        "data_approval.scenario.list",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ScenarioKey, IsEnabled,
               WorkflowDefinitionKey, WorkflowDefinitionVersionId,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_dataapproval_scenario
        WHERE TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertScenario = new(
        "data_approval.scenario.insert",
        """
        INSERT INTO fn_dataapproval_scenario
            (Id, TenantId, ScopeKey, TenantScopeKey, ScenarioKey, IsEnabled,
             WorkflowDefinitionKey, WorkflowDefinitionVersionId,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @ScopeKey, @TenantScopeKey, @ScenarioKey, @IsEnabled,
             @WorkflowDefinitionKey, @WorkflowDefinitionVersionId,
             @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateScenarioBinding = new(
        "data_approval.scenario.update_binding",
        """
        UPDATE fn_dataapproval_scenario
        SET IsEnabled = @IsEnabled,
            WorkflowDefinitionKey = @WorkflowDefinitionKey,
            WorkflowDefinitionVersionId = @WorkflowDefinitionVersionId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE TenantScopeKey = @TenantScopeKey
          AND ScenarioKey = @ScenarioKey
          AND Version = @ExpectedVersion
        """,
        SqlDataScope.Global);
}

/// <summary>DataApproval 场景绑定持久化投影。</summary>
internal sealed record DataApprovalScenarioRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    string ScenarioKey,
    bool IsEnabled,
    string? WorkflowDefinitionKey,
    Guid? WorkflowDefinitionVersionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

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
    string RecoveryStatusKey,
    string? LastFailureCode,
    string? LastFailureMessage,
    DateTimeOffset? LastRecoveryAttemptAtUtc,
    int RecoveryAttemptCount,
    string ApplicationStatusKey,
    string? LastApplicationFailureCode,
    string? LastApplicationFailureMessage,
    DateTimeOffset? LastApplicationAttemptAtUtc,
    int ApplicationAttemptCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);
