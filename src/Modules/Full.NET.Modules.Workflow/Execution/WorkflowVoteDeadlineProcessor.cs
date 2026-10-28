using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>截止只根据持久绝对时间裁定；后台不生成个人选票、签名或伪造办理人。</summary>
internal sealed class WorkflowVoteDeadlineProcessor(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database, IActiveTenantContextResolver tenants,
    ICurrentTenantContextWriter tenantWriter, WorkflowVoteTodoService contexts, WorkflowHumanActivityPreparation assignees,
    WorkflowVoteDecisionCoordinator decisions, WorkflowVoteResultWriter results, ILogger<WorkflowVoteDeadlineProcessor> logger)
{
    private const int BatchSize = 50;

    public async Task<int> ProcessDueAsync(CancellationToken token)
    {
        var statement = database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowVoteSql.FindDueSqlServer : WorkflowVoteSql.FindDueMySql;
        var rows = await query.QueryAsync<WorkflowDueVoteRecord>(statement,
            P(("BatchSize", BatchSize), ("Now", clock.UtcNow)), token).ConfigureAwait(false);
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            try { await ProcessOneAsync(row, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger.LogError(exception, "Workflow vote deadline processing failed for {VoteId}.", row.Id); }
        }
        return rows.Count;
    }

    private async Task ProcessOneAsync(WorkflowDueVoteRecord row, CancellationToken token)
    {
        var instance = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            P(("Id", row.InstanceId), ("TenantScopeKey", row.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.StatusKey != "active" || instance.TenantScopeKey != (instance.TenantId is { } id ? $"tenant:{id:N}" : "host") ||
            instance.ScopeKey != (instance.TenantId is null ? "host" : "tenant")) return;
        var wasHost = tenantWriter.IsHost;
        var previous = tenantWriter.Id is { } oldId && tenantWriter.Identifier is { } identifier && tenantWriter.Name is { } name
            ? new TenantContext(oldId, identifier, name) : null;
        try
        {
            if (instance.TenantId is { } tenantId)
            {
                var tenant = await tenants.ResolveActiveByIdAsync(tenantId, token).ConfigureAwait(false);
                if (tenant is null) return;
                tenantWriter.SetTenant(tenant);
            }
            else tenantWriter.SetHost();
            var scope = new WorkflowManagementScope(instance.TenantId, instance.ScopeKey, instance.TenantScopeKey);
            var member = await query.QuerySingleOrDefaultAsync<WorkflowVoteDeadlineMemberRecord>(database.Value.Provider == DatabaseProvider.SqlServer
                ? WorkflowVoteSql.FindDeadlineMemberSqlServer : WorkflowVoteSql.FindDeadlineMemberMySql,
                P(("VoteId", row.Id), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (member is null) return;
            // 此处只复用本人资源读取约束定位活动，不提交该人的表单或动作事实。
            var read = await contexts.ReadAsync(member.TodoId, member.AssigneeUserId, scope, token).ConfigureAwait(false);
            if (!read.IsSuccess || read.Value!.Round.StatusKey != "active" || read.Value.Round.ClosesAtUtc is not { } due || due > clock.UtcNow) return;
            var prepared = read.Value;
            var values = WorkflowVoteResultWriter.ResultValues(prepared, true);
            if (values is null || !prepared.RuntimePlan.TryResolveAfterHuman(prepared.Todo.NodeKey, values,
                prepared.Todo.ParallelJoinNodeKey, out var transition)) return;
            var next = await assignees.PrepareAsync(transition, scope, instance.StartedById, token, values, instance.Id, prepared.Todo.NodeKey,
                flowAdministratorUserIds: prepared.RuntimePlan.FlowAdministratorUserIds, executionEpochId: await WorkflowExecutionEpochScope.ReadForStepAsync(query, instance.Id, scope.TenantScopeKey, prepared.Todo.StepId, token).ConfigureAwait(false)).ConfigureAwait(false);
            var outcome = await transaction.ExecuteResultAsync(async inner =>
            {
                if (await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                    P(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false) != 1) return Conflict();
                var latest = await contexts.ReadAsync(member.TodoId, member.AssigneeUserId, scope, inner).ConfigureAwait(false);
                if (!latest.IsSuccess) return Result<bool>.Failure(latest.Error!);
                var current = latest.Value!;
                if (current.Round.Id != row.Id || current.Instance.StatusKey != "active" || current.Round.StatusKey != "active") return Result<bool>.Success(false);
                var now = clock.UtcNow;
                var decision = await decisions.ResolveAsync(current.Round, current.Policy, scope, now, true, inner).ConfigureAwait(false);
                if (!decision.IsSuccess) return Result<bool>.Failure(decision.Error!);
                var applied = await results.ApplyAsync(current, prepared, decision.Value!, next, scope, null, now, inner).ConfigureAwait(false);
                if (!applied.IsSuccess) return Result<bool>.Failure(applied.Error!);
                var correlation = Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson();
                await command.ExecuteAsync(WorkflowSql.InsertExecutionLog, P(("Id", ids.NewId()), ("InstanceId", instance.Id), ("StepId", row.Id),
                    ("TransitionKey", "vote.deadline"), ("FromStatusKey", "active"), ("ToStatusKey", applied.Value!.StatusKey),
                    ("IdempotencyKey", $"vote.deadline:{row.Id:N}"), ("Summary", decision.Value!.Outcome.ToString()),
                    ("CreatedAtUtc", now), ("CorrelationJson", correlation)), inner).ConfigureAwait(false);
                await command.ExecuteAsync(WorkflowSql.InsertDomainAudit, P(("Id", ids.NewId()), ("TenantId", scope.TenantId), ("ScopeKey", scope.ScopeKey),
                    ("InstanceId", instance.Id), ("OperationKey", "vote.deadline"), ("ActorUserId", (Guid?)null), ("ResourceTypeKey", "step"),
                    ("ResourceId", row.Id), ("OutcomeKey", "succeeded"), ("DetailJson", "{\"sourceKey\":\"server-deadline\"}"),
                    ("CreatedAtUtc", now), ("CorrelationJson", correlation)), inner).ConfigureAwait(false);
                return Result<bool>.Success(true);
            }, token).ConfigureAwait(false);
            if (!outcome.IsSuccess) logger.LogWarning("Workflow vote deadline deferred for {VoteId}: {ErrorCode}.", row.Id, outcome.Error!.Code);
        }
        finally
        {
            if (wasHost) tenantWriter.SetHost();
            else if (previous is not null) tenantWriter.SetTenant(previous);
            else tenantWriter.Clear();
        }
    }

    private static object P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<bool> Conflict() => Result<bool>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
}
