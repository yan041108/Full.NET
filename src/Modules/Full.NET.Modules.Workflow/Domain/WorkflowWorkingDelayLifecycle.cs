using System.Text;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>工作时长暂停保存原日历工作量，恢复只按原快照求截止；调用方持有实例锁和结果事务。</summary>
internal static class WorkflowWorkingDelayLifecycle
{
    public static async Task<bool> ApplyAsync(Guid instanceId,string tenantScopeKey,string action,DateTimeOffset now,DatabaseProvider provider,
        IQueryExecutor query,ICommandExecutor commands,CancellationToken token)
    {
        if(action is not ("pause" or "resume"))return false;
        var sql=provider==DatabaseProvider.SqlServer?WorkflowWorkingDelaySql.ListSqlServer:WorkflowWorkingDelaySql.ListMySql;
        var rows=await query.QueryAsync<WorkflowWorkingDelayRecord>(sql,Parameters(("InstanceId",instanceId),("TenantScopeKey",tenantScopeKey),("Take",129)),token).ConfigureAwait(false);
        if(rows.Count>128)return false;
        foreach(var row in rows)
        {
            if(row.InstanceId!=instanceId || row.TenantScopeKey!=tenantScopeKey)return false;
            if((action=="pause" && row.PausedAtUtc is not null) || (action=="resume" && row.PausedAtUtc is null))continue;
            if(row.CalendarVersionId is not { } versionId || versionId==Guid.Empty || row.CalendarJson is not { } snapshot ||
                row.CalendarHash is not{Length:64} hash || Encoding.UTF8.GetByteCount(snapshot)>131072)return false;
            WorkflowWorkCalendar? calendar;
            try{using var json=JsonDocument.Parse(snapshot);if(!WorkflowWorkCalendar.TryRead(json.RootElement,out calendar) || calendar!.ContentHash!=hash || calendar.TimeZoneKey!=row.TimeZoneKey)return false;}
            catch(JsonException){return false;}
            if(calendar is null)return false;
            var parameters=Parameters(("Id",row.Id),("InstanceId",instanceId),("TenantScopeKey",tenantScopeKey),("Revision",row.Revision),("Now",now));
            if(action=="pause")
            {
                if(!calendar.TryRemainingWorkingMilliseconds(now,row.DueAtUtc,out var remaining))return false;
                parameters.Add("RemainingMilliseconds",remaining);
                if(await commands.ExecuteAsync(WorkflowWorkingDelaySql.Pause,parameters,token).ConfigureAwait(false)!=1)return false;
            }
            else
            {
                if(row.RemainingMilliseconds is not { } remaining || !calendar.TryAddWorkingMilliseconds(now,remaining,out var due))return false;
                parameters.Add("DueAtUtc",due);
                if(await commands.ExecuteAsync(WorkflowWorkingDelaySql.Resume,parameters,token).ConfigureAwait(false)!=1)return false;
            }
        }
        return true;
    }
    private static Dictionary<string,object?> Parameters(params(string Key,object? Value)[] values)=>WorkflowSqlParameters.Create(values);
}
