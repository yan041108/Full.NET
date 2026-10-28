using System.Text.Json;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在父根锁事务中固定真实等待、最小子输入和预算；不启动子实例或调用其他模块端口。</summary>
internal sealed class WorkflowSubprocessActivationWriter(IQueryExecutor query, ICommandExecutor commands, IIdGenerator ids)
{
    public async Task<Result<int>> ActivateAsync(WorkflowInstanceRecord instance, WorkflowManagementScope scope, Guid executionEpochId, WorkflowSubprocessWaitPlan wait,
        IReadOnlyDictionary<string, JsonElement> values, long formRevision, long sequence, Guid? joinId, string? branchKey,
        DateTimeOffset now, CancellationToken token, WorkflowActivationTrace? trace=null)
    {
        var existing = await query.QuerySingleOrDefaultAsync<WorkflowSubprocessCallRecord>(WorkflowSubprocessSql.FindAtNode,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("ExecutionEpochId", executionEpochId), ("TenantScopeKey", scope.TenantScopeKey), ("NodeKey", wait.NodeKey)), token).ConfigureAwait(false);
        if (existing is not null) return existing.StatusKey is "requested" or "running" or "result_ready" && existing.PolicyJson == wait.Configuration.GetRawText() &&
            existing.ParallelJoinId == joinId && existing.ParallelBranchKey == branchKey ? Result<int>.Success(0) : Invalid();
        var childAsset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", wait.Binding.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (childAsset is null || !WorkflowSubprocessBindings.TryBindAsset(childAsset, wait.Policy, wait.ParentSchema, out var binding) || binding != wait.Binding) return Invalid();
        WorkflowFormSchema? childSchema;
        try { childSchema = JsonSerializer.Deserialize(childAsset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema); }
        catch (JsonException) { return Invalid(); }
        string? inputJson = null;
        if (childSchema is null || wait.Policy.ExecutionSet is null && !wait.Policy.TryCreateInput(values, childSchema, out inputJson)) return Invalid();
        var reserved = binding.ActivityBudget * (wait.Policy.ExecutionSet?.MaximumCount ?? 1);
        var parentDepth = await query.QuerySingleOrDefaultAsync<int?>(WorkflowSubprocessSql.FindDepth,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("ExecutionEpochId", executionEpochId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) ?? 0;
        if (parentDepth < 0 || parentDepth + binding.MaximumDepth > WorkflowSubprocessPolicy.MaximumDepth) return Invalid();
        var budget = await query.QuerySingleOrDefaultAsync<WorkflowExecutionBudgetRecord>(WorkflowSubprocessSql.FindBudget,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("ExecutionEpochId", executionEpochId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (budget is null)
        {
            if (instance.BusinessType == WorkflowChildStartContext.BusinessType || await commands.ExecuteAsync(WorkflowSubprocessSql.InsertBudget,
                    WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("ExecutionEpochId", executionEpochId), ("TenantScopeKey", scope.TenantScopeKey),
                        ("Granted", WorkflowActivityScheduler.InstanceActivityBudget)), token).ConfigureAwait(false) != 1) return Invalid();
            budget = new(instance.Id, WorkflowActivityScheduler.InstanceActivityBudget, 0, 1);
        }
        var count = await query.QuerySingleOrDefaultAsync<int>(WorkflowActivitySql.CountActivations,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id)), token).ConfigureAwait(false);
        if (await commands.ExecuteAsync(WorkflowSubprocessSql.ReserveBudget, WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey), ("Revision", budget.Revision), ("Reserve", reserved),
                ("UsedCount", count + 1), ("KeepRemaining", 1)), token).ConfigureAwait(false) != 1) return Invalid();
        var parentInput = WorkflowActivityScheduler.SerializeInput(values);
        if (Encoding.UTF8.GetByteCount(parentInput) > WorkflowSubprocessPolicy.MaximumInputBytes) return Invalid();
        var id = ids.NewId(); var parameters = WorkflowSqlParameters.Create(("Id", id), ("InstanceId", instance.Id), ("ExecutionEpochId", executionEpochId), ("TenantScopeKey", scope.TenantScopeKey),
            ("NodeKey", wait.NodeKey), ("ContinuationNodeKey", wait.ContinuationNodeKey), ("ChildDefinitionVersionId", binding.DefinitionVersionId),
            ("ChildFormVersionId", binding.FormVersionId), ("DefinitionHash", binding.DefinitionHash), ("FormHash", binding.FormHash),
            ("PolicyJson", wait.Configuration.GetRawText()), ("ParentInputJson", parentInput), ("InputFormRevision", formRevision),
            ("ParallelJoinId", joinId), ("ParallelBranchKey", branchKey), ("Depth", parentDepth + 1), ("ChildActivityBudget", binding.ActivityBudget),
            ("ExecutionSequence", sequence), ("Now", now));
        if (await commands.ExecuteAsync(WorkflowSubprocessSql.InsertStep, parameters, token).ConfigureAwait(false) != 1 ||
            await commands.ExecuteAsync(WorkflowSubprocessSql.Insert, parameters, token).ConfigureAwait(false) != 1) return Invalid();
        await WorkflowActivationBindingWriter.BindAsync(commands,instance.Id,scope.TenantScopeKey,executionEpochId,id,now,token,trace,ids).ConfigureAwait(false);
        if (wait.Policy.ExecutionSet is { } multi)
        {
            if (await commands.ExecuteAsync(WorkflowMultiInstanceSql.Insert, WorkflowSqlParameters.Create(("CallId", id), ("InstanceId", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey), ("ModeKey", multi.ModeKey), ("SourceKey", multi.SourceKey), ("MaximumCount", multi.MaximumCount),
                ("RequiredPercentage", multi.RequiredPercentage), ("Now", now)), token).ConfigureAwait(false) != 1) return Invalid();
        }
        else if (await commands.ExecuteAsync(WorkflowSubprocessSql.InsertItem, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("CallId", id), ("InstanceId", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey), ("ItemKey", "single"), ("SequenceNo", 1), ("StarterUserId", instance.StartedById),
                ("InputJson", inputJson), ("InputHash", WorkflowSubprocessBindings.Hash(inputJson!)), ("Now", now)), token).ConfigureAwait(false) != 1) return Invalid();
        if (wait.Policy.Timeout is { } timeout && await commands.ExecuteAsync(WorkflowSubprocessTimeoutSql.Insert,
                WorkflowSqlParameters.Create(("CallId", id), ("InstanceId", instance.Id), ("ExecutionEpochId", executionEpochId), ("TenantScopeKey", scope.TenantScopeKey),
                    ("DueAtUtc", now.AddMinutes(timeout.DueAfterMinutes)), ("PauseModeKey", timeout.PauseModeKey)), token).ConfigureAwait(false) != 1) return Invalid();
        await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", instance.TenantId),
            ("ScopeKey", instance.ScopeKey), ("InstanceId", instance.Id), ("OperationKey", "activity.execute"), ("ActorUserId", null),
            ("ResourceTypeKey", "step"), ("ResourceId", id), ("OutcomeKey", "succeeded"),
            ("DetailJson", "{\"nodeTypeKey\":\"process.subprocess\",\"phaseKey\":\"waiting\"}"),
            ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        return Result<int>.Success(reserved);
    }
    private static Result<int> Invalid() => Result<int>.Failure(new(WorkflowErrorCodes.SubprocessActivationInvalid,
        "The fixed child activation or its execution budget is invalid.", ErrorType.Conflict));
}
