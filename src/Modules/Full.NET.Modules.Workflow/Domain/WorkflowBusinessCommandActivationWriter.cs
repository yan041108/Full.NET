using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>持有实例锁时建立真实业务等待及Outbox意图；节点不提供目标记录、快照或个人待办。</summary>
internal sealed class WorkflowBusinessCommandActivationWriter(IQueryExecutor query, ICommandExecutor commands, IIdGenerator ids,
    WorkflowBusinessActionCatalog catalog, WorkflowBusinessCommandPublisher publisher)
{
    public async Task<Result<bool>> ActivateAsync(WorkflowInstanceRecord instance, WorkflowManagementScope scope, WorkflowBusinessCommandWaitPlan wait,
        long sequence, Guid? joinId, string? branchKey, DateTimeOffset now, CancellationToken token)
    {
        var action = catalog.Find(scope.ScopeKey, wait.Policy.ActionKey, wait.Policy.ActionVersion, wait.ActionHash);
        if (joinId is not null || branchKey is not null || action is null || action.Definition.BusinessType != instance.BusinessType ||
            action.Definition.PlacementKey != "root_finalizer" || wait.ApprovalNodeKeys.Count == 0) return Invalid();
        var existing = await query.QuerySingleOrDefaultAsync<WorkflowBusinessCommandRecord>(WorkflowBusinessCommandSql.FindAtNode,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey), ("NodeKey", wait.NodeKey)), token).ConfigureAwait(false);
        if (existing is not null)
            return existing.ActionHash == wait.ActionHash && existing.StatusKey is "requested" or "result_ready"
                ? Result<bool>.Success(false) : Invalid();
        if (await query.QuerySingleOrDefaultAsync<int>(WorkflowBusinessCommandSql.ApprovedPredecessors,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey), ("NodeKeys", wait.ApprovalNodeKeys.ToArray())), token).ConfigureAwait(false) < 1)
            return Invalid();
        var id = ids.NewId();
        var binding = new WorkflowBusinessCommandBinding(id, instance.Id, instance.DefinitionVersionId, action.Definition.OwnerKey,
            wait.Policy.ActionKey, wait.Policy.ActionVersion, wait.ActionHash, instance.BusinessType, instance.BusinessId, instance.StartedById);
        var hash = WorkflowBusinessCommandHash.Compute(binding);
        var json = JsonSerializer.Serialize(binding, WorkflowJsonSerializerContext.Default.WorkflowBusinessCommandBinding);
        var parameters = WorkflowSqlParameters.Create(("Id", id), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
            ("NodeKey", wait.NodeKey), ("ContinuationNodeKey", wait.ContinuationNodeKey), ("ExecutionSequence", sequence),
            ("ActionKey", wait.Policy.ActionKey), ("ActionVersion", wait.Policy.ActionVersion), ("ActionHash", wait.ActionHash),
            ("BindingJson", json), ("InputHash", hash), ("Now", now));
        if (await commands.ExecuteAsync(WorkflowBusinessCommandSql.InsertStep, parameters, token).ConfigureAwait(false) != 1 ||
            await commands.ExecuteAsync(WorkflowBusinessCommandSql.Insert, parameters, token).ConfigureAwait(false) != 1) return Invalid();
        await publisher.RequestAsync(binding, hash, "execute", 1, now, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", instance.TenantId),
            ("ScopeKey", instance.ScopeKey), ("InstanceId", instance.Id), ("OperationKey", "activity.execute"), ("ActorUserId", null),
            ("ResourceTypeKey", "step"), ("ResourceId", id), ("OutcomeKey", "succeeded"),
            ("DetailJson", "{\"sourceKey\":\"business_command\",\"phaseKey\":\"requested\"}"),
            ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }
    private static Result<bool> Invalid() => Result<bool>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
}
