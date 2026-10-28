using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>内部子根身份不接受公共启动请求；真实关联只能在子根创建事务内提交。</summary>
internal sealed record WorkflowChildStartContext(Guid ParentInstanceId, Guid CallId, Guid ItemId, string InputHash)
{
    internal const string BusinessType = "workflow.child";
}

/// <summary>同作用域先锁父根，再创建子根并关联；父取消先到时不能产生孤立子实例。</summary>
internal static class WorkflowSubprocessStartGuard
{
    public static async Task<Result<WorkflowSubprocessStartReservation>> AcquireAsync(WorkflowChildStartContext context,
        StartWorkflowInstanceRequest request, Guid actorId, WorkflowRuntimeAssetRecord childAsset, WorkflowFormSchema childSchema,
        WorkflowManagementScope scope, IQueryExecutor query, ICommandExecutor commands, CancellationToken token)
    {
        if (context.ParentInstanceId == Guid.Empty || context.CallId == Guid.Empty || context.ItemId == Guid.Empty || context.InputHash is not { Length: 64 } ||
            request.BusinessType != WorkflowChildStartContext.BusinessType || request.BusinessId != context.ItemId.ToString("D") ||
            request.IdempotencyKey != "workflow-child-" + context.ItemId.ToString("N") || request.BusinessTitle is not null) return Invalid();
        if (await commands.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction, WorkflowSqlParameters.Create(("Id", context.ParentInstanceId),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 1) return Invalid();
        var root = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", context.ParentInstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var call = await query.QuerySingleOrDefaultAsync<WorkflowSubprocessCallRecord>(WorkflowSubprocessSql.Find,
            WorkflowSqlParameters.Create(("Id", context.CallId), ("InstanceId", context.ParentInstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var item = await query.QuerySingleOrDefaultAsync<WorkflowSubprocessItemRecord>(WorkflowSubprocessSql.FindItem,
            WorkflowSqlParameters.Create(("Id", context.ItemId), ("CallId", context.CallId), ("InstanceId", context.ParentInstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (root?.StatusKey != "active" || call is null || item is null || call.StatusKey is not ("requested" or "running") || call.CancellationRequested ||
            item.StatusKey != "pending" || item.ChildInstanceId is not null || item.StarterUserId != actorId || item.InputHash != context.InputHash ||
            WorkflowSubprocessBindings.Hash(item.InputJson) != item.InputHash || request.InitialValues.GetRawText() != item.InputJson ||
            call.ChildDefinitionVersionId != childAsset.DefinitionVersionId || call.ChildFormVersionId != childAsset.FormVersionId ||
            call.DefinitionHash != WorkflowSubprocessBindings.Hash(childAsset.CanonicalJson) || call.FormHash != WorkflowSubprocessBindings.Hash(childAsset.FormSchemaJson) ||
            call.Depth is < 1 or > WorkflowSubprocessPolicy.MaximumDepth || call.ChildActivityBudget is < 2 or > WorkflowActivityScheduler.InstanceActivityBudget) return Invalid();
        if (await WorkflowExecutionEpochScope.ReadForStepAsync(query, root.Id, scope.TenantScopeKey, call.Id, token).ConfigureAwait(false) != call.ExecutionEpochId)
            return Invalid();
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", root.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        WorkflowDefinitionDraft? draft; WorkflowFormSchema? parentSchema; Dictionary<string, JsonElement>? original;
        try { draft = asset is null ? null : JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
            parentSchema = asset is null ? null : JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            original = JsonSerializer.Deserialize(call.ParentInputJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement); }
        catch (JsonException) { return Invalid(); }
        var node = draft?.Nodes.SingleOrDefault(node => node.NodeKey == call.NodeKey);
        if (node?.NodeTypeKey != WorkflowSubprocessPolicy.NodeType || parentSchema is null || original is null || node.Config.GetRawText() != call.PolicyJson ||
            !WorkflowSubprocessPolicy.TryRead(node.Config, parentSchema, out var policy) ||
            !node.Config.TryGetProperty("boundSubprocess", out var boundJson) || !WorkflowBoundSubprocess.TryRead(boundJson, childAsset.DefinitionVersionId, out var bound) ||
            bound!.FormVersionId != childAsset.FormVersionId || bound.DefinitionHash != call.DefinitionHash || bound.FormHash != call.FormHash || bound.ActivityBudget != call.ChildActivityBudget ||
            call.Depth + bound.MaximumDepth - 1 > WorkflowSubprocessPolicy.MaximumDepth ||
            !policy!.MatchesSchemas(parentSchema, childSchema)) return Invalid();
        if (policy.ExecutionSet is { } multi)
        {
            var set = await query.QuerySingleOrDefaultAsync<WorkflowMultiInstanceSetRecord>(WorkflowMultiInstanceSql.Find,
                WorkflowSqlParameters.Create(("CallId", call.Id), ("InstanceId", root.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            var items = await query.QueryAsync<WorkflowSubprocessItemRecord>(WorkflowSubprocessSql.Items,
                WorkflowSqlParameters.Create(("CallId", call.Id), ("InstanceId", root.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (set?.StatusKey != "running" || call.ErrorCode is not null ||
                !WorkflowMultiInstanceSnapshotRules.TryRead(set, multi, original, out var snapshot) ||
                !WorkflowMultiInstanceSnapshotRules.MatchesItems(policy, childSchema, original, snapshot, items, root.StartedById) ||
                multi.ModeKey == "serial" && items.Any(other => other.SequenceNo < item.SequenceNo && other.StatusKey is "pending" or "running") ||
                WorkflowMultiInstanceDecision.Evaluate(multi, items.Select(value => new WorkflowMultiInstanceFact(value.Id, value.ItemKey, value.SequenceNo, value.StatusKey)).ToArray()).StateKey != "wait") return Invalid();
        }
        else if (item.StarterUserId != root.StartedById || !policy.TryCreateInput(original, childSchema, out var expectedInput) || expectedInput != item.InputJson) return Invalid();
        var revision = await query.QuerySingleOrDefaultAsync<long?>(WorkflowSubprocessSql.ActiveStepRevision,
            WorkflowSqlParameters.Create(("Id", call.Id), ("InstanceId", root.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var budget = await query.QuerySingleOrDefaultAsync<WorkflowExecutionBudgetRecord>(WorkflowSubprocessSql.FindBudget,
            WorkflowSqlParameters.Create(("InstanceId", root.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (revision is null || budget is null || budget.Reserved < call.ChildActivityBudget * (policy.ExecutionSet?.MaximumCount ?? 1) || budget.Granted > WorkflowActivityScheduler.InstanceActivityBudget) return Invalid();
        return Result<WorkflowSubprocessStartReservation>.Success(new(call, item, policy.Starter?.SourceKey ?? "parent"));
    }

    public static async Task<Result<bool>> LinkCreatedAsync(WorkflowSubprocessStartReservation reservation, Guid childId,
        WorkflowManagementScope scope, DateTimeOffset now, ICommandExecutor commands, CancellationToken token)
    {
        var call = reservation.Call; var item = reservation.Item;
        if (await commands.ExecuteAsync(WorkflowSubprocessSql.LinkChild, WorkflowSqlParameters.Create(("Id", item.Id), ("CallId", call.Id),
                ("InstanceId", call.InstanceId), ("TenantScopeKey", scope.TenantScopeKey), ("InputHash", item.InputHash), ("Revision", item.Revision),
                ("ChildInstanceId", childId), ("Now", now)), token).ConfigureAwait(false) != 1 ||
            await commands.ExecuteAsync(WorkflowSubprocessSql.InsertBudget, WorkflowSqlParameters.Create(("InstanceId", childId),
                ("TenantScopeKey", scope.TenantScopeKey), ("Granted", call.ChildActivityBudget)), token).ConfigureAwait(false) != 1) return InvalidLink();
        if (call.StatusKey == "requested" && await commands.ExecuteAsync(WorkflowSubprocessSql.MarkRunning, WorkflowSqlParameters.Create(("Id", call.Id),
                ("InstanceId", call.InstanceId), ("TenantScopeKey", scope.TenantScopeKey), ("Revision", call.Revision), ("Now", now)), token).ConfigureAwait(false) != 1) return InvalidLink();
        return Result<bool>.Success(true);
    }
    private static Result<WorkflowSubprocessStartReservation> Invalid() => Result<WorkflowSubprocessStartReservation>.Failure(new(
        "workflow.subprocess.start_not_admitted", "The fixed child activation cannot start in the current parent state.", ErrorType.Conflict));
    private static Result<bool> InvalidLink() => Result<bool>.Failure(new(
        "workflow.subprocess.link_conflict", "The child activation could not be linked atomically.", ErrorType.Conflict));
}

/// <summary>持有父根锁时读取的原项修订，不接受客户端生成的关联凭据。</summary>
internal sealed record WorkflowSubprocessStartReservation(WorkflowSubprocessCallRecord Call, WorkflowSubprocessItemRecord Item, string StarterSourceKey);
