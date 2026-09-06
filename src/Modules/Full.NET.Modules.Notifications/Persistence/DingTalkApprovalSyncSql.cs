using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>钉钉审批镜像同步表的显式 SQL。</summary>
internal static class DingTalkApprovalSyncSql
{
    public static readonly SqlStatement CountForScope = new(
        "notifications.dingtalk_approval_sync.count_for_scope",
        """
        SELECT COUNT(1)
        FROM fn_notifications_dingtalk_approval_sync
        WHERE TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListForScopeSqlServer = new(
        "notifications.dingtalk_approval_sync.list_for_scope.sql_server",
        """
        SELECT Id, TenantScopeKey, WorkflowInstanceId, IdempotencyKey, DingTalkProcessInstanceId,
               ProcessCode, OriginatorUserId, DeptId, Title, Summary, StatusKey,
               ExternalStatusKey, ExternalResultKey, LastErrorCode, LastSyncedAtUtc,
               CreatedAtUtc, UpdatedAtUtc, CreatedByUserId
        FROM fn_notifications_dingtalk_approval_sync
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY CreatedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListForScopeMySql = new(
        "notifications.dingtalk_approval_sync.list_for_scope.mysql",
        """
        SELECT Id, TenantScopeKey, WorkflowInstanceId, IdempotencyKey, DingTalkProcessInstanceId,
               ProcessCode, OriginatorUserId, DeptId, Title, Summary, StatusKey,
               ExternalStatusKey, ExternalResultKey, LastErrorCode, LastSyncedAtUtc,
               CreatedAtUtc, UpdatedAtUtc, CreatedByUserId
        FROM fn_notifications_dingtalk_approval_sync
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY CreatedAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindById = new(
        "notifications.dingtalk_approval_sync.find_by_id",
        """
        SELECT Id, TenantScopeKey, WorkflowInstanceId, IdempotencyKey, DingTalkProcessInstanceId,
               ProcessCode, OriginatorUserId, DeptId, Title, Summary, StatusKey,
               ExternalStatusKey, ExternalResultKey, LastErrorCode, LastSyncedAtUtc,
               CreatedAtUtc, UpdatedAtUtc, CreatedByUserId
        FROM fn_notifications_dingtalk_approval_sync
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindByWorkflowInstance = new(
        "notifications.dingtalk_approval_sync.find_by_workflow_instance",
        """
        SELECT Id, TenantScopeKey, WorkflowInstanceId, IdempotencyKey, DingTalkProcessInstanceId,
               ProcessCode, OriginatorUserId, DeptId, Title, Summary, StatusKey,
               ExternalStatusKey, ExternalResultKey, LastErrorCode, LastSyncedAtUtc,
               CreatedAtUtc, UpdatedAtUtc, CreatedByUserId
        FROM fn_notifications_dingtalk_approval_sync
        WHERE TenantScopeKey = @TenantScopeKey
          AND WorkflowInstanceId = @WorkflowInstanceId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindByProcessInstanceId = new(
        "notifications.dingtalk_approval_sync.find_by_process_instance_id",
        """
        SELECT Id, TenantScopeKey, WorkflowInstanceId, IdempotencyKey, DingTalkProcessInstanceId,
               ProcessCode, OriginatorUserId, DeptId, Title, Summary, StatusKey,
               ExternalStatusKey, ExternalResultKey, LastErrorCode, LastSyncedAtUtc,
               CreatedAtUtc, UpdatedAtUtc, CreatedByUserId
        FROM fn_notifications_dingtalk_approval_sync
        WHERE DingTalkProcessInstanceId = @DingTalkProcessInstanceId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListPollCandidates = new(
        "notifications.dingtalk_approval_sync.list_poll_candidates",
        """
        SELECT TOP (@BatchSize) Id, TenantScopeKey, WorkflowInstanceId, IdempotencyKey, DingTalkProcessInstanceId,
               ProcessCode, OriginatorUserId, DeptId, Title, Summary, StatusKey,
               ExternalStatusKey, ExternalResultKey, LastErrorCode, LastSyncedAtUtc,
               CreatedAtUtc, UpdatedAtUtc, CreatedByUserId
        FROM fn_notifications_dingtalk_approval_sync
        WHERE StatusKey IN (@PendingOutbound, @OutboundFailed, @Running)
        ORDER BY UpdatedAtUtc ASC, CreatedAtUtc ASC
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListPollCandidatesMySql = new(
        "notifications.dingtalk_approval_sync.list_poll_candidates.mysql",
        """
        SELECT Id, TenantScopeKey, WorkflowInstanceId, IdempotencyKey, DingTalkProcessInstanceId,
               ProcessCode, OriginatorUserId, DeptId, Title, Summary, StatusKey,
               ExternalStatusKey, ExternalResultKey, LastErrorCode, LastSyncedAtUtc,
               CreatedAtUtc, UpdatedAtUtc, CreatedByUserId
        FROM fn_notifications_dingtalk_approval_sync
        WHERE StatusKey IN (@PendingOutbound, @OutboundFailed, @Running)
        ORDER BY COALESCE(UpdatedAtUtc, CreatedAtUtc) ASC
        LIMIT @BatchSize
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Insert = new(
        "notifications.dingtalk_approval_sync.insert",
        """
        INSERT INTO fn_notifications_dingtalk_approval_sync
            (Id, TenantScopeKey, WorkflowInstanceId, IdempotencyKey, DingTalkProcessInstanceId,
             ProcessCode, OriginatorUserId, DeptId, Title, Summary, StatusKey,
             ExternalStatusKey, ExternalResultKey, LastErrorCode, LastSyncedAtUtc,
             CreatedAtUtc, UpdatedAtUtc, CreatedByUserId)
        VALUES
            (@Id, @TenantScopeKey, @WorkflowInstanceId, @IdempotencyKey, @DingTalkProcessInstanceId,
             @ProcessCode, @OriginatorUserId, @DeptId, @Title, @Summary, @StatusKey,
             @ExternalStatusKey, @ExternalResultKey, @LastErrorCode, @LastSyncedAtUtc,
             @CreatedAtUtc, @UpdatedAtUtc, @CreatedByUserId)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateMirrorState = new(
        "notifications.dingtalk_approval_sync.update_mirror_state",
        """
        UPDATE fn_notifications_dingtalk_approval_sync
        SET DingTalkProcessInstanceId = @DingTalkProcessInstanceId,
            StatusKey = @StatusKey,
            ExternalStatusKey = @ExternalStatusKey,
            ExternalResultKey = @ExternalResultKey,
            LastErrorCode = @LastErrorCode,
            LastSyncedAtUtc = @LastSyncedAtUtc,
            UpdatedAtUtc = @UpdatedAtUtc
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);
}
