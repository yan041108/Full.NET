using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>退回轮次绑定原步骤、固定区域和唯一入出工作；普通同代次来源查询不因退回而放宽。</summary>
internal static class WorkflowReturnRoundSql
{
    private const string Projection = "Id,InstanceId,TenantScopeKey,DefinitionVersionId,ParentEpochId,SourceStepId,TargetStepId,EntryNodeKey,ExitNodeKey,NextNodeKey,RegionNodesJson,RegionHash,TargetSnapshotHash,ParallelJoinId,ParallelBranchKey,EntryWorkId,InputHash,InputFormRevision,ExitWorkId,ExitStepId,ExitInputHash,ExitInputFormRevision,Revision,CreatedAtUtc,ExitedAtUtc,SourceParallelJoinId,SourceParallelBranchKey,ExitSourceEpochId,EntrySourceEpochId";
    public static readonly SqlStatement Find = new("workflow.return_round.find", "SELECT " + Projection + """
         FROM fn_workflow_return_round WHERE Id=@ReturnRoundId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindStep = new("workflow.return_round.find_step", """
        SELECT step.Id,step.InstanceId,step.ExecutionEpochId,step.NodeKey,step.NodeTypeKey,step.StatusKey,
            step.ParallelJoinId,step.ParallelBranchKey,instance.DefinitionVersionId
        FROM fn_workflow_step AS step INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        WHERE step.Id=@StepId AND step.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindTargetSnapshot = new("workflow.return_round.find_target_snapshot", """
        SELECT step.AssigneeSnapshotJson,step.AssigneeSnapshotHash
        FROM fn_workflow_return_round AS round
        INNER JOIN fn_workflow_step AS step ON step.Id=round.TargetStepId AND step.InstanceId=round.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=round.InstanceId AND instance.TenantScopeKey=round.TenantScopeKey
        WHERE round.Id=@ReturnRoundId AND round.InstanceId=@InstanceId AND round.TenantScopeKey=@TenantScopeKey
          AND round.EntryWorkId=@WorkId AND round.EntryNodeKey=step.NodeKey AND step.StatusKey='rolled_back'
          AND round.DefinitionVersionId=instance.DefinitionVersionId AND step.AssigneeSnapshotHash=round.TargetSnapshotHash
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindCompletedSnapshot = new("workflow.return_round.find_completed_snapshot", """
        SELECT step.AssigneeSnapshotJson,step.AssigneeSnapshotHash
        FROM fn_workflow_step AS step INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        WHERE step.Id=@StepId AND step.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
          AND step.StatusKey='completed' AND step.NodeTypeKey IN ('human.approval','human.group_approval','human.task')
          AND instance.StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.return_round.insert", """
        INSERT INTO fn_workflow_return_round (Id,InstanceId,TenantScopeKey,DefinitionVersionId,ParentEpochId,SourceStepId,TargetStepId,
            EntryNodeKey,ExitNodeKey,NextNodeKey,RegionNodesJson,RegionHash,TargetSnapshotHash,ParallelJoinId,ParallelBranchKey,
            EntryWorkId,InputHash,InputFormRevision,ExitWorkId,ExitStepId,ExitInputHash,ExitInputFormRevision,Revision,CreatedAtUtc,ExitedAtUtc,SourceParallelJoinId,SourceParallelBranchKey,EntrySourceEpochId)
        SELECT epoch.Id,instance.Id,instance.TenantScopeKey,instance.DefinitionVersionId,epoch.ParentEpochId,source.Id,target.Id,
            @EntryNodeKey,@ExitNodeKey,@NextNodeKey,@RegionNodesJson,@RegionHash,@TargetSnapshotHash,@ParallelJoinId,@ParallelBranchKey,
            @EntryWorkId,@InputHash,@InputFormRevision,NULL,NULL,NULL,NULL,1,@Now,NULL,@SourceParallelJoinId,@SourceParallelBranchKey,source.ExecutionEpochId
        FROM fn_workflow_instance AS instance
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.InstanceId=instance.Id AND epoch.TenantScopeKey=instance.TenantScopeKey
        INNER JOIN fn_workflow_step AS source ON source.InstanceId=instance.Id AND source.Id=@SourceStepId AND source.ExecutionEpochId=@EntrySourceEpochId
        INNER JOIN fn_workflow_step AS target ON target.InstanceId=instance.Id AND target.Id=@TargetStepId
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
          AND instance.DefinitionVersionId=@DefinitionVersionId AND epoch.Id=@ReturnRoundId AND epoch.StatusKey='active'
          AND epoch.ParentEpochId=@ParentEpochId AND source.StatusKey='returned' AND target.StatusKey='rolled_back'
          AND target.NodeKey=@EntryNodeKey AND target.AssigneeSnapshotHash=@TargetSnapshotHash
        """, SqlDataScope.Global);
    public static readonly SqlStatement BindExit = new("workflow.return_round.bind_exit", """
        UPDATE fn_workflow_return_round SET ExitWorkId=@ExitWorkId,ExitStepId=@ExitStepId,ExitSourceEpochId=@ExitSourceEpochId,ExitInputHash=@ExitInputHash,
            ExitInputFormRevision=@ExitInputFormRevision,Revision=Revision+1,ExitedAtUtc=@Now
        WHERE Id=@ReturnRoundId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=1 AND ExitWorkId IS NULL
          AND EXISTS (SELECT 1 FROM fn_workflow_step AS step INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
              WHERE step.Id=@ExitStepId AND step.InstanceId=@InstanceId AND step.ExecutionEpochId=@ExitSourceEpochId
                AND step.NodeKey=fn_workflow_return_round.ExitNodeKey AND step.StatusKey='completed'
                AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
                AND instance.DefinitionVersionId=fn_workflow_return_round.DefinitionVersionId
                AND (step.ExecutionEpochId=@ReturnRoundId OR EXISTS (
                    SELECT 1 FROM fn_workflow_activity_work AS work
                    WHERE work.Id=@ParentReturnWorkId AND work.ReturnRoundId=@ParentReturnRoundId AND work.InstanceId=@InstanceId AND work.TenantScopeKey=@TenantScopeKey
                      AND work.ExecutionEpochId=@ReturnRoundId AND work.SourceStepId=step.Id AND work.NodeKey=fn_workflow_return_round.NextNodeKey
                      AND work.InputHash=@ParentInputHash AND work.InputFormRevision=@ParentInputFormRevision
                      AND work.StatusKey='leased' AND work.CauseKindKey='return' AND work.RouteDecisionId IS NULL AND work.ResumeBarrierId IS NULL)))
        """, SqlDataScope.Global);

    // 同一个谓词用于原子入队和重领校验，防止只在 Worker 入口接受宽松的祖先代次关系。
    private const string FramePredicate = """
          AND ((round.ParallelJoinId IS NULL AND @ParallelJoinId IS NULL AND round.ParallelBranchKey IS NULL AND @ParallelBranchKey IS NULL)
            OR (round.ParallelJoinId=@ParallelJoinId AND round.ParallelBranchKey=@ParallelBranchKey))
        """;
    private const string PhasePredicate = """
          AND ((round.EntryWorkId=@WorkId AND @ExecutionEpochId=round.Id AND @NodeKey=round.EntryNodeKey
              AND @SourceStepId=round.SourceStepId AND source.ExecutionEpochId=round.EntrySourceEpochId AND source.StatusKey='returned'
              AND @InputHash=round.InputHash AND @InputFormRevision=round.InputFormRevision AND round.Revision=1 AND ((round.EntrySourceEpochId=round.ParentEpochId AND sourceEpoch.StatusKey='active')
                  OR (round.EntrySourceEpochId<>round.ParentEpochId AND sourceEpoch.StatusKey='closed' AND EXISTS (SELECT 1 FROM fn_workflow_parallel_join AS oldFork
                      WHERE oldFork.Id=round.SourceParallelJoinId AND oldFork.InstanceId=round.InstanceId AND oldFork.StatusKey='cancelled'))))
            OR (round.ExitWorkId=@WorkId AND @ExecutionEpochId=round.ParentEpochId AND @NodeKey=round.NextNodeKey
              AND @SourceStepId=round.ExitStepId AND source.ExecutionEpochId=round.ExitSourceEpochId AND source.StatusKey='completed'
              AND @InputHash=round.ExitInputHash AND @InputFormRevision=round.ExitInputFormRevision AND round.Revision=2
              AND round.ExitedAtUtc IS NOT NULL AND sourceEpoch.StatusKey='closed'
              AND EXISTS (SELECT 1 FROM fn_workflow_execution_epoch AS closedRound WHERE closedRound.Id=round.Id AND closedRound.InstanceId=round.InstanceId AND closedRound.StatusKey='closed')))
          AND ((round.EntryWorkId=@WorkId AND ((source.ParallelJoinId IS NULL AND round.SourceParallelJoinId IS NULL AND source.ParallelBranchKey IS NULL)
              OR (source.ParallelJoinId=round.SourceParallelJoinId AND source.ParallelBranchKey=round.SourceParallelBranchKey)))
            OR (round.ExitWorkId=@WorkId AND ((source.ParallelJoinId IS NULL AND round.ParallelJoinId IS NULL AND source.ParallelBranchKey IS NULL)
              OR (source.ParallelJoinId=round.ParallelJoinId AND source.ParallelBranchKey=round.ParallelBranchKey))))
        """;
    public static readonly SqlStatement InsertWork = new("workflow.return_round.insert_work", """
        INSERT INTO fn_workflow_activity_work (Id,InstanceId,ExecutionEpochId,TenantScopeKey,NodeKey,InputJson,InputHash,InputFormRevision,
            ExecutionSequence,ParallelJoinId,ParallelBranchKey,DueAtUtc,StatusKey,LeaseGeneration,AttemptCount,SourceStepId,CauseKindKey,ReturnRoundId)
        SELECT @WorkId,round.InstanceId,@ExecutionEpochId,round.TenantScopeKey,@NodeKey,@InputJson,@InputHash,@InputFormRevision,
            @ExecutionSequence,@ParallelJoinId,@ParallelBranchKey,@Now,'pending',0,0,source.Id,'return',round.Id
        FROM fn_workflow_return_round AS round
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=round.InstanceId AND instance.TenantScopeKey=round.TenantScopeKey
        INNER JOIN fn_workflow_step AS source ON source.Id=@SourceStepId AND source.InstanceId=round.InstanceId
        INNER JOIN fn_workflow_execution_epoch AS sourceEpoch ON sourceEpoch.Id=source.ExecutionEpochId AND sourceEpoch.InstanceId=round.InstanceId
        INNER JOIN fn_workflow_execution_epoch AS targetEpoch ON targetEpoch.Id=@ExecutionEpochId AND targetEpoch.InstanceId=round.InstanceId
        WHERE round.Id=@ReturnRoundId AND round.InstanceId=@InstanceId AND round.TenantScopeKey=@TenantScopeKey
          AND instance.StatusKey='active' AND instance.DefinitionVersionId=round.DefinitionVersionId
          AND targetEpoch.StatusKey='active' AND targetEpoch.TenantScopeKey=round.TenantScopeKey
        """ + FramePredicate + PhasePredicate, SqlDataScope.Global);
    private const string WorkProof = """
        SELECT COUNT(*) FROM fn_workflow_activity_work AS work
        INNER JOIN fn_workflow_return_round AS round ON round.Id=work.ReturnRoundId AND round.InstanceId=work.InstanceId AND round.TenantScopeKey=work.TenantScopeKey
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=round.InstanceId AND instance.TenantScopeKey=round.TenantScopeKey
        INNER JOIN fn_workflow_step AS source ON source.Id=work.SourceStepId AND source.InstanceId=round.InstanceId
        INNER JOIN fn_workflow_execution_epoch AS sourceEpoch ON sourceEpoch.Id=source.ExecutionEpochId AND sourceEpoch.InstanceId=round.InstanceId
        INNER JOIN fn_workflow_execution_epoch AS targetEpoch ON targetEpoch.Id=work.ExecutionEpochId AND targetEpoch.InstanceId=round.InstanceId
        WHERE round.Id=@ReturnRoundId AND round.InstanceId=@InstanceId AND round.TenantScopeKey=@TenantScopeKey
          AND instance.StatusKey='active' AND instance.DefinitionVersionId=round.DefinitionVersionId
          AND targetEpoch.StatusKey='active' AND targetEpoch.TenantScopeKey=round.TenantScopeKey
          AND work.Id=@WorkId AND work.ExecutionEpochId=@ExecutionEpochId AND work.NodeKey=@NodeKey
          AND work.InputHash=@InputHash AND work.InputFormRevision=@InputFormRevision AND work.SourceStepId=@SourceStepId
          AND work.CauseKindKey='return' AND @CauseKindKey='return' AND work.RouteDecisionId IS NULL AND work.ResumeBarrierId IS NULL
          AND ((work.ParallelJoinId IS NULL AND @ParallelJoinId IS NULL AND work.ParallelBranchKey IS NULL AND @ParallelBranchKey IS NULL)
            OR (work.ParallelJoinId=@ParallelJoinId AND work.ParallelBranchKey=@ParallelBranchKey))
        """ + FramePredicate + PhasePredicate;
    public static readonly SqlStatement CountWorkProof = new("workflow.return_round.work_proof", WorkProof, SqlDataScope.Global);
    public static readonly SqlStatement CountJoinSourceProof = new("workflow.return_round.join_source_proof", WorkProof + """
          AND work.StatusKey='leased' AND work.NodeKey=@JoinNodeKey
        """, SqlDataScope.Global);
}

/// <summary>新代次本身就是轮次身份；原目标及入队输入始终不变，正常退出最多CAS一次。</summary>
internal sealed record WorkflowReturnRoundRecord(Guid Id, Guid InstanceId, string TenantScopeKey, Guid DefinitionVersionId,
    Guid ParentEpochId, Guid SourceStepId, Guid TargetStepId, string EntryNodeKey, string ExitNodeKey, string NextNodeKey,
    string RegionNodesJson, string RegionHash, string TargetSnapshotHash, Guid? ParallelJoinId, string? ParallelBranchKey,
    Guid EntryWorkId, string InputHash, long InputFormRevision, Guid? ExitWorkId, Guid? ExitStepId, string? ExitInputHash,
    long? ExitInputFormRevision, long Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset? ExitedAtUtc,
    Guid? SourceParallelJoinId, string? SourceParallelBranchKey, Guid? ExitSourceEpochId, Guid EntrySourceEpochId);
