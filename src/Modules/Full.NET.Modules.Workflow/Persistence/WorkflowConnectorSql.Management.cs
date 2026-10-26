using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>运维读取锚定父实例和可信作用域，操作回执按真实活动/操作人/语义键唯一。</summary>
internal static partial class WorkflowConnectorSql
{
    public static readonly SqlStatement FindInFrame = new("workflow.connector.find_frame", "SELECT " + Projection + """
         FROM fn_workflow_connector_call AS activation WHERE activation.InstanceId=@InstanceId AND activation.TenantScopeKey=@TenantScopeKey
           AND activation.ModeKey='async' AND activation.NodeKey IN @NodeKeys
           AND (activation.ParallelJoinId=@ParallelJoinId OR (activation.ParallelJoinId IS NULL AND @ParallelJoinId IS NULL))
           AND (activation.ParallelBranchKey=@ParallelBranchKey OR (activation.ParallelBranchKey IS NULL AND @ParallelBranchKey IS NULL))
         ORDER BY activation.CreatedAtUtc,activation.Id
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindByIds = new("workflow.connector.find_ids", "SELECT " + Projection + """
         FROM fn_workflow_connector_call AS activation WHERE activation.InstanceId=@InstanceId AND activation.TenantScopeKey=@TenantScopeKey AND activation.Id IN @CallIds
         ORDER BY activation.CreatedAtUtc,activation.Id
        """, SqlDataScope.Global);
    public static readonly SqlStatement ListForInstanceSqlServer = new("workflow.connector.list_instance.sqlserver", "SELECT " + Projection + """
         FROM fn_workflow_connector_call AS activation WHERE activation.InstanceId=@InstanceId AND activation.TenantScopeKey=@TenantScopeKey
         ORDER BY activation.CreatedAtUtc,activation.Id OFFSET @Offset ROWS FETCH NEXT @Take ROWS ONLY
        """, SqlDataScope.Global);
    public static readonly SqlStatement ListForInstanceMySql = new("workflow.connector.list_instance.mysql", "SELECT " + Projection + """
         FROM fn_workflow_connector_call AS activation WHERE activation.InstanceId=@InstanceId AND activation.TenantScopeKey=@TenantScopeKey
         ORDER BY activation.CreatedAtUtc,activation.Id LIMIT @Take OFFSET @Offset
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountForInstance = new("workflow.connector.count_instance", """
        SELECT COUNT(*) FROM fn_workflow_connector_call WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement ListAttempts = new("workflow.connector_attempt.list", "SELECT " + AttemptProjection + """
         FROM fn_workflow_connector_attempt AS execution WHERE execution.CallId=@CallId AND execution.TenantScopeKey=@TenantScopeKey
         ORDER BY execution.LeaseGeneration,execution.Id
        """, SqlDataScope.Global);
    public static readonly SqlStatement ListAttemptSummaries = new("workflow.connector_attempt.list_summaries", """
        SELECT Id,CallId,LeaseGeneration,StatusKey,OutcomeKey,HttpStatusCode,ErrorKey,ResponseHash,StartedAtUtc,CompletedAtUtc
        FROM fn_workflow_connector_attempt WHERE CallId IN @CallIds AND TenantScopeKey=@TenantScopeKey ORDER BY CallId,LeaseGeneration,Id
        """, SqlDataScope.Global);
    public static readonly SqlStatement LatestResolutions = new("workflow.connector_resolution.latest", """
        SELECT CallId,ActionKey,CreatedAtUtc FROM (
          SELECT CallId,ActionKey,CreatedAtUtc,ROW_NUMBER() OVER (PARTITION BY CallId ORDER BY CreatedAtUtc DESC,Id DESC) AS RowNumber
          FROM fn_workflow_connector_resolution WHERE CallId IN @CallIds AND TenantScopeKey=@TenantScopeKey
        ) AS resolution WHERE RowNumber=1
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindResolution = new("workflow.connector_resolution.find", """
        SELECT RequestHash,ResponseJson FROM fn_workflow_connector_resolution
        WHERE CallId=@CallId AND TenantScopeKey=@TenantScopeKey AND ActorUserId=@ActorUserId AND IdempotencyKey=@IdempotencyKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertResolution = new("workflow.connector_resolution.insert", """
        INSERT INTO fn_workflow_connector_resolution
          (Id,CallId,InstanceId,TenantScopeKey,ActorUserId,ActionKey,AttemptId,EvidenceReference,Reason,IdempotencyKey,RequestHash,ResponseJson,CreatedAtUtc)
        VALUES (@Id,@CallId,@InstanceId,@TenantScopeKey,@ActorUserId,@ActionKey,@AttemptId,@EvidenceReference,@Reason,@IdempotencyKey,@RequestHash,@ResponseJson,@Now)
        """, SqlDataScope.Global);
    public static readonly SqlStatement Resolve = new("workflow.connector.resolve", """
        UPDATE fn_workflow_connector_call SET StatusKey=@StatusKey,SelectedAttemptId=@AttemptId,LeaseGeneration=LeaseGeneration+1,
          LeaseOwnerKey=NULL,LeaseExpiresAtUtc=NULL,ApplyAttemptCount=0,LastErrorKey=NULL,DueAtUtc=@Now,
          CompletedAtUtc=CASE WHEN @StatusKey='cancelled' THEN @Now ELSE NULL END,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
          AND StatusKey IN ('outcome_unknown','failed','rejected','cancelled')
        """, SqlDataScope.Global);
    public static readonly SqlStatement AdvanceForResolution = new("workflow.connector.advance_resolution", """
        UPDATE fn_workflow_instance SET Revision=Revision+1 WHERE Id=@Id AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
        """, SqlDataScope.Global);
}

/// <summary>首次对账响应与规范请求摘要共同保存，重放不能接受不同意图。</summary>
internal sealed record WorkflowConnectorResolutionRecord(string RequestHash,string ResponseJson);
internal sealed record WorkflowConnectorAttemptSummaryRecord(Guid Id,Guid CallId,long LeaseGeneration,string StatusKey,string? OutcomeKey,
    int? HttpStatusCode,string? ErrorKey,string? ResponseHash,DateTimeOffset StartedAtUtc,DateTimeOffset? CompletedAtUtc);
internal sealed record WorkflowConnectorLatestResolutionRecord(Guid CallId,string ActionKey,DateTimeOffset CreatedAtUtc);
