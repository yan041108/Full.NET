using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>延时事实属于Workflow本模块，所有读取和变更均锚定持久实例作用域。</summary>
internal static class WorkflowDelaySql
{
    public static readonly SqlStatement InsertStep = new("workflow.delay.insert_step", """
        INSERT INTO fn_workflow_step
          (Id, InstanceId, ExecutionEpochId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId, DueAtUtc,
           AttemptCount, Revision, ExecutionSequence, StartedAtUtc, CompletedAtUtc, ParallelJoinId, ParallelBranchKey)
        VALUES (@Id, @InstanceId, @ExecutionEpochId, @NodeKey, 'timer.delay', 'active', NULL, @DueAtUtc,
           0, 1, @ExecutionSequence, @Now, NULL, @ParallelJoinId, @ParallelBranchKey)
        """, SqlDataScope.Global);

    public static readonly SqlStatement Insert = new("workflow.delay.insert", """
        INSERT INTO fn_workflow_delay
          (Id, InstanceId, TenantScopeKey, NodeKey, ContinuationNodeKey, InputJson, InputHash, InputFormRevision,
           ParallelJoinId, ParallelBranchKey, ModeKey, TimeZoneKey, DueAtUtc, RemainingMilliseconds,
           PausedAtUtc, StatusKey, Revision, CreatedAtUtc, CompletedAtUtc,CalendarVersionId,CalendarHash,CalendarJson)
        VALUES (@Id, @InstanceId, @TenantScopeKey, @NodeKey, @ContinuationNodeKey, @InputJson, @InputHash, @InputFormRevision,
           @ParallelJoinId, @ParallelBranchKey, @ModeKey, @TimeZoneKey, @DueAtUtc, NULL,
           NULL, 'waiting', 1, @Now, NULL,@CalendarVersionId,@CalendarHash,@CalendarJson)
        """, SqlDataScope.Global);

    private const string Projection = "delay.Id, delay.InstanceId, delay.TenantScopeKey, delay.NodeKey, delay.ContinuationNodeKey, delay.InputJson, delay.InputHash, delay.InputFormRevision, delay.ParallelJoinId, delay.ParallelBranchKey, delay.Revision";
    private const string Due = """
        FROM fn_workflow_delay AS delay
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=delay.InstanceId AND instance.TenantScopeKey=delay.TenantScopeKey
        INNER JOIN fn_workflow_step AS step ON step.Id=delay.Id AND step.InstanceId=delay.InstanceId
        WHERE instance.StatusKey='active' AND step.StatusKey='active' AND delay.StatusKey='waiting'
          AND delay.PausedAtUtc IS NULL AND delay.DueAtUtc<=@Now
        ORDER BY delay.DueAtUtc, delay.Id
        """;
    public static readonly SqlStatement ListDueSqlServer = new("workflow.delay.due.sqlserver", "SELECT TOP (@Take) " + Projection + " " + Due, SqlDataScope.Global);
    public static readonly SqlStatement ListDueMySql = new("workflow.delay.due.mysql", "SELECT " + Projection + " " + Due + " LIMIT @Take", SqlDataScope.Global);

    public static readonly SqlStatement Fire = new("workflow.delay.fire", """
        UPDATE fn_workflow_delay SET StatusKey='fired', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
          AND Revision=@Revision AND StatusKey='waiting' AND PausedAtUtc IS NULL AND DueAtUtc<=@Now
        """, SqlDataScope.Global);

    public static readonly SqlStatement CountWaiting = new("workflow.delay.count_waiting", """
        SELECT COUNT(*) FROM fn_workflow_delay
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='waiting'
        """, SqlDataScope.Global);

    public static readonly SqlStatement PauseRelativeSqlServer = new("workflow.delay.pause_relative.sqlserver", """
        UPDATE fn_workflow_delay SET RemainingMilliseconds=CASE WHEN DueAtUtc>@Now THEN DATEDIFF_BIG(millisecond,@Now,DueAtUtc) ELSE 0 END,
          PausedAtUtc=@Now, Revision=Revision+1
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='waiting'
          AND ModeKey='relative' AND PausedAtUtc IS NULL
        """, SqlDataScope.Global);
    public static readonly SqlStatement PauseRelativeMySql = new("workflow.delay.pause_relative.mysql", """
        UPDATE fn_workflow_delay SET RemainingMilliseconds=GREATEST(0,TIMESTAMPDIFF(MICROSECOND,@Now,DueAtUtc) DIV 1000),
          PausedAtUtc=@Now, Revision=Revision+1
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='waiting'
          AND ModeKey='relative' AND PausedAtUtc IS NULL
        """, SqlDataScope.Global);
    public static readonly SqlStatement ResumeRelativeSqlServer = new("workflow.delay.resume_relative.sqlserver", """
        UPDATE fn_workflow_delay SET DueAtUtc=DATEADD(millisecond,CAST(RemainingMilliseconds%1000 AS int),DATEADD(second,CAST(RemainingMilliseconds/1000 AS int),@Now)),
          RemainingMilliseconds=NULL, PausedAtUtc=NULL, Revision=Revision+1
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='waiting'
          AND ModeKey='relative' AND PausedAtUtc IS NOT NULL AND RemainingMilliseconds IS NOT NULL
        """, SqlDataScope.Global);
    public static readonly SqlStatement ResumeRelativeMySql = new("workflow.delay.resume_relative.mysql", """
        UPDATE fn_workflow_delay SET DueAtUtc=TIMESTAMPADD(MICROSECOND,RemainingMilliseconds*1000,@Now),
          RemainingMilliseconds=NULL, PausedAtUtc=NULL, Revision=Revision+1
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='waiting'
          AND ModeKey='relative' AND PausedAtUtc IS NOT NULL AND RemainingMilliseconds IS NOT NULL
        """, SqlDataScope.Global);

    public static readonly SqlStatement Cancel = new("workflow.delay.cancel", """
        UPDATE fn_workflow_delay SET StatusKey='cancelled', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='waiting'
        """, SqlDataScope.Global);

    public static readonly SqlStatement SynchronizeStepDue = new("workflow.delay.synchronize_step_due", """
        UPDATE fn_workflow_step SET DueAtUtc=(SELECT delay.DueAtUtc FROM fn_workflow_delay AS delay WHERE delay.Id=fn_workflow_step.Id)
        WHERE InstanceId=@InstanceId AND StatusKey='active' AND NodeTypeKey='timer.delay'
          AND EXISTS (SELECT 1 FROM fn_workflow_delay AS delay WHERE delay.Id=fn_workflow_step.Id
            AND delay.InstanceId=@InstanceId AND delay.TenantScopeKey=@TenantScopeKey AND delay.StatusKey='waiting')
        """, SqlDataScope.Global);
}

/// <summary>唤醒只消费激活时固化的后继和输入，修订号防止暂停恢复或重复扫描接纳旧行。</summary>
internal sealed record WorkflowDelayRecord(Guid Id, Guid InstanceId, string TenantScopeKey, string NodeKey,
    string ContinuationNodeKey, string InputJson, string InputHash, long InputFormRevision,
    Guid? ParallelJoinId, string? ParallelBranchKey, long Revision);
