using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>静态续办处理器；短事务领取与执行事务分离，代次 CAS 阻止过期 Worker 提交。</summary>
internal sealed partial class WorkflowActivityWorkProcessor(IQueryExecutor query, ICommandExecutor command,
    ICommandTransaction transaction, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database,
    IOptions<WorkflowRecoveryWorkerOptions> options, WorkflowRuntimeTransitionExecutor executor,
    WorkflowNotificationOutboxPublisher notifications, ICurrentTenantContextWriter tenantWriter,
    WorkflowHumanActivityPreparation approvalPreparation, WorkflowBranchStateCoordinator branchStates,
    WorkflowAutomaticOutputCoordinator outputs)
{
    public async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        var statement = database.Value.Provider switch
        {
            DatabaseProvider.SqlServer => WorkflowActivitySql.ListDueSqlServer,
            DatabaseProvider.MySql => WorkflowActivitySql.ListDueMySql,
            _ => throw new InvalidOperationException("Unsupported workflow database provider."),
        };
        var now = clock.UtcNow;
        var rows = await query.QueryAsync<WorkflowActivityWorkRecord>(statement,
            WorkflowSqlParameters.Create(("Now", now), ("Take", options.Value.BatchSize)), token).ConfigureAwait(false);
        var claimed = 0;
        foreach (var work in rows)
        {
            token.ThrowIfCancellationRequested();
            var owner = ids.NewId().ToString("N");
            now = clock.UtcNow;
            var updated = await command.ExecuteAsync(WorkflowActivitySql.ClaimWork,
                WorkflowSqlParameters.Create(("Id", work.Id), ("LeaseGeneration", work.LeaseGeneration),
                    ("LeaseOwnerKey", owner), ("LeaseExpiresAtUtc", now.AddSeconds(options.Value.LeaseSeconds)),
                    ("Now", now)), token).ConfigureAwait(false);
            if (updated != 1) continue;
            claimed++;
            var prepared = await PrepareAsync(work, token).ConfigureAwait(false);
            // 执行只包含本模块数据库事实；失败事务回滚，过期租约才允许重领相同输入。
            try
            {
                _ = await transaction.ExecuteResultAsync(inner => ExecuteAsync(
                    work with { LeaseGeneration = work.LeaseGeneration + 1, AttemptCount = work.AttemptCount + 1 },
                    owner, prepared, inner), token).ConfigureAwait(false);
            }
            finally { await outputs.FinishAsync(prepared.Field, CancellationToken.None).ConfigureAwait(false); }
        }
        return claimed;
    }

    private async Task<PreparedActivityWork> PrepareAsync(WorkflowActivityWorkRecord work, CancellationToken token)
    {
        var instance = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", work.InstanceId), ("TenantScopeKey", work.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.StatusKey != "active" ||
            instance.TenantScopeKey != (instance.TenantId is { } tenantId ? $"tenant:{tenantId:N}" : "host") ||
            instance.ScopeKey != (instance.TenantId is null ? "host" : "tenant")) return PreparedActivityWork.Empty;
        using var tenantScope = new ExecutionTenantScope(tenantWriter, instance.TenantId);
        if (!await ValidateAsyncResumeAsync(work,token).ConfigureAwait(false))
            return new(WorkflowPreparedHumanActivities.Empty,null,false,InvalidAsyncResume:true);
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId),
                ("TenantScopeKey", work.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null) return PreparedActivityWork.Empty;
        var draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var values = JsonSerializer.Deserialize(work.InputJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        var join = work.ParallelJoinId is { } joinId
            ? await query.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(WorkflowSql.FindParallelJoinById,
                WorkflowSqlParameters.Create(("Id", joinId), ("InstanceId", work.InstanceId)), token).ConfigureAwait(false) : null;
        if (draft is null || schema is null || values is null || !WorkflowRuntimePlan.TryCreate(draft, schema, out var plan) ||
            !plan!.TryResolveContinuation(work.NodeKey, values, join?.JoinNodeKey, out var transition,work.ResumeBarrierId is null?null:work.NodeKey)) return PreparedActivityWork.Empty;
        WorkflowPreparedAutomaticOutput? field = null;
        if (transition.AutomaticWait is WorkflowFieldWaitPlan wait && wait.NodeKey == work.NodeKey)
        {
            field = await PrepareFieldAsync(instance, work, schema, wait, values, token).ConfigureAwait(false);
            if (field is null || !plan.TryResolveAfterAutomaticWait(work.NodeKey, field.Output.Values, join?.JoinNodeKey, out transition))
            {
                await outputs.FinishAsync(field, CancellationToken.None).ConfigureAwait(false);
                return new(WorkflowPreparedHumanActivities.Empty, null, true);
            }
            values = field.Output.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }
        try
        {
            var humans = await approvalPreparation.PrepareAsync(transition,
            new WorkflowManagementScope(instance.TenantId, instance.ScopeKey, instance.TenantScopeKey), instance.StartedById, token, values, instance.Id,
            flowAdministratorUserIds: plan.FlowAdministratorUserIds).ConfigureAwait(false);
            return new(humans, field, false);
        }
        catch
        {
            await outputs.FinishAsync(field, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<Result<bool>> ExecuteAsync(WorkflowActivityWorkRecord work, string owner,
        PreparedActivityWork prepared, CancellationToken token)
    {
        var instance = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", work.InstanceId), ("TenantScopeKey", work.TenantScopeKey)), token)
            .ConfigureAwait(false);
        if (instance?.StatusKey != "active") return Invalid();
        if (instance.TenantScopeKey != (instance.TenantId is { } tenantId ? $"tenant:{tenantId:N}" : "host") ||
            instance.ScopeKey != (instance.TenantId is null ? "host" : "tenant")) return Invalid();
        using var tenantScope = new ExecutionTenantScope(tenantWriter, instance.TenantId);
        var locked = await command.ExecuteAsync(WorkflowSql.LockInstanceForMultiApproval,
            WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
                ("Revision", instance.Revision)), token).ConfigureAwait(false);
        if (locked != 1) return Invalid();
        if (prepared.InvalidAsyncResume || !await ValidateAsyncResumeAsync(work,token).ConfigureAwait(false))
            return await FaultAsyncResumeAsync(instance,work,owner,token).ConfigureAwait(false);
        if (prepared.InvalidField || work.AttemptCount > options.Value.MaxAttempts)
        {
            if (!await FinishAsync(work, owner, "faulted", "suspended", null, token).ConfigureAwait(false)) return Invalid();
            if (prepared.InvalidField)
                await command.ExecuteAsync(WorkflowSql.InsertExecutionLog,
                    WorkflowSqlParameters.Create(("Id", ids.NewId()), ("InstanceId", instance.Id), ("StepId", null),
                        ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                        ("TransitionKey", "activity.field.invalid_input"), ("FromStatusKey", "active"), ("ToStatusKey", "suspended"),
                        ("IdempotencyKey", null), ("Summary", "workflow.activity.field_input_or_policy_invalid"), ("CreatedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);
            var suspended = await command.ExecuteAsync(WorkflowSql.SuspendInstanceWithRevision,
                WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
                    ("Revision", instance.Revision)), token).ConfigureAwait(false);
            return suspended == 1 ? Result<bool>.Success(true) : Invalid();
        }
        // 输入版本属于原分支或主表单；旧快照不能在刷新修订后继续求值或覆盖。
        if (await branchStates.ReadRevisionAsync(instance, work.ParallelJoinId, work.ParallelBranchKey, token)
                .ConfigureAwait(false) != work.InputFormRevision) return Invalid();
        if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(work.InputJson))) != work.InputHash)
            return Invalid();
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId),
                ("TenantScopeKey", work.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null) return Invalid();
        if (!WorkflowDesignerRelease.AllowsExecution(await WorkflowDesignerRelease.ReadBindingsAsync(query,
                asset.FormVersionId, new WorkflowManagementScope(instance.TenantId, instance.ScopeKey, instance.TenantScopeKey), token).ConfigureAwait(false)))
            return Invalid();
        var draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var values = JsonSerializer.Deserialize(work.InputJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        var join = work.ParallelJoinId is { } joinId
            ? await query.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(WorkflowSql.FindParallelJoinById,
                WorkflowSqlParameters.Create(("Id", joinId), ("InstanceId", work.InstanceId)), token).ConfigureAwait(false)
            : null;
        if (draft is null || schema is null || values is null ||
            (work.ParallelJoinId is not null && join?.StatusKey != "waiting") ||
            !WorkflowRuntimePlan.TryCreate(draft, schema, out var plan) ||
            !plan!.TryResolveContinuation(work.NodeKey, values, join?.JoinNodeKey, out var transition,work.ResumeBarrierId is null?null:work.NodeKey)) return Invalid();
        var sequence = await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence,
            WorkflowSqlParameters.Create(("InstanceId", work.InstanceId)), token).ConfigureAwait(false);
        if (transition.AutomaticWait is WorkflowFieldWaitPlan fieldWait && fieldWait.NodeKey == work.NodeKey)
        {
            if (prepared.Field is not { } field || !fieldWait.Policy.TryApply(values, out var output) ||
                SerializeFieldValues(output!.Values) != field.OutputJson ||
                !await ApplyFieldAsync(instance, work, field, plan.FindNodeTypeKey(work.NodeKey)!, schema, values, sequence, token).ConfigureAwait(false) ||
                !plan.TryResolveAfterAutomaticWait(work.NodeKey, output.Values, join?.JoinNodeKey, out transition)) return Invalid();
            sequence++;
            values = output.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }
        // 分支键来自持久化工作项，不能用节点键猜测一次分叉中的到达身份。
        if (transition.JoinArrival is { } arrival && work.ParallelBranchKey is { } branchKey)
            transition = new(transition.NextHumanNodeKey, transition.CompletesInstance, transition.AutomaticNodes,
                transition.TimeoutPolicy, transition.ApprovalPolicy, transition.AssigneePolicy,
                transition.ParallelFork, arrival with { BranchKey = branchKey }, transition.TaskPolicy, transition.VotePolicy, transition.ClaimPolicy);
        var executed = await executor.ExecuteAsync(instance,
            new WorkflowManagementScope(instance.TenantId, instance.ScopeKey, instance.TenantScopeKey), plan,
            transition, values, sequence, clock.UtcNow, instance.StartedById, work.ParallelJoinId,
            work.ParallelBranchKey, token, budget: new() { CurrentWorkId = work.Id, TotalRemaining = -1 }, preparedAssignees: prepared.Humans).ConfigureAwait(false);
        if (!executed.IsSuccess) return Result<bool>.Failure(executed.Error!);
        var result = executed.Value!;
        var status = result.InstanceStatusKey;
        if (!await FinishAsync(work, owner, "completed", status, result.FirstTodoId, token).ConfigureAwait(false)) return Invalid();
        var advanced = await command.ExecuteAsync(status switch
            {
                "active" => WorkflowSql.AdvanceInstanceWithRevision,
                "suspended" => WorkflowSql.SuspendInstanceWithRevision,
                _ => WorkflowSql.CompleteInstanceWithRevision,
            },
            WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
                ("Revision", instance.Revision), ("StatusKey", status), ("CompletedAtUtc", clock.UtcNow)), token)
            .ConfigureAwait(false);
        if (advanced != 1) return Invalid();
        if (result.RejectsInstance)
            await notifications.PublishInstanceRejectedAsync(instance.Id, instance.StartedById,
                instance.BusinessType, instance.BusinessId, clock.UtcNow, token).ConfigureAwait(false);
        else if (result.CompletesInstance)
            await notifications.PublishInstanceCompletedAsync(instance.Id, instance.StartedById,
                instance.BusinessType, instance.BusinessId, clock.UtcNow, token).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }

    private async Task<bool> FinishAsync(WorkflowActivityWorkRecord work, string owner, string status,
        string resultStatus, Guid? todoId, CancellationToken token) =>
        await command.ExecuteAsync(WorkflowActivitySql.FinishWork,
            WorkflowSqlParameters.Create(("Id", work.Id), ("LeaseOwnerKey", owner), ("LeaseGeneration", work.LeaseGeneration),
                ("StatusKey", status), ("ResultStatusKey", resultStatus), ("ResultTodoId", todoId),
                ("Now", clock.UtcNow)), token).ConfigureAwait(false) == 1;

    private static Result<bool> Invalid() => Result<bool>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());

    /// <summary>后台基础设施只接受持久化实例中的可信租户身份，退出时恢复原作用域。</summary>
    private sealed class ExecutionTenantScope : IDisposable
    {
        private readonly ICurrentTenantContextWriter writer;
        private readonly bool wasHost;
        private readonly TenantContext? previous;

        public ExecutionTenantScope(ICurrentTenantContextWriter writer, Guid? tenantId)
        {
            this.writer = writer;
            wasHost = writer.IsHost;
            previous = writer.Id is { } id && writer.Identifier is { } identifier && writer.Name is { } name
                ? new TenantContext(id, identifier, name) : null;
            if (tenantId is { } target) writer.SetTenant(new TenantContext(target, target.ToString("N"), target.ToString("D")));
            else writer.SetHost();
        }

        public void Dispose()
        {
            if (wasHost) writer.SetHost();
            else if (previous is not null) writer.SetTenant(previous);
            else writer.Clear();
        }
    }
}
