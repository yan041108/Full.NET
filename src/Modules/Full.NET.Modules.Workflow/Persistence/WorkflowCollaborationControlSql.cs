using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>协议事实是双方最小协调面；所有业务实例、表单和步骤只在本作用域事务写入。</summary>
internal static class WorkflowCollaborationControlSql
{
    public static readonly SqlStatement LockSourceGrant = new("workflow.collaboration_control.lock_source_grant", """
        UPDATE fn_workflow_collaboration_grant SET Revision=Revision
        WHERE Id=@GrantId AND SourceTenantScopeKey=@TenantScopeKey AND TargetTenantScopeKey=@TargetTenantScopeKey
            AND StatusKey='active' AND Revision=@GrantRevision AND PolicyHash=@PolicyHash AND ExpiresAtUtc>@Now
        """, SqlDataScope.Global);
    public static readonly SqlStatement ActiveSourceStep = new("workflow.collaboration_control.active_source_step", """
        SELECT step.Revision FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_collaboration_call AS callState ON callState.Id=step.Id AND callState.InstanceId=step.InstanceId
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.Id=step.ExecutionEpochId AND epoch.InstanceId=step.InstanceId AND epoch.TenantScopeKey=callState.TenantScopeKey
        WHERE step.Id=@Id AND step.InstanceId=@InstanceId AND callState.TenantScopeKey=@TenantScopeKey
            AND step.NodeTypeKey='process.collaboration' AND step.NodeKey=callState.NodeKey AND step.StatusKey='active'
            AND step.ExecutionEpochId=callState.ExecutionEpochId AND epoch.StatusKey='active' AND callState.BindingHash=@BindingHash
        """, SqlDataScope.Global);
    public static readonly SqlStatement Find = new("workflow.collaboration_control.find", """
        SELECT Id,CallId,SourceTenantScopeKey,TargetTenantScopeKey,KindKey,BodyJson,BodyHash,EnvelopeMessageId,StatusKey,Revision,CreatedAtUtc,ReceivedAtUtc
        FROM fn_workflow_collaboration_control WHERE CallId=@Id AND KindKey=@KindKey
            AND (SourceTenantScopeKey=@TenantScopeKey OR TargetTenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.collaboration_control.insert", """
        INSERT INTO fn_workflow_collaboration_control
            (Id,CallId,SourceTenantScopeKey,TargetTenantScopeKey,KindKey,BodyJson,BodyHash,EnvelopeMessageId,StatusKey,Revision,CreatedAtUtc,ReceivedAtUtc)
        SELECT @ControlId,Id,SourceTenantScopeKey,TargetTenantScopeKey,@KindKey,@BodyJson,@BodyHash,NULL,'published',1,@Now,NULL
        FROM fn_workflow_collaboration_admission WHERE Id=@Id AND TargetTenantScopeKey=@TenantScopeKey AND BindingHash=@BindingHash
            AND StatusKey IN ('created','active','cancel_requested')
            AND (TargetInstanceId=@TargetInstanceId OR (TargetInstanceId IS NULL AND @TargetInstanceId IS NULL))
        """, SqlDataScope.Global);
    public static readonly SqlStatement Receive = new("workflow.collaboration_control.receive", """
        UPDATE fn_workflow_collaboration_control SET StatusKey='received',EnvelopeMessageId=@EnvelopeMessageId,ReceivedAtUtc=@Now,Revision=Revision+1
        WHERE Id=@ControlId AND CallId=@Id AND TargetTenantScopeKey=@TenantScopeKey AND KindKey=@KindKey AND BodyHash=@BodyHash
            AND StatusKey='published' AND EnvelopeMessageId IS NULL
        """, SqlDataScope.Global);
    public static readonly SqlStatement Apply = new("workflow.collaboration_control.apply", """
        UPDATE fn_workflow_collaboration_control SET StatusKey='applied',Revision=Revision+1
        WHERE Id=@ControlId AND CallId=@Id AND SourceTenantScopeKey=@TenantScopeKey AND KindKey=@KindKey AND BodyHash=@BodyHash
            AND StatusKey='received' AND EnvelopeMessageId IS NOT NULL AND Revision=@Revision
        """, SqlDataScope.Global);
    public static readonly SqlStatement LockSource = new("workflow.collaboration_control.lock_source", """
        UPDATE fn_workflow_collaboration_admission SET Revision=Revision
        WHERE Id=@Id AND SourceTenantScopeKey=@TenantScopeKey AND BindingHash=@BindingHash AND StatusKey=@AdmissionStatus
            AND (TargetInstanceId=@TargetInstanceId OR (TargetInstanceId IS NULL AND @TargetInstanceId IS NULL))
        """, SqlDataScope.Global);
    public static readonly SqlStatement LockClosing = new("workflow.collaboration_control.lock_closing", """
        UPDATE fn_workflow_collaboration_admission SET Revision=Revision
        WHERE Id=@Id AND TargetTenantScopeKey=@TenantScopeKey AND BindingHash=@BindingHash AND StatusKey='cancel_requested'
            AND (TargetInstanceId=@TargetInstanceId OR (TargetInstanceId IS NULL AND @TargetInstanceId IS NULL))
        """, SqlDataScope.Global);
    public static readonly SqlStatement LockTerminalReconciliation = new("workflow.collaboration_control.lock_terminal_reconciliation", """
        UPDATE fn_workflow_collaboration_admission SET Revision=Revision
        WHERE Id=@Id AND TargetTenantScopeKey=@TenantScopeKey AND BindingHash=@BindingHash AND TargetInstanceId=@TargetInstanceId
            AND StatusKey IN ('cancel_requested','cancelled')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CloseGrant = new("workflow.collaboration_control.close_grant", """
        UPDATE fn_workflow_collaboration_admission SET StatusKey='cancel_requested',Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE GrantId=@GrantId AND StatusKey IN ('requested','created','active')
            AND (SourceTenantScopeKey=@TenantScopeKey OR TargetTenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CloseTargetRoot = new("workflow.collaboration_control.close_target_root", """
        UPDATE fn_workflow_collaboration_admission SET StatusKey='cancel_requested',Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE TargetTenantScopeKey=@TenantScopeKey AND TargetInstanceId=@InstanceId AND StatusKey IN ('created','active')
            AND EXISTS (SELECT 1 FROM fn_workflow_collaboration_origin AS origin
                WHERE origin.Id=@InstanceId AND origin.RootInstanceId=@InstanceId AND origin.CallId=fn_workflow_collaboration_admission.Id AND origin.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CloseCall = new("workflow.collaboration_control.close_call", """
        UPDATE fn_workflow_collaboration_admission SET StatusKey='cancel_requested',Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND BindingHash=@BindingHash AND StatusKey IN ('requested','created','active')
            AND (SourceTenantScopeKey=@TenantScopeKey OR TargetTenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CloseRejectedStart = new("workflow.collaboration_control.close_rejected_start", """
        UPDATE fn_workflow_collaboration_admission SET StatusKey='cancel_requested',Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND TargetTenantScopeKey=@TenantScopeKey AND BindingHash=@BindingHash
            AND Revision=@Revision AND StatusKey='requested' AND TargetInstanceId IS NULL
        """, SqlDataScope.Global);
    public static readonly SqlStatement CompleteSourceCall = new("workflow.collaboration_control.complete_source_call", """
        UPDATE fn_workflow_collaboration_call SET StatusKey=@StatusKey,Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND BindingHash=@BindingHash
            AND Revision=@CallRevision AND StatusKey='accepted'
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelSourceCall = new("workflow.collaboration_control.cancel_source_call", """
        UPDATE fn_workflow_collaboration_call SET StatusKey=@StatusKey,Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND BindingHash=@BindingHash
            AND Revision=@CallRevision AND StatusKey IN ('requested','accepted','result_ready','cancelling')
        """, SqlDataScope.Global);
    public static readonly SqlStatement FinishAdmission = new("workflow.collaboration_control.finish_admission", """
        UPDATE fn_workflow_collaboration_admission SET StatusKey=@NextAdmissionStatus,Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND SourceTenantScopeKey=@TenantScopeKey AND BindingHash=@BindingHash AND StatusKey=@AdmissionStatus
            AND EXISTS (SELECT 1 FROM fn_workflow_collaboration_control AS control
                WHERE control.Id=@ControlId AND control.CallId=fn_workflow_collaboration_admission.Id
                    AND control.SourceTenantScopeKey=@TenantScopeKey AND control.KindKey=@KindKey AND control.BodyHash=@BodyHash
                    AND control.StatusKey='applied')
            AND EXISTS (SELECT 1 FROM fn_workflow_collaboration_call AS callState WHERE callState.Id=@Id AND callState.TenantScopeKey=@TenantScopeKey
                AND callState.StatusKey IN ('completed','denied','cancelled'))
        """, SqlDataScope.Global);
    public static readonly SqlStatement ReleaseBudget = new("workflow.collaboration_control.release_budget", """
        UPDATE fn_workflow_exec_budget SET Reserved=Reserved-@UnusedActivities,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND Reserved>=@UnusedActivities AND @UnusedActivities>=0
            AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement ReadBudget = new("workflow.collaboration_control.read_budget", """
        SELECT COALESCE(SUM(facts.Granted-facts.Reserved-facts.UsedCount),0) AS UnusedActivities,COUNT(*) AS InstanceCount,
            COALESCE(SUM(CASE WHEN facts.Granted<facts.Reserved+facts.UsedCount OR facts.Granted<1 THEN 1 ELSE 0 END),0) AS InvalidBudgets
        FROM (SELECT origin.Id,COALESCE(budget.Granted,-1) AS Granted,COALESCE(budget.Reserved,0) AS Reserved,COUNT(step.Id) AS UsedCount
            FROM fn_workflow_collaboration_origin AS origin LEFT JOIN fn_workflow_exec_budget AS budget ON budget.InstanceId=origin.Id
            LEFT JOIN fn_workflow_step AS step ON step.InstanceId=origin.Id
            WHERE origin.RootInstanceId=@TargetInstanceId AND origin.TenantScopeKey=@TenantScopeKey
            GROUP BY origin.Id,budget.Granted,budget.Reserved) AS facts
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountUnsettled = new("workflow.collaboration_control.count_unsettled", """
        SELECT COUNT(*) FROM fn_workflow_collaboration_origin AS origin
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=origin.Id AND instance.TenantScopeKey=origin.TenantScopeKey
        WHERE origin.RootInstanceId=@TargetInstanceId AND origin.TenantScopeKey=@TenantScopeKey AND
            (instance.StatusKey NOT IN ('completed','rejected','cancelled') OR
             EXISTS (SELECT 1 FROM fn_workflow_activity_work AS work WHERE work.InstanceId=origin.Id AND work.TenantScopeKey=@TenantScopeKey AND work.StatusKey IN ('pending','leased','faulted')) OR
             EXISTS (SELECT 1 FROM fn_workflow_connector_call AS connector WHERE connector.InstanceId=origin.Id AND connector.TenantScopeKey=@TenantScopeKey AND
                ((connector.StatusKey NOT IN ('applied','cancelled') AND connector.AttemptCount>0) OR
        """ + WorkflowConnectorSettlementSql.Unresolved + """
                )) OR
             EXISTS (SELECT 1 FROM fn_workflow_subprocess_call AS childCall WHERE childCall.InstanceId=origin.Id AND childCall.TenantScopeKey=@TenantScopeKey AND childCall.StatusKey NOT IN ('applied','cancelled')) OR
             EXISTS (SELECT 1 FROM fn_workflow_business_cmd AS businessCall WHERE businessCall.InstanceId=origin.Id AND businessCall.TenantScopeKey=@TenantScopeKey AND
                (businessCall.CompensationRequested=1 OR businessCall.StatusKey NOT IN ('applied','cancelled','compensated','declined'))))
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountBlockedSource = new("workflow.collaboration_control.count_blocked_source", """
        SELECT COUNT(*) FROM fn_workflow_collaboration_call AS callState
        INNER JOIN fn_workflow_step AS step ON step.Id=callState.Id AND step.InstanceId=callState.InstanceId
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.Id=callState.ExecutionEpochId AND epoch.InstanceId=callState.InstanceId AND epoch.TenantScopeKey=callState.TenantScopeKey
        WHERE callState.InstanceId=@InstanceId AND callState.TenantScopeKey=@TenantScopeKey
            AND step.StatusKey='active' AND epoch.StatusKey='active' AND callState.StatusKey IN ('denied','cancelling','cancelled')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountUnsettledSource = new("workflow.collaboration_control.count_unsettled_source", """
        SELECT COUNT(*) FROM fn_workflow_collaboration_call AS callState
        LEFT JOIN fn_workflow_step AS step ON step.Id=callState.Id AND step.InstanceId=callState.InstanceId
        WHERE callState.InstanceId=@InstanceId AND callState.TenantScopeKey=@TenantScopeKey AND
            (step.Id IS NULL OR NOT ((callState.StatusKey='completed' AND step.StatusKey='completed') OR (callState.StatusKey IN ('denied','cancelled') AND step.StatusKey<>'active')))
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountRecoverableSource = new("workflow.collaboration_control.count_recoverable_source", """
        SELECT COUNT(*) FROM fn_workflow_collaboration_call AS callState
        INNER JOIN fn_workflow_step AS step ON step.Id=callState.Id AND step.InstanceId=callState.InstanceId AND step.ExecutionEpochId=callState.ExecutionEpochId
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.Id=callState.ExecutionEpochId AND epoch.InstanceId=callState.InstanceId AND epoch.TenantScopeKey=callState.TenantScopeKey
        LEFT JOIN fn_workflow_collaboration_admission AS admission ON admission.Id=callState.Id AND admission.SourceTenantScopeKey=callState.TenantScopeKey
        INNER JOIN fn_workflow_collaboration_grant AS grantRow ON grantRow.Id=callState.GrantId AND grantRow.SourceTenantScopeKey=callState.TenantScopeKey
        WHERE callState.InstanceId=@InstanceId AND callState.TenantScopeKey=@TenantScopeKey
            AND ((callState.StatusKey='preparing' AND admission.Id IS NULL) OR (callState.StatusKey IN ('requested','accepted','result_ready')
                AND admission.BindingHash=callState.BindingHash AND admission.TargetTenantScopeKey=grantRow.TargetTenantScopeKey AND admission.StatusKey IN ('requested','created','active')))
            AND step.StatusKey='active' AND epoch.StatusKey='active' AND grantRow.StatusKey='active' AND grantRow.ExpiresAtUtc>@Now
        """, SqlDataScope.Global);
}
