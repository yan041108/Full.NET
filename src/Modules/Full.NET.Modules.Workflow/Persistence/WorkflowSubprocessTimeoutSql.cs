using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>截止身份复用真实父调用；封口和截止暂停均要求持有父根锁。</summary>
internal static class WorkflowSubprocessTimeoutSql
{
    private const string Projection = "CallId,InstanceId,TenantScopeKey,DueAtUtc,PauseModeKey,RemainingMilliseconds,PausedAtUtc,StatusKey,Revision";
    public static readonly SqlStatement Find = new("workflow.subprocess_timeout.find", "SELECT " + Projection + " FROM fn_workflow_subprocess_timeout WHERE CallId=@CallId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey", SqlDataScope.Global);
    public static readonly SqlStatement ListWaiting = new("workflow.subprocess_timeout.list_waiting", "SELECT " + Projection + " FROM fn_workflow_subprocess_timeout WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='waiting' ORDER BY CallId", SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.subprocess_timeout.insert", """
        INSERT INTO fn_workflow_subprocess_timeout (CallId,InstanceId,TenantScopeKey,DueAtUtc,PauseModeKey,RemainingMilliseconds,PausedAtUtc,StatusKey,Revision)
        SELECT Id,InstanceId,TenantScopeKey,@DueAtUtc,@PauseModeKey,NULL,NULL,'waiting',1 FROM fn_workflow_subprocess_call
        WHERE Id=@CallId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Update = new("workflow.subprocess_timeout.update", """
        UPDATE fn_workflow_subprocess_timeout SET DueAtUtc=@DueAtUtc,RemainingMilliseconds=@RemainingMilliseconds,
            PausedAtUtc=@PausedAtUtc,StatusKey=@StatusKey,Revision=Revision+1
        WHERE CallId=@CallId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
        """, SqlDataScope.Global);
}

/// <summary>ready只表示所有真实子项已确认收敛，父暂停时仍不能推进。</summary>
internal sealed record WorkflowSubprocessTimeoutRecord(Guid CallId, Guid InstanceId, string TenantScopeKey, DateTimeOffset DueAtUtc,
    string PauseModeKey, long? RemainingMilliseconds, DateTimeOffset? PausedAtUtc, string StatusKey, long Revision);
