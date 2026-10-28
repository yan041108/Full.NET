using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>因果事实只连接同一可信根的实际步骤；既有记录缺少来源时不猜测补边。</summary>
internal static class WorkflowCausalitySql
{
    // 普通来源属于同一实际帧；分支首步可由该次真实fork产生，不能借另一分支同名节点作为来源。
    internal const string SourceFramePredicate = """
         AND (((source.ParallelJoinId=@ParallelJoinId OR (source.ParallelJoinId IS NULL AND @ParallelJoinId IS NULL))
           AND (source.ParallelBranchKey=@ParallelBranchKey OR (source.ParallelBranchKey IS NULL AND @ParallelBranchKey IS NULL)))
          OR EXISTS (SELECT 1 FROM fn_workflow_parallel_join AS fork
            INNER JOIN fn_workflow_branch_state AS selected ON selected.ParallelJoinId=fork.Id AND selected.BranchKey=@ParallelBranchKey
            WHERE fork.Id=@ParallelJoinId AND fork.InstanceId=source.InstanceId AND fork.ExecutionEpochId=source.ExecutionEpochId
              AND fork.ForkStepId=source.Id AND fork.StatusKey='waiting'))
        """;
    public static readonly SqlStatement CountSourceFrame = new("workflow.causality.count_source_frame", """
        SELECT COUNT(*) FROM fn_workflow_step AS source
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=source.InstanceId
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.Id=source.ExecutionEpochId AND epoch.InstanceId=instance.Id
        WHERE source.Id=@SourceStepId AND source.InstanceId=@InstanceId AND source.ExecutionEpochId=@ExecutionEpochId
          AND instance.TenantScopeKey=@TenantScopeKey AND epoch.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active' AND epoch.StatusKey='active'
        """ + SourceFramePredicate,SqlDataScope.Global);
    public static readonly SqlStatement InsertStart = new("workflow.causality.insert_start", """
        INSERT INTO fn_workflow_step (Id,InstanceId,ExecutionEpochId,NodeKey,NodeTypeKey,StatusKey,AssignedUserId,
            DueAtUtc,AttemptCount,Revision,ExecutionSequence,StartedAtUtc,CompletedAtUtc,ParallelJoinId,ParallelBranchKey)
        SELECT instance.Id,instance.Id,epoch.Id,@NodeKey,'start','completed',NULL,NULL,0,1,0,@Now,@Now,NULL,NULL
        FROM fn_workflow_instance AS instance
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.Id=instance.Id AND epoch.InstanceId=instance.Id AND epoch.TenantScopeKey=instance.TenantScopeKey
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active' AND epoch.StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertEdge = new("workflow.causality.insert_edge", """
        INSERT INTO fn_workflow_step_cause (Id,InstanceId,TenantScopeKey,StepId,ParentStepId,KindKey,CreatedAtUtc)
        SELECT @Id,child.InstanceId,instance.TenantScopeKey,child.Id,parent.Id,@KindKey,@Now
        FROM fn_workflow_step AS child
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=child.InstanceId
        INNER JOIN fn_workflow_step AS parent ON parent.Id=@ParentStepId AND parent.InstanceId=child.InstanceId
        WHERE child.Id=@StepId AND child.InstanceId=@InstanceId AND child.Id<>parent.Id AND instance.TenantScopeKey=@TenantScopeKey
          AND child.ExecutionSequence>parent.ExecutionSequence AND instance.StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement ListEdges = new("workflow.causality.list_edges", """
        SELECT Id,InstanceId,TenantScopeKey,StepId,ParentStepId,KindKey,CreatedAtUtc FROM fn_workflow_step_cause
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        ORDER BY CreatedAtUtc,Id
        """, SqlDataScope.Global);
    public static readonly SqlStatement ArrivalSources = new("workflow.causality.arrival_sources", """
        SELECT arrival.SourceStepId FROM fn_workflow_parallel_branch_arrival AS arrival
        INNER JOIN fn_workflow_parallel_join AS activation ON activation.Id=arrival.ParallelJoinId AND activation.InstanceId=arrival.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=activation.InstanceId
        WHERE activation.Id=@ParallelJoinId AND activation.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        ORDER BY arrival.BranchKey
        """, SqlDataScope.Global);
}

/// <summary>不可变边保留实际父步骤；顺序号仅用于存储时阻止自环/倒向，不用于选择业务来源。</summary>
internal sealed record WorkflowStepCauseRecord(Guid Id,Guid InstanceId,string TenantScopeKey,Guid StepId,Guid ParentStepId,
    string KindKey,DateTimeOffset CreatedAtUtc);
