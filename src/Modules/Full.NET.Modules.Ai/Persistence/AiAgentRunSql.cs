using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>运行租约与原子进度提交；Worker 扫描与领取使用 Global 作用域并依赖行内 ScopeKey 隔离。</summary>
internal static class AiAgentRunSql
{
    public static readonly SqlStatement FindByClientRequest = new("ai.agent_run.find_by_client_request", """
        SELECT Id FROM fn_ai_agent_run
        WHERE ScopeKey = @ScopeKey AND ActorUserId = @ActorUserId AND ClientRequestId = @ClientRequestId
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertRun = new("ai.agent_run.insert", """
        INSERT INTO fn_ai_agent_run
            (Id, ScopeKey, TenantId, ActorUserId, SessionId, ClientRequestId, RequestHash,
             DefinitionKey, DefinitionVersion, AuthorizationBindingId, SecurityStamp, ActorScope, EffectiveScope,
             StatusKey, BudgetJson, DeadlineAtUtc, Version, LeaseEpoch, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @ScopeKey, @TenantId, @ActorUserId, @SessionId, @ClientRequestId, @RequestHash,
             @DefinitionKey, @DefinitionVersion, @AuthorizationBindingId, @SecurityStamp, @ActorScope, @EffectiveScope,
             'queued', @BudgetJson, @DeadlineAtUtc, 1, 0, @Now, @Now)
        """, SqlDataScope.Global);

    public static readonly SqlStatement AcquireLeaseSqlServer = new("ai.agent_run.acquire_lease.sqlserver", """
        UPDATE fn_ai_agent_run
        SET StatusKey = CASE WHEN StatusKey = 'queued' THEN 'running' ELSE StatusKey END,
            LeaseOwner = @WorkerId,
            LeaseEpoch = LeaseEpoch + 1,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            Version = Version + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @RunId
          AND StatusKey IN ('queued', 'running', 'retry_scheduled')
          AND DeadlineAtUtc > @Now
          AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now OR LeaseOwner = @WorkerId)
        """, SqlDataScope.Global);

    public static readonly SqlStatement AcquireLeaseMySql = new("ai.agent_run.acquire_lease.mysql", """
        UPDATE fn_ai_agent_run
        SET StatusKey = IF(StatusKey = 'queued', 'running', StatusKey),
            LeaseOwner = @WorkerId,
            LeaseEpoch = LeaseEpoch + 1,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            Version = Version + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @RunId
          AND StatusKey IN ('queued', 'running', 'retry_scheduled')
          AND DeadlineAtUtc > @Now
          AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now OR LeaseOwner = @WorkerId)
        """, SqlDataScope.Global);

    public static readonly SqlStatement SelectLease = new("ai.agent_run.select_lease", """
        SELECT Id, Version, LeaseEpoch, LeaseExpiresAtUtc
        FROM fn_ai_agent_run
        WHERE Id = @RunId AND LeaseOwner = @WorkerId
        """, SqlDataScope.Global);

    public static readonly SqlStatement RenewLease = new("ai.agent_run.renew_lease", """
        UPDATE fn_ai_agent_run
        SET LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            Version = Version + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @RunId
          AND LeaseOwner = @WorkerId
          AND LeaseEpoch = @LeaseEpoch
          AND Version = @Version
          AND StatusKey IN ('running', 'retry_scheduled', 'awaiting_approval', 'reconciliation_required')
        """, SqlDataScope.Global);

    public static readonly SqlStatement UpdateRunStatus = new("ai.agent_run.update_status", """
        UPDATE fn_ai_agent_run
        SET StatusKey = @StatusKey,
            Version = Version + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @RunId
          AND LeaseOwner = @WorkerId
          AND LeaseEpoch = @LeaseEpoch
          AND Version = @Version
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertStep = new("ai.agent_step.insert", """
        INSERT INTO fn_ai_agent_step
            (Id, RunId, StepKey, Attempt, OperationId, StatusKey, ModelConfigVersion, ToolVersion, PriceVersionId,
             InputTokens, OutputTokens, Cost, Currency, TraceId, ArgumentDigest, ErrorCode, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@StepId, @RunId, @StepKey, @Attempt, @OperationId, @StatusKey, @ModelConfigVersion, @ToolVersion, @PriceVersionId,
             @InputTokens, @OutputTokens, @Cost, @Currency, @TraceId, @ArgumentDigest, @ErrorCode, @Now, @Now)
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertCheckpoint = new("ai.agent_checkpoint.insert", """
        INSERT INTO fn_ai_agent_checkpoint
            (Id, RunId, Sequence, FormatVersion, FrameworkVersion, DefinitionVersion, PayloadProtected, Checksum, CreatedAtUtc)
        VALUES
            (@CheckpointId, @RunId, @Sequence, @FormatVersion, @FrameworkVersion, @DefinitionVersion, @PayloadProtected, @Checksum, @Now)
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindLatestCheckpoint = new("ai.agent_checkpoint.find_latest", """
        SELECT TOP 1 Id AS CheckpointId, RunId, Sequence, FormatVersion, FrameworkVersion, DefinitionVersion, PayloadProtected, Checksum
        FROM fn_ai_agent_checkpoint
        WHERE RunId = @RunId
        ORDER BY Sequence DESC
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindLatestCheckpointMySql = new("ai.agent_checkpoint.find_latest.mysql", """
        SELECT Id AS CheckpointId, RunId, Sequence, FormatVersion, FrameworkVersion, DefinitionVersion, PayloadProtected, Checksum
        FROM fn_ai_agent_checkpoint
        WHERE RunId = @RunId
        ORDER BY Sequence DESC
        LIMIT 1
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertEvent = new("ai.agent_event.insert", """
        INSERT INTO fn_ai_agent_event
            (Id, RunId, Sequence, EventType, PayloadVersion, Payload, CreatedAtUtc)
        VALUES
            (@EventId, @RunId, @Sequence, @EventType, @PayloadVersion, @Payload, @Now)
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindOwned = new("ai.agent_run.find_owned", """
        SELECT Id, ScopeKey, TenantId, ActorUserId, SessionId, DefinitionKey, DefinitionVersion,
               AuthorizationBindingId, SecurityStamp, ActorScope, EffectiveScope, StatusKey, BudgetJson,
               DeadlineAtUtc, Version, LeaseOwner, LeaseEpoch, LeaseExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc
        FROM fn_ai_agent_run
        WHERE Id = @RunId AND ScopeKey = @ScopeKey AND ActorUserId = @ActorUserId
        """, SqlDataScope.Global);

    public static readonly SqlStatement ScanQueuedSqlServer = new("ai.agent_run.scan_queued.sqlserver", """
        SELECT TOP (@BatchSize) Id
        FROM fn_ai_agent_run
        WHERE StatusKey = 'queued' AND DeadlineAtUtc > @Now
          AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
        ORDER BY CreatedAtUtc, Id
        """, SqlDataScope.Global);

    public static readonly SqlStatement ScanQueuedMySql = new("ai.agent_run.scan_queued.mysql", """
        SELECT Id
        FROM fn_ai_agent_run
        WHERE StatusKey = 'queued' AND DeadlineAtUtc > @Now
          AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
        ORDER BY CreatedAtUtc, Id
        LIMIT @BatchSize
        """, SqlDataScope.Global);

    public static readonly SqlStatement CancelOwned = new("ai.agent_run.cancel_owned", """
        UPDATE fn_ai_agent_run
        SET StatusKey = 'cancelled', Version = Version + 1, UpdatedAtUtc = @Now
        WHERE Id = @RunId AND ScopeKey = @ScopeKey AND ActorUserId = @ActorUserId
          AND StatusKey IN ('queued', 'running')
        """, SqlDataScope.Global);

    public static readonly SqlStatement ResumeOwned = new("ai.agent_run.resume_owned", """
        UPDATE fn_ai_agent_run
        SET StatusKey = 'queued',
            LeaseOwner = NULL,
            LeaseExpiresAtUtc = NULL,
            Version = Version + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @RunId
          AND ScopeKey = @ScopeKey
          AND ActorUserId = @ActorUserId
          AND SessionId = @SessionId
          AND SecurityStamp = @SecurityStamp
          AND StatusKey = 'awaiting_approval'
          AND DeadlineAtUtc > @Now
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindById = new("ai.agent_run.find_by_id", """
        SELECT Id, ScopeKey, TenantId, ActorUserId, SessionId, DefinitionKey, DefinitionVersion,
               AuthorizationBindingId, SecurityStamp, ActorScope, EffectiveScope, StatusKey, BudgetJson,
               DeadlineAtUtc, Version, LeaseOwner, LeaseEpoch, LeaseExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc
        FROM fn_ai_agent_run WHERE Id = @RunId
        """, SqlDataScope.Global);
}
