using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>暂停只阻止新分发和接纳；取消保留在途尝试，未知结果必须经独立对账才能恢复。</summary>
internal static partial class WorkflowConnectorSql
{
    public static readonly SqlStatement YieldApply = new("workflow.connector.yield_apply", """
        UPDATE fn_workflow_connector_call SET StatusKey='result_ready',LeaseOwnerKey=NULL,LeaseExpiresAtUtc=NULL,
          LastErrorKey=@ErrorKey,DueAtUtc=@DueAtUtc,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND ModeKey='async' AND StatusKey='applying'
          AND LeaseGeneration=@Generation AND LeaseOwnerKey=@Owner AND LeaseExpiresAtUtc>@Now AND CancelRequestedAtUtc IS NULL
        """, SqlDataScope.Global);
    public static readonly SqlStatement RecoverExpired = new("workflow.connector.recover_expired", """
        UPDATE fn_workflow_connector_call SET StatusKey=CASE
            WHEN CancelRequestedAtUtc IS NOT NULL AND @NextStatusKey IN ('result_ready','rejected','failed') THEN 'cancelled'
            WHEN CancelRequestedAtUtc IS NOT NULL THEN 'outcome_unknown' ELSE @NextStatusKey END,
          SelectedAttemptId=@SelectedAttemptId,LeaseOwnerKey=NULL,LeaseExpiresAtUtc=NULL,
          CompletedAtUtc=CASE WHEN CancelRequestedAtUtc IS NOT NULL AND @NextStatusKey IN ('result_ready','rejected','failed') THEN @Now ELSE CompletedAtUtc END,
          DueAtUtc=@DueAtUtc,LastErrorKey=@ErrorKey,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='dispatched'
          AND LeaseGeneration=@Generation AND Revision=@Revision AND LeaseExpiresAtUtc<=@Now
        """, SqlDataScope.Global);

    public static readonly SqlStatement FailPending = new("workflow.connector.fail_pending", """
        UPDATE fn_workflow_connector_call SET StatusKey=@StatusKey,LastErrorKey=@ErrorKey,
          LeaseOwnerKey=NULL,LeaseExpiresAtUtc=NULL,CompletedAtUtc=@Now,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
          AND Revision=@Revision AND StatusKey IN ('pending','retryable') AND CancelRequestedAtUtc IS NULL
        """, SqlDataScope.Global);

    public static readonly SqlStatement FailApply = new("workflow.connector.fail_apply", """
        UPDATE fn_workflow_connector_call SET StatusKey='failed',LastErrorKey=@ErrorKey,
          LeaseOwnerKey=NULL,LeaseExpiresAtUtc=NULL,CompletedAtUtc=@Now,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='applying'
          AND LeaseGeneration=@Generation AND LeaseOwnerKey=@Owner AND LeaseExpiresAtUtc>@Now AND CancelRequestedAtUtc IS NULL
        """, SqlDataScope.Global);

    public static readonly SqlStatement CancelInstance = new("workflow.connector.cancel_instance", """
        UPDATE fn_workflow_connector_call SET
          StatusKey=CASE WHEN StatusKey='dispatched' THEN 'dispatched' WHEN StatusKey='outcome_unknown' THEN 'outcome_unknown' ELSE 'cancelled' END,
          CancelRequestedAtUtc=COALESCE(CancelRequestedAtUtc,@Now),
          LeaseGeneration=CASE WHEN StatusKey='dispatched' THEN LeaseGeneration ELSE LeaseGeneration+1 END,
          LeaseOwnerKey=CASE WHEN StatusKey='dispatched' THEN LeaseOwnerKey ELSE NULL END,
          LeaseExpiresAtUtc=CASE WHEN StatusKey='dispatched' THEN LeaseExpiresAtUtc ELSE NULL END,
          CompletedAtUtc=CASE WHEN StatusKey IN ('dispatched','outcome_unknown') THEN CompletedAtUtc ELSE @Now END,
          Revision=Revision+1
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey NOT IN ('applied','cancelled')
        """, SqlDataScope.Global);

    public static readonly SqlStatement CountRecoverable = new("workflow.connector.count_recoverable", """
        SELECT COUNT(*) FROM fn_workflow_connector_call WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
          AND StatusKey IN ('pending','retryable','dispatched','result_ready','applying') AND CancelRequestedAtUtc IS NULL
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountBlocked = new("workflow.connector.count_blocked", """
        SELECT COUNT(*) FROM fn_workflow_connector_call WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
          AND StatusKey IN ('outcome_unknown','rejected','failed') AND CancelRequestedAtUtc IS NULL
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountUnsettled = new("workflow.connector.count_unsettled", """
        SELECT COUNT(*) FROM fn_workflow_connector_call WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
          AND StatusKey NOT IN ('applied','cancelled')
        """, SqlDataScope.Global);
}
