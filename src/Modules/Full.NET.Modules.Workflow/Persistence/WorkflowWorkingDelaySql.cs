using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>工作时长更新只消费本实例固定日历；墙钟相对等待继续使用原批量SQL。</summary>
internal static class WorkflowWorkingDelaySql
{
    private const string Projection="delay.Id,delay.InstanceId,delay.TenantScopeKey,delay.DueAtUtc,delay.RemainingMilliseconds,delay.PausedAtUtc,delay.Revision,delay.CalendarVersionId,delay.CalendarHash,delay.CalendarJson,delay.TimeZoneKey";
    private const string From=" FROM fn_workflow_delay AS delay WHERE delay.InstanceId=@InstanceId AND delay.TenantScopeKey=@TenantScopeKey AND delay.ModeKey='working' AND delay.StatusKey='waiting' ORDER BY delay.Id ";
    public static readonly SqlStatement ListSqlServer=new("workflow.working_delay.list.sqlserver","SELECT TOP (@Take) "+Projection+From,SqlDataScope.Global);
    public static readonly SqlStatement ListMySql=new("workflow.working_delay.list.mysql","SELECT "+Projection+From+" LIMIT @Take",SqlDataScope.Global);
    public static readonly SqlStatement Pause=new("workflow.working_delay.pause","""
        UPDATE fn_workflow_delay SET RemainingMilliseconds=@RemainingMilliseconds,PausedAtUtc=@Now,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision AND ModeKey='working' AND StatusKey='waiting' AND PausedAtUtc IS NULL
        """,SqlDataScope.Global);
    public static readonly SqlStatement Resume=new("workflow.working_delay.resume","""
        UPDATE fn_workflow_delay SET DueAtUtc=@DueAtUtc,RemainingMilliseconds=NULL,PausedAtUtc=NULL,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision AND ModeKey='working' AND StatusKey='waiting' AND PausedAtUtc IS NOT NULL AND RemainingMilliseconds IS NOT NULL
        """,SqlDataScope.Global);
}

/// <summary>控制操作读取固定版本和原时区规则，不重新绑定最新日历。</summary>
internal sealed record WorkflowWorkingDelayRecord(Guid Id,Guid InstanceId,string TenantScopeKey,DateTimeOffset DueAtUtc,long? RemainingMilliseconds,
    DateTimeOffset? PausedAtUtc,long Revision,Guid? CalendarVersionId,string? CalendarHash,string? CalendarJson,string TimeZoneKey);
