using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>区域事实只属于已锁定的真实根；所有读写显式携带可信作用域，不猜测当前代次。</summary>
internal static class WorkflowRegionSql
{
    public static readonly SqlStatement FindFrame = new("workflow.region_frame.find", """
        SELECT ExecutionEpochId,InstanceId,TenantScopeKey,DefinitionVersionId,EntryNodeKey,ExitNodeKey,
            ParentForkActivationId,ParentBranchKey,CreatedAtUtc
        FROM fn_workflow_region_frame
        WHERE ExecutionEpochId=@ExecutionEpochId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertFrame = new("workflow.region_frame.insert", """
        INSERT INTO fn_workflow_region_frame (ExecutionEpochId,InstanceId,TenantScopeKey,DefinitionVersionId,EntryNodeKey,
            ExitNodeKey,ParentForkActivationId,ParentBranchKey,CreatedAtUtc)
        SELECT epoch.Id,epoch.InstanceId,epoch.TenantScopeKey,instance.DefinitionVersionId,@EntryNodeKey,@ExitNodeKey,
            @ParentForkActivationId,@ParentBranchKey,@Now
        FROM fn_workflow_execution_epoch AS epoch
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=epoch.InstanceId
        WHERE epoch.Id=@ExecutionEpochId AND epoch.InstanceId=@InstanceId AND epoch.TenantScopeKey=@TenantScopeKey
            AND epoch.StatusKey='active' AND instance.StatusKey='active' AND instance.DefinitionVersionId=@DefinitionVersionId
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindRouteSource = new("workflow.region_frame.route_source", """
        SELECT step.Id,step.InstanceId,step.ExecutionEpochId,step.NodeKey,step.NodeTypeKey,step.StatusKey,
            step.ParallelJoinId,step.ParallelBranchKey,instance.DefinitionVersionId
        FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        WHERE step.Id=@StepId AND step.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
            AND instance.StatusKey='active' AND step.NodeTypeKey='control.route' AND step.StatusKey='completed'
        """, SqlDataScope.Global);
    public static readonly SqlStatement NextIteration = new("workflow.execution_epoch.next_iteration", """
        SELECT COALESCE(MAX(IterationNumber),0)+1 FROM fn_workflow_execution_epoch
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND ParentEpochId=@ParentEpochId AND RegionKey=@RegionKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountActiveRegion = new("workflow.execution_epoch.active_region", """
        SELECT COUNT(*) FROM fn_workflow_execution_epoch
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND ParentEpochId=@ParentEpochId
            AND RegionKey=@RegionKey AND StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountSourceEntries = new("workflow.execution_epoch.source_entries", """
        SELECT COUNT(*) FROM fn_workflow_execution_epoch
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND CauseStepId=@CauseStepId
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertEpoch = new("workflow.execution_epoch.insert_region", """
        INSERT INTO fn_workflow_execution_epoch (Id,InstanceId,TenantScopeKey,ParentEpochId,CauseStepId,RegionKey,
            IterationNumber,Depth,StatusKey,Revision,CreatedAtUtc,ClosedAtUtc)
        SELECT @ExecutionEpochId,parent.InstanceId,parent.TenantScopeKey,parent.Id,@CauseStepId,@RegionKey,
            @IterationNumber,@Depth,'active',1,@Now,NULL
        FROM fn_workflow_execution_epoch AS parent
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=parent.InstanceId
        WHERE parent.Id=@ParentEpochId AND parent.InstanceId=@InstanceId AND parent.TenantScopeKey=@TenantScopeKey
            AND parent.StatusKey='active' AND instance.StatusKey='active' AND instance.DefinitionVersionId=@DefinitionVersionId
        """, SqlDataScope.Global);
    public static readonly SqlStatement CloseEpoch = new("workflow.execution_epoch.close_region", """
        UPDATE fn_workflow_execution_epoch SET StatusKey='closed',ClosedAtUtc=@Now,Revision=Revision+1
        WHERE Id=@ExecutionEpochId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
            AND Id<>InstanceId AND RegionKey=@RegionKey AND Revision=@Revision AND StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountActiveChildren = new("workflow.execution_epoch.active_children", """
        SELECT COUNT(*) FROM fn_workflow_execution_epoch
        WHERE ParentEpochId=@ExecutionEpochId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountUnsettled = new("workflow.execution_epoch.unsettled", """
        SELECT
         (SELECT COUNT(*) FROM fn_workflow_step AS step INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
          WHERE step.InstanceId=@InstanceId AND step.ExecutionEpochId=@ExecutionEpochId AND instance.TenantScopeKey=@TenantScopeKey AND step.StatusKey='active')
         + (SELECT COUNT(*) FROM fn_workflow_activity_work AS work
          WHERE work.InstanceId=@InstanceId AND work.ExecutionEpochId=@ExecutionEpochId AND work.TenantScopeKey=@TenantScopeKey
            AND work.StatusKey IN ('pending','leased','faulted') AND (@WorkId IS NULL OR work.Id<>@WorkId))
        """, SqlDataScope.Global);
}

/// <summary>区域帧绑定实际根版本及外层分叉；空帧不代表可默认回到根。</summary>
internal sealed record WorkflowRegionFrameRecord(Guid ExecutionEpochId,Guid InstanceId,string TenantScopeKey,Guid DefinitionVersionId,
    string EntryNodeKey,string ExitNodeKey,Guid? ParentForkActivationId,string? ParentBranchKey,DateTimeOffset CreatedAtUtc);

/// <summary>进入区域的因果来源必须是本次已完成的真实路由步骤。</summary>
internal sealed record WorkflowRegionRouteSourceRecord(Guid Id,Guid InstanceId,Guid ExecutionEpochId,string NodeKey,string NodeTypeKey,
    string StatusKey,Guid? ParallelJoinId,string? ParallelBranchKey,Guid DefinitionVersionId);
