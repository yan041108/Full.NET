using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>延时唤醒只执行短本地事务；崩溃回滚或重复扫描均不能重复产生后继工作。</summary>
internal sealed class WorkflowDelayProcessor(IQueryExecutor query, ICommandExecutor commands,
    ICommandTransaction transaction, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database,
    IOptions<WorkflowRecoveryWorkerOptions> options, WorkflowActivityScheduler scheduler)
{
    public async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        var statement = database.Value.Provider switch
        {
            DatabaseProvider.SqlServer => WorkflowDelaySql.ListDueSqlServer,
            DatabaseProvider.MySql => WorkflowDelaySql.ListDueMySql,
            _ => throw new InvalidOperationException("Unsupported workflow database provider."),
        };
        var rows = await query.QueryAsync<WorkflowDelayRecord>(statement,
            WorkflowSqlParameters.Create(("Now", clock.UtcNow), ("Take", options.Value.BatchSize)), token).ConfigureAwait(false);
        var fired = 0;
        foreach (var row in rows)
        {
            var result = await transaction.ExecuteResultAsync(inner => FireAsync(row, inner), token).ConfigureAwait(false);
            if (result.IsSuccess && result.Value) fired++;
        }
        return fired;
    }

    private async Task<Result<bool>> FireAsync(WorkflowDelayRecord delay, CancellationToken token)
    {
        var instance = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", delay.InstanceId), ("TenantScopeKey", delay.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.StatusKey != "active" ||
            instance.TenantScopeKey != (instance.TenantId is { } tenantId ? $"tenant:{tenantId:N}" : "host") ||
            instance.ScopeKey != (instance.TenantId is null ? "host" : "tenant")) return Invalid();
        if (await commands.ExecuteAsync(WorkflowSql.LockInstanceForMultiApproval,
                WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", delay.TenantScopeKey), ("Revision", instance.Revision)), token).ConfigureAwait(false) != 1)
            return Invalid();
        var now = clock.UtcNow;
        if (await commands.ExecuteAsync(WorkflowDelaySql.Fire,
                WorkflowSqlParameters.Create(("Id", delay.Id), ("InstanceId", instance.Id), ("TenantScopeKey", delay.TenantScopeKey),
                    ("Revision", delay.Revision), ("Now", now)), token).ConfigureAwait(false) != 1) return Invalid();
        if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(delay.InputJson))) != delay.InputHash) return Invalid();
        var values = JsonSerializer.Deserialize(delay.InputJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (values is null) return Invalid();
        if (await commands.ExecuteAsync(WorkflowSql.CompleteStepWithRevision,
                WorkflowSqlParameters.Create(("Id", delay.Id), ("InstanceId", instance.Id), ("Revision", 1L),
                    ("StatusKey", "completed"), ("CompletedAtUtc", now)), token).ConfigureAwait(false) != 1) return Invalid();
        var sequence = await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id)), token).ConfigureAwait(false);
        await scheduler.EnqueueAsync(instance, delay.ContinuationNodeKey, values, delay.InputFormRevision, sequence,
            delay.ParallelJoinId, delay.ParallelBranchKey, now, token).ConfigureAwait(false);
        if (await commands.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision,
                WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", delay.TenantScopeKey), ("Revision", instance.Revision)), token).ConfigureAwait(false) != 1)
            return Invalid();
        await commands.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(("Id", ids.NewId()), ("InstanceId", instance.Id), ("StepId", delay.Id),
                ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                ("TransitionKey", "node.timer.delay.fired"), ("FromStatusKey", "active"), ("ToStatusKey", "completed"),
                ("IdempotencyKey", null), ("Summary", null), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }

    private static Result<bool> Invalid() => Result<bool>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
}
