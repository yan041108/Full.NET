using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Calendar.Persistence;

/// <summary>仅Calendar本地表；原操作幂等和pending CAS在同一事务，不跨模块表或外键。</summary>
internal static class PersonalScheduleCompletionSql
{
    private const string Scope = "OwnerUserId=@OwnerUserId AND ((@ScopeTenantId IS NULL AND TenantId IS NULL) OR TenantId=@ScopeTenantId)";
    internal static readonly SqlStatement Find = new("calendar.find_schedule_completion",
        $"SELECT OperationId,RequestDigest,Outcome,Reason,ActualVersion,CompletedAtUtc FROM fn_calendar_schedule_completion WHERE OperationId=@OperationId AND {Scope}", SqlDataScope.Global);
    internal static readonly SqlStatement Read = new("calendar.read_schedule_completion_target",
        $"SELECT Version FROM fn_calendar_personal_schedule WHERE Id=@ScheduleId AND Status='pending' AND {Scope}", SqlDataScope.Global);
    internal static readonly SqlStatement LockSqlServer = new("calendar.lock_schedule_completion_target.sql_server",
        $"SELECT Version FROM fn_calendar_personal_schedule WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ScheduleId AND {Scope}", SqlDataScope.Global);
    internal static readonly SqlStatement LockMySql = new("calendar.lock_schedule_completion_target.mysql",
        $"SELECT Version FROM fn_calendar_personal_schedule WHERE Id=@ScheduleId AND {Scope} FOR UPDATE", SqlDataScope.Global);
    internal static readonly SqlStatement Complete = new("calendar.complete_personal_schedule",
        $"UPDATE fn_calendar_personal_schedule SET Status='completed',CompletedAtUtc=@Now,UpdatedAtUtc=@Now,Version=@ActualVersion WHERE Id=@ScheduleId AND Version=@ExpectedVersion AND Status='pending' AND {Scope}", SqlDataScope.Global);
    internal static readonly SqlStatement Insert = new("calendar.insert_schedule_completion",
        "INSERT INTO fn_calendar_schedule_completion (Id,TenantId,ScopeKey,OwnerUserId,RunId,OperationId,ScheduleId,ExpectedVersion,RequestDigest,Outcome,Reason,ActualVersion,CompletedAtUtc,CreatedAtUtc) VALUES (@Id,@ScopeTenantId,@ScopeKey,@OwnerUserId,@RunId,@OperationId,@ScheduleId,@ExpectedVersion,@RequestDigest,@Outcome,@Reason,@ActualVersion,@CompletedAtUtc,@Now)", SqlDataScope.Global);
}
