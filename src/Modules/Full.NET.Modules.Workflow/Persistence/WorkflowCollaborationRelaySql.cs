using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>共享准入只协调创建与关闭竞争；源和目标实例仍由各自本地事务写入。</summary>
internal static class WorkflowCollaborationRelaySql
{
    public static readonly SqlStatement ResolveResourceRoot = new("workflow.collaboration_origin.resource_root", """
        SELECT instance.Id FROM fn_workflow_instance AS instance
        WHERE instance.TenantScopeKey=@TenantScopeKey AND (
            (@Kind='todo' AND EXISTS (SELECT 1 FROM fn_workflow_todo AS todo WHERE todo.Id=@ResourceId AND todo.InstanceId=instance.Id)) OR
            (@Kind='pool' AND EXISTS (SELECT 1 FROM fn_workflow_claim AS pool WHERE pool.Id=@ResourceId AND pool.InstanceId=instance.Id)) OR
            (@Kind='cc' AND EXISTS (SELECT 1 FROM fn_workflow_cc AS cc WHERE cc.Id=@ResourceId AND cc.InstanceId=instance.Id)) OR
            (@Kind='recovery' AND EXISTS (SELECT 1 FROM fn_workflow_recovery_task AS task WHERE task.Id=@ResourceId AND task.InstanceId=instance.Id)))
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertAdmission = new("workflow.collaboration_admission.insert", """
        INSERT INTO fn_workflow_collaboration_admission
            (Id,SourceTenantScopeKey,TargetTenantScopeKey,DeliveryId,GrantId,BindingJson,BindingHash,ProjectionJson,ProjectionHash,StatusKey,TargetInstanceId,Revision,CreatedAtUtc,UpdatedAtUtc)
        SELECT callState.Id,callState.TenantScopeKey,@TargetTenantScopeKey,callState.DeliveryId,callState.GrantId,
            callState.BindingJson,callState.BindingHash,callState.ProjectionJson,callState.ProjectionHash,'requested',NULL,1,@Now,@Now
        FROM fn_workflow_collaboration_call AS callState
        INNER JOIN fn_workflow_collaboration_grant AS grantRow ON grantRow.Id=callState.GrantId
            AND grantRow.SourceTenantScopeKey=callState.TenantScopeKey AND grantRow.TargetTenantScopeKey=@TargetTenantScopeKey
        WHERE callState.Id=@Id AND callState.TenantScopeKey=@TenantScopeKey AND callState.BindingHash=@BindingHash AND callState.StatusKey='requested'
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindAdmission = new("workflow.collaboration_admission.find", """
        SELECT Id,SourceTenantScopeKey,TargetTenantScopeKey,DeliveryId,GrantId,BindingJson,BindingHash,ProjectionJson,ProjectionHash,
            StatusKey,TargetInstanceId,Revision,CreatedAtUtc,UpdatedAtUtc
        FROM fn_workflow_collaboration_admission
        WHERE Id=@Id AND (SourceTenantScopeKey=@TenantScopeKey OR TargetTenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement PendingSqlServer = new("workflow.collaboration_admission.pending_sqlserver", """
        SELECT TOP (@Take) admission.Id,admission.SourceTenantScopeKey,admission.TargetTenantScopeKey,admission.UpdatedAtUtc
        FROM fn_workflow_collaboration_admission AS admission
        INNER JOIN fn_workflow_collaboration_delivery AS delivery ON delivery.Id=admission.DeliveryId AND delivery.CallId=admission.Id
            AND delivery.TenantScopeKey=admission.SourceTenantScopeKey AND delivery.BindingHash=admission.BindingHash
        WHERE admission.StatusKey IN ('requested','created','active','cancel_requested')
            AND (@HasAfter=0 OR admission.UpdatedAtUtc>@AfterUpdatedAtUtc OR (admission.UpdatedAtUtc=@AfterUpdatedAtUtc AND admission.Id>@AfterId))
        ORDER BY admission.UpdatedAtUtc,admission.Id
        """, SqlDataScope.Global);
    public static readonly SqlStatement PendingMySql = new("workflow.collaboration_admission.pending_mysql", """
        SELECT admission.Id,admission.SourceTenantScopeKey,admission.TargetTenantScopeKey,admission.UpdatedAtUtc
        FROM fn_workflow_collaboration_admission AS admission
        INNER JOIN fn_workflow_collaboration_delivery AS delivery ON delivery.Id=admission.DeliveryId AND delivery.CallId=admission.Id
            AND delivery.TenantScopeKey=admission.SourceTenantScopeKey AND delivery.BindingHash=admission.BindingHash
        WHERE admission.StatusKey IN ('requested','created','active','cancel_requested')
            AND (@HasAfter=0 OR admission.UpdatedAtUtc>@AfterUpdatedAtUtc OR (admission.UpdatedAtUtc=@AfterUpdatedAtUtc AND admission.Id>@AfterId))
        ORDER BY admission.UpdatedAtUtc,admission.Id LIMIT @Take
        """, SqlDataScope.Global);
    public static readonly SqlStatement LockAdmission = new("workflow.collaboration_admission.lock", """
        UPDATE fn_workflow_collaboration_admission SET Revision=Revision
        WHERE Id=@Id AND TargetTenantScopeKey=@TenantScopeKey AND BindingHash=@BindingHash AND StatusKey='requested'
            AND EXISTS (SELECT 1 FROM fn_workflow_collaboration_delivery AS delivery
                WHERE delivery.Id=fn_workflow_collaboration_admission.DeliveryId AND delivery.CallId=fn_workflow_collaboration_admission.Id
                    AND delivery.TenantScopeKey=fn_workflow_collaboration_admission.SourceTenantScopeKey
                    AND delivery.BindingHash=fn_workflow_collaboration_admission.BindingHash)
        """, SqlDataScope.Global);
    public static readonly SqlStatement LockGrant = new("workflow.collaboration_admission.lock_grant", """
        UPDATE fn_workflow_collaboration_grant SET Revision=Revision
        WHERE Id=@GrantId AND TargetTenantScopeKey=@TenantScopeKey AND SourceTenantScopeKey=@SourceTenantScopeKey
            AND StatusKey='active' AND Revision=@GrantRevision AND PolicyHash=@PolicyHash AND ExpiresAtUtc>@Now
        """, SqlDataScope.Global);
    public static readonly SqlStatement LockActiveAdmission = new("workflow.collaboration_admission.lock_active", """
        UPDATE fn_workflow_collaboration_admission SET Revision=Revision
        WHERE Id=@Id AND TargetTenantScopeKey=@TenantScopeKey AND BindingHash=@BindingHash
            AND StatusKey IN ('created','active') AND TargetInstanceId=@RootInstanceId
        """, SqlDataScope.Global);
    public static readonly SqlStatement MarkCreated = new("workflow.collaboration_admission.mark_created", """
        UPDATE fn_workflow_collaboration_admission SET StatusKey='created',TargetInstanceId=@TargetInstanceId,Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND TargetTenantScopeKey=@TenantScopeKey AND StatusKey='requested' AND BindingHash=@BindingHash AND TargetInstanceId IS NULL
        """, SqlDataScope.Global);
    public static readonly SqlStatement MarkAccepted = new("workflow.collaboration_admission.mark_accepted", """
        UPDATE fn_workflow_collaboration_call SET StatusKey='accepted',Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND TenantScopeKey=@TenantScopeKey AND StatusKey='requested' AND BindingHash=@BindingHash
            AND EXISTS (SELECT 1 FROM fn_workflow_collaboration_admission AS admission
                WHERE admission.Id=fn_workflow_collaboration_call.Id AND admission.SourceTenantScopeKey=@TenantScopeKey
                    AND admission.BindingHash=@BindingHash AND admission.StatusKey='created' AND admission.TargetInstanceId=@TargetInstanceId)
            AND EXISTS (SELECT 1 FROM fn_workflow_collaboration_control AS control WHERE control.Id=@ControlId AND control.CallId=@Id
                AND control.SourceTenantScopeKey=@TenantScopeKey AND control.KindKey='created' AND control.BodyHash=@BodyHash AND control.StatusKey='received')
        """, SqlDataScope.Global);
    public static readonly SqlStatement ConfirmAccepted = new("workflow.collaboration_admission.confirm_accepted", """
        UPDATE fn_workflow_collaboration_admission SET StatusKey='active',Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND SourceTenantScopeKey=@TenantScopeKey AND BindingHash=@BindingHash AND StatusKey='created' AND TargetInstanceId=@TargetInstanceId
            AND EXISTS (SELECT 1 FROM fn_workflow_collaboration_call AS callState
                WHERE callState.Id=fn_workflow_collaboration_admission.Id AND callState.TenantScopeKey=@TenantScopeKey AND callState.StatusKey='accepted')
            AND EXISTS (SELECT 1 FROM fn_workflow_collaboration_control AS control WHERE control.Id=@ControlId AND control.CallId=@Id
                AND control.SourceTenantScopeKey=@TenantScopeKey AND control.KindKey='created' AND control.BodyHash=@BodyHash AND control.StatusKey='applied')
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertOrigin = new("workflow.collaboration_origin.insert", """
        INSERT INTO fn_workflow_collaboration_origin (Id,CallId,RootInstanceId,TenantScopeKey,Depth)
        SELECT instance.Id,@CallId,@RootInstanceId,instance.TenantScopeKey,@Depth FROM fn_workflow_instance AS instance
        INNER JOIN fn_workflow_collaboration_admission AS admission ON admission.Id=@CallId AND admission.TargetTenantScopeKey=instance.TenantScopeKey
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND admission.TargetInstanceId=@RootInstanceId
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindOrigin = new("workflow.collaboration_origin.find", """
        SELECT origin.Id,origin.CallId,origin.RootInstanceId,origin.TenantScopeKey,origin.Depth
        FROM fn_workflow_collaboration_origin AS origin
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=origin.Id AND instance.TenantScopeKey=origin.TenantScopeKey
        WHERE origin.Id=@InstanceId AND origin.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement RequestCancellation = new("workflow.collaboration_admission.request_cancellation", """
        UPDATE fn_workflow_collaboration_admission SET StatusKey='cancel_requested',Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE SourceTenantScopeKey=@TenantScopeKey AND StatusKey IN ('requested','created','active')
            AND EXISTS (SELECT 1 FROM fn_workflow_collaboration_call AS callState
                WHERE callState.Id=fn_workflow_collaboration_admission.Id AND callState.InstanceId=@InstanceId AND callState.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CloseInvalidated = new("workflow.collaboration_admission.close_invalidated", """
        UPDATE fn_workflow_collaboration_admission SET StatusKey='cancel_requested',Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE SourceTenantScopeKey=@TenantScopeKey AND StatusKey IN ('requested','created','active')
            AND EXISTS (SELECT 1 FROM fn_workflow_collaboration_call AS callState
                INNER JOIN fn_workflow_step AS step ON step.Id=callState.Id AND step.InstanceId=callState.InstanceId
                INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.Id=callState.ExecutionEpochId AND epoch.InstanceId=callState.InstanceId
                    AND epoch.TenantScopeKey=callState.TenantScopeKey
                WHERE callState.Id=fn_workflow_collaboration_admission.Id AND callState.InstanceId=@InstanceId AND callState.TenantScopeKey=@TenantScopeKey
                    AND (step.StatusKey<>'active' OR epoch.StatusKey<>'active'))
        """, SqlDataScope.Global);
}
