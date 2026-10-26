using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>实例锁内处理已发布的暂停语义；不能从延时器策略推断活动截止。</summary>
internal static class WorkflowSlaLifecycle
{
    public static async Task<bool> ApplyAsync(Guid instanceId, string scope, string action, DateTimeOffset now,
        DatabaseProvider provider, IQueryExecutor query, ICommandExecutor commands, CancellationToken token)
    {
        if (action is not ("pause" or "resume")) return false;
        var rows = await query.QueryAsync<WorkflowSlaRecord>(provider == DatabaseProvider.SqlServer ? WorkflowSlaSql.ListActiveSqlServer : WorkflowSlaSql.ListActiveMySql,
            WorkflowSqlParameters.Create(("InstanceId", instanceId), ("TenantScopeKey", scope), ("Take", WorkflowDefinitionCompiler.MaximumNodes + 1)), token).ConfigureAwait(false);
        if (rows.Count > WorkflowDefinitionCompiler.MaximumNodes) return false;
        foreach (var row in rows)
        {
            if (row.InstanceId != instanceId || row.TenantScopeKey != scope || row.StatusKey != "waiting" || row.Revision < 1) return false;
            if (row.PauseModeKey == "elapsed") continue;
            if (row.PauseModeKey != "freeze") return false;
            if (action == "pause" && row.PausedAtUtc is not null || action == "resume" && row.PausedAtUtc is null) continue;
            if (action == "resume" && row.RemainingMilliseconds is not (>= 0 and <= 31536000000L)) return false;
            var parameters = WorkflowSqlParameters.Create(("Id", row.Id), ("InstanceId", instanceId), ("TenantScopeKey", scope),
                ("Revision", row.Revision), ("Now", now), ("RemainingMilliseconds", WorkflowSlaPolicy.RemainingMilliseconds(row.DueAtUtc, now)),
                ("DueAtUtc", action == "resume" ? now.AddMilliseconds(row.RemainingMilliseconds!.Value) : row.DueAtUtc));
            if (await commands.ExecuteAsync(action == "pause" ? WorkflowSlaSql.Pause : WorkflowSlaSql.Resume, parameters, token).ConfigureAwait(false) != 1) return false;
        }
        return true;
    }
}
