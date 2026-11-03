using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>请求与收件始终锚定源信封作用域；本批SQL不写目标实例或改变当前租户。</summary>
internal static class WorkflowCollaborationCallSql
{
    public static readonly SqlStatement FindAtNode = new("workflow.collaboration_call.find_at_node", """
        SELECT callState.Id,callState.InstanceId,callState.ExecutionEpochId,callState.TenantScopeKey,callState.NodeKey,callState.ContinuationNodeKey,
            callState.GrantId,callState.DeliveryId,callState.BindingJson,callState.BindingHash,callState.InputJson,callState.InputHash,callState.InputFormRevision,
            callState.ProjectionJson,callState.ProjectionHash,callState.ReservedActivities,callState.StatusKey,callState.Revision,callState.CreatedAtUtc,callState.UpdatedAtUtc
        FROM fn_workflow_collaboration_call AS callState
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=callState.InstanceId AND instance.TenantScopeKey=callState.TenantScopeKey
        INNER JOIN fn_workflow_step AS step ON step.Id=callState.Id AND step.InstanceId=callState.InstanceId AND step.ExecutionEpochId=callState.ExecutionEpochId
            AND step.NodeKey=callState.NodeKey AND step.NodeTypeKey='process.collaboration'
        WHERE callState.InstanceId=@InstanceId AND callState.ExecutionEpochId=@ExecutionEpochId
            AND callState.TenantScopeKey=@TenantScopeKey AND callState.NodeKey=@NodeKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement Find = new("workflow.collaboration_call.find", """
        SELECT callState.Id,callState.InstanceId,callState.ExecutionEpochId,callState.TenantScopeKey,callState.NodeKey,callState.ContinuationNodeKey,
            callState.GrantId,callState.DeliveryId,callState.BindingJson,callState.BindingHash,callState.InputJson,callState.InputHash,callState.InputFormRevision,
            callState.ProjectionJson,callState.ProjectionHash,callState.ReservedActivities,callState.StatusKey,callState.Revision,callState.CreatedAtUtc,callState.UpdatedAtUtc
        FROM fn_workflow_collaboration_call AS callState
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=callState.InstanceId AND instance.TenantScopeKey=callState.TenantScopeKey
        INNER JOIN fn_workflow_step AS step ON step.Id=callState.Id AND step.InstanceId=callState.InstanceId AND step.ExecutionEpochId=callState.ExecutionEpochId
            AND step.NodeKey=callState.NodeKey AND step.NodeTypeKey='process.collaboration'
        WHERE callState.Id=@Id AND callState.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertStep = new("workflow.collaboration_call.insert_step", """
        INSERT INTO fn_workflow_step (Id,InstanceId,ExecutionEpochId,NodeKey,NodeTypeKey,StatusKey,AssignedUserId,DueAtUtc,AttemptCount,
            Revision,ExecutionSequence,StartedAtUtc,CompletedAtUtc,ParallelJoinId,ParallelBranchKey)
        SELECT @Id,instance.Id,@ExecutionEpochId,@NodeKey,'process.collaboration','active',NULL,NULL,0,
            1,@ExecutionSequence,@Now,NULL,@ParallelJoinId,@ParallelBranchKey
        FROM fn_workflow_instance AS instance
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.InstanceId=instance.Id AND epoch.Id=@ExecutionEpochId
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
            AND epoch.TenantScopeKey=@TenantScopeKey AND epoch.StatusKey='active'
        """, SqlDataScope.Global);

    public static readonly SqlStatement Insert = new("workflow.collaboration_call.insert", """
        INSERT INTO fn_workflow_collaboration_call (Id,InstanceId,ExecutionEpochId,TenantScopeKey,NodeKey,ContinuationNodeKey,GrantId,DeliveryId,
            BindingJson,BindingHash,InputJson,InputHash,InputFormRevision,ProjectionJson,ProjectionHash,ReservedActivities,StatusKey,Revision,CreatedAtUtc,UpdatedAtUtc)
        SELECT @Id,@InstanceId,@ExecutionEpochId,@TenantScopeKey,@NodeKey,@ContinuationNodeKey,@GrantId,@DeliveryId,
            @BindingJson,@BindingHash,@InputJson,@InputHash,@InputFormRevision,@ProjectionJson,@ProjectionHash,@ReservedActivities,@StatusKey,1,@Now,@Now
        FROM fn_workflow_collaboration_grant AS grantRow
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        INNER JOIN fn_workflow_step AS step ON step.Id=@Id AND step.InstanceId=instance.Id AND step.ExecutionEpochId=@ExecutionEpochId
            AND step.NodeKey=@NodeKey AND step.NodeTypeKey='process.collaboration'
        WHERE grantRow.Id=@GrantId AND grantRow.SourceTenantScopeKey=@TenantScopeKey AND grantRow.StatusKey='active'
            AND @StatusKey IN ('preparing','requested')
            AND grantRow.Revision=@GrantRevision AND grantRow.PolicyHash=@PolicyHash AND grantRow.ExpiresAtUtc>@Now
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindDelivery = new("workflow.collaboration_delivery.find", """
        SELECT delivery.Id,delivery.CallId,delivery.TenantScopeKey,delivery.EnvelopeMessageId,delivery.BindingHash,delivery.StatusKey,delivery.Revision,delivery.ReceivedAtUtc
        FROM fn_workflow_collaboration_delivery AS delivery WHERE delivery.Id=@DeliveryId AND delivery.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertDelivery = new("workflow.collaboration_delivery.insert", """
        INSERT INTO fn_workflow_collaboration_delivery (Id,CallId,TenantScopeKey,EnvelopeMessageId,BindingHash,StatusKey,Revision,ReceivedAtUtc)
        SELECT callState.DeliveryId,callState.Id,callState.TenantScopeKey,@EnvelopeMessageId,callState.BindingHash,'received',1,@Now
        FROM fn_workflow_collaboration_call AS callState
        WHERE callState.Id=@Id AND callState.DeliveryId=@DeliveryId AND callState.TenantScopeKey=@TenantScopeKey AND callState.BindingHash=@BindingHash AND callState.StatusKey<>'preparing'
        """, SqlDataScope.Global);
}
