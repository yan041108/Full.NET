using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>字段活动步骤和不可变结果只在持租执行事务内写入；重复激活由工作身份主键拒绝。</summary>
internal static class WorkflowFieldActivitySql
{
    public static readonly SqlStatement InsertStep = new("workflow.field_activity.insert_step", """
        INSERT INTO fn_workflow_step
          (Id,InstanceId, ExecutionEpochId,NodeKey,NodeTypeKey,StatusKey,AssignedUserId,DueAtUtc,AttemptCount,Revision,
           ExecutionSequence,StartedAtUtc,CompletedAtUtc,ParallelJoinId,ParallelBranchKey)
        VALUES (@Id,@InstanceId, @ExecutionEpochId,@NodeKey,@NodeTypeKey,'completed',NULL,NULL,0,1,
           @ExecutionSequence,@Now,@Now,@ParallelJoinId,@ParallelBranchKey)
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertResult = new("workflow.field_activity.insert_result", """
        INSERT INTO fn_workflow_activity_result
          (Id,InstanceId,TenantScopeKey,InputFormRevision,OutputFormRevision,InputHash,OutputHash,
           InputJson,OutputJson,PatchJson,OutcomeKey,CreatedAtUtc)
        VALUES (@Id,@InstanceId,@TenantScopeKey,@InputFormRevision,@OutputFormRevision,@InputHash,@OutputHash,
           @InputJson,@OutputJson,@PatchJson,@OutcomeKey,@Now)
        """, SqlDataScope.Global);
}
