using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>相对截止在实例暂停事务中固定剩余量；elapsed截止不随暂停移动。</summary>
internal static class WorkflowSubprocessTimeoutLifecycle
{
    public static async Task<bool> ApplyAsync(Guid instanceId, string scope, string action, DateTimeOffset now,
        IQueryExecutor query, ICommandExecutor commands, CancellationToken token)
    {
        if (action is not ("pause" or "resume")) return false;
        var rows = await query.QueryAsync<WorkflowSubprocessTimeoutRecord>(WorkflowSubprocessTimeoutSql.ListWaiting,
            WorkflowSqlParameters.Create(("InstanceId", instanceId), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        if (rows.Count > WorkflowActivityScheduler.InstanceActivityBudget) return false;
        foreach (var row in rows)
        {
            if (row.InstanceId != instanceId || row.TenantScopeKey != scope || row.StatusKey != "waiting" || row.Revision < 1) return false;
            if (row.PauseModeKey == "elapsed") continue;
            if (row.PauseModeKey != "freeze") return false;
            if (action == "pause" && row.PausedAtUtc is not null || action == "resume" && row.PausedAtUtc is null) continue;
            if (action == "resume" && row.RemainingMilliseconds is not (>= 0 and <= 31536000000L)) return false;
            var next = action == "pause" ? row with { PausedAtUtc = now, RemainingMilliseconds = WorkflowSlaPolicy.RemainingMilliseconds(row.DueAtUtc, now) }
                : row with { DueAtUtc = now.AddMilliseconds(row.RemainingMilliseconds!.Value), PausedAtUtc = null, RemainingMilliseconds = null };
            if (!await UpdateAsync(next, commands, token).ConfigureAwait(false)) return false;
        }
        return true;
    }
    public static async Task<bool> UpdateAsync(WorkflowSubprocessTimeoutRecord row, ICommandExecutor commands, CancellationToken token) =>
        await commands.ExecuteAsync(WorkflowSubprocessTimeoutSql.Update, WorkflowSqlParameters.Create(("CallId", row.CallId), ("InstanceId", row.InstanceId),
            ("TenantScopeKey", row.TenantScopeKey), ("DueAtUtc", row.DueAtUtc), ("RemainingMilliseconds", row.RemainingMilliseconds),
            ("PausedAtUtc", row.PausedAtUtc), ("StatusKey", row.StatusKey), ("Revision", row.Revision)), token).ConfigureAwait(false) == 1;
}
