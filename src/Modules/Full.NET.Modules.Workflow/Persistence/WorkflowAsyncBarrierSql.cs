using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>屏障只绑定原实例、分支及固定依赖；续办证明在同一租约事务内一次释放。</summary>
internal static class WorkflowAsyncBarrierSql
{
    private const string Projection = "barrier.Id,barrier.InstanceId,barrier.TenantScopeKey,barrier.NodeKey,barrier.DependencyCallIdsJson,barrier.DependencyHash,barrier.InputJson,barrier.InputHash,barrier.InputFormRevision,barrier.ParallelJoinId,barrier.ParallelBranchKey,barrier.StatusKey,barrier.LeaseGeneration,barrier.LeaseOwnerKey,barrier.LeaseExpiresAtUtc,barrier.FailureCount,barrier.Revision,barrier.ResumeWorkId,barrier.OutputJson,barrier.OutputHash,barrier.OutputFormRevision,barrier.DueAtUtc,barrier.CreatedAtUtc,barrier.CompletedAtUtc,barrier.ErrorKey";
    private const string Due = """
         FROM fn_workflow_async_barrier AS barrier
         INNER JOIN fn_workflow_instance AS instance ON instance.Id=barrier.InstanceId AND instance.TenantScopeKey=barrier.TenantScopeKey
         INNER JOIN fn_workflow_step AS step ON step.Id=barrier.Id AND step.InstanceId=instance.Id
         WHERE instance.StatusKey='active' AND step.StatusKey='active' AND barrier.DueAtUtc<=@Now
           AND (barrier.StatusKey='pending' OR (barrier.StatusKey='applying' AND barrier.LeaseExpiresAtUtc<=@Now))
         ORDER BY barrier.DueAtUtc,barrier.Id
        """;
    public static readonly SqlStatement ListDueSqlServer = new("workflow.async_barrier.due.sqlserver", "SELECT TOP (@Take) " + Projection + Due, SqlDataScope.Global);
    public static readonly SqlStatement ListDueMySql = new("workflow.async_barrier.due.mysql", "SELECT " + Projection + Due + " LIMIT @Take", SqlDataScope.Global);
    public static readonly SqlStatement Find = new("workflow.async_barrier.find", "SELECT " + Projection + """
         FROM fn_workflow_async_barrier AS barrier WHERE barrier.Id=@Id AND barrier.InstanceId=@InstanceId AND barrier.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindResume = new("workflow.async_barrier.find_resume", "SELECT " + Projection + """
         FROM fn_workflow_async_barrier AS barrier WHERE barrier.Id=@BarrierId AND barrier.InstanceId=@InstanceId AND barrier.TenantScopeKey=@TenantScopeKey
           AND barrier.StatusKey='released' AND barrier.ResumeWorkId=@WorkId AND barrier.NodeKey=@NodeKey
           AND barrier.OutputHash=@InputHash AND barrier.OutputFormRevision=@InputFormRevision
           AND EXISTS (SELECT 1 FROM fn_workflow_step AS step WHERE step.Id=barrier.Id AND step.InstanceId=barrier.InstanceId AND step.StatusKey='completed')
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertStep = new("workflow.async_barrier.insert_step", """
        INSERT INTO fn_workflow_step
          (Id,InstanceId,NodeKey,NodeTypeKey,StatusKey,AssignedUserId,DueAtUtc,AttemptCount,Revision,ExecutionSequence,StartedAtUtc,CompletedAtUtc,ParallelJoinId,ParallelBranchKey)
        VALUES (@Id,@InstanceId,@NodeKey,'internal.await','active',NULL,NULL,0,1,@ExecutionSequence,@Now,NULL,@ParallelJoinId,@ParallelBranchKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.async_barrier.insert", """
        INSERT INTO fn_workflow_async_barrier
          (Id,InstanceId,TenantScopeKey,NodeKey,DependencyCallIdsJson,DependencyHash,InputJson,InputHash,InputFormRevision,ParallelJoinId,ParallelBranchKey,
           StatusKey,LeaseGeneration,FailureCount,Revision,DueAtUtc,CreatedAtUtc)
        VALUES (@Id,@InstanceId,@TenantScopeKey,@NodeKey,@DependencyCallIdsJson,@DependencyHash,@InputJson,@InputHash,@InputFormRevision,@ParallelJoinId,@ParallelBranchKey,
          'pending',0,0,1,@Now,@Now)
        """, SqlDataScope.Global);
    public static readonly SqlStatement Claim = new("workflow.async_barrier.claim", """
        UPDATE fn_workflow_async_barrier SET StatusKey='applying',LeaseOwnerKey=@Owner,LeaseExpiresAtUtc=@Expires,
          LeaseGeneration=LeaseGeneration+1,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision AND DueAtUtc<=@Now
          AND (StatusKey='pending' OR (StatusKey='applying' AND LeaseExpiresAtUtc<=@Now))
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
          AND EXISTS (SELECT 1 FROM fn_workflow_step AS step WHERE step.Id=@Id AND step.InstanceId=@InstanceId AND step.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement Yield = new("workflow.async_barrier.yield", """
        UPDATE fn_workflow_async_barrier SET StatusKey=@StatusKey,LeaseOwnerKey=NULL,LeaseExpiresAtUtc=NULL,FailureCount=@FailureCount,
          ErrorKey=@ErrorKey,DueAtUtc=@DueAtUtc,CompletedAtUtc=CASE WHEN @StatusKey='failed' THEN @Now ELSE NULL END,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='applying'
          AND LeaseGeneration=@Generation AND LeaseOwnerKey=@Owner AND LeaseExpiresAtUtc>@Now
        """, SqlDataScope.Global);
    public static readonly SqlStatement Release = new("workflow.async_barrier.release", """
        UPDATE fn_workflow_async_barrier SET StatusKey='released',ResumeWorkId=@WorkId,OutputJson=@OutputJson,OutputHash=@OutputHash,
          OutputFormRevision=@OutputFormRevision,LeaseOwnerKey=NULL,LeaseExpiresAtUtc=NULL,CompletedAtUtc=@Now,ErrorKey=NULL,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='applying'
          AND LeaseGeneration=@Generation AND LeaseOwnerKey=@Owner AND LeaseExpiresAtUtc>@Now
          AND EXISTS (SELECT 1 FROM fn_workflow_activity_work AS work WHERE work.Id=@WorkId AND work.InstanceId=@InstanceId
            AND work.TenantScopeKey=@TenantScopeKey AND work.NodeKey=@NodeKey AND work.ResumeBarrierId=@Id AND work.InputHash=@OutputHash AND work.InputFormRevision=@OutputFormRevision)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelInstance = new("workflow.async_barrier.cancel_instance", """
        UPDATE fn_workflow_async_barrier SET StatusKey='cancelled',LeaseOwnerKey=NULL,LeaseExpiresAtUtc=NULL,LeaseGeneration=LeaseGeneration+1,
          CompletedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey IN ('pending','applying')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountRecoverable = new("workflow.async_barrier.count_recoverable", """
        SELECT COUNT(*) FROM fn_workflow_async_barrier WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey IN ('pending','applying')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountBlocked = new("workflow.async_barrier.count_blocked", """
        SELECT COUNT(*) FROM fn_workflow_async_barrier WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='failed'
        """, SqlDataScope.Global);
}

/// <summary>输入与依赖在激活后不可变；输出仅在原租约释放时关联唯一续办工作项。</summary>
internal sealed record WorkflowAsyncBarrierRecord(Guid Id,Guid InstanceId,string TenantScopeKey,string NodeKey,string DependencyCallIdsJson,string DependencyHash,
    string InputJson,string InputHash,long InputFormRevision,Guid? ParallelJoinId,string? ParallelBranchKey,string StatusKey,long LeaseGeneration,string? LeaseOwnerKey,
    DateTimeOffset? LeaseExpiresAtUtc,int FailureCount,long Revision,Guid? ResumeWorkId,string? OutputJson,string? OutputHash,long? OutputFormRevision,
    DateTimeOffset DueAtUtc,DateTimeOffset CreatedAtUtc,DateTimeOffset? CompletedAtUtc,string? ErrorKey);
