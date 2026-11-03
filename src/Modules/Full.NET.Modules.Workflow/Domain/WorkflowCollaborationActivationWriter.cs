using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.ManageCollaborationGrants;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>持有真实源根锁时保存等待、固定投影和Outbox；不在源事务启动外租户实例。</summary>
internal sealed class WorkflowCollaborationActivationWriter(IQueryExecutor query, ICommandExecutor command, IIdGenerator ids, IOutboxWriter outbox)
{
    internal async Task<Result<int>> ActivateAsync(WorkflowInstanceRecord instance, WorkflowManagementScope scope, Guid epoch,
        WorkflowCollaborationWaitPlan wait, IReadOnlyDictionary<string, JsonElement> values, long inputRevision, long sequence,
        Guid? joinId, string? branchKey, DateTimeOffset now, CancellationToken token, WorkflowActivationTrace trace)
    {
        // 发布/接受已拒绝嵌套协作，运行边界仍独立拒绝子根和协作目标再次分配跨根预算。
        if (instance.BusinessType is WorkflowChildStartContext.BusinessType or WorkflowCollaborationStartContext.BusinessType ||
            instance.TenantScopeKey != scope.TenantScopeKey || instance.TenantId != scope.TenantId || instance.StatusKey != "active" ||
            epoch == Guid.Empty || inputRevision < 1 || sequence < 1 || trace.Parents.Count == 0 ||
            (joinId is null) != (branchKey is null)) return Invalid();
        var input = WorkflowActivityScheduler.SerializeInput(values);
        if (Encoding.UTF8.GetByteCount(input) > 131_072) return Invalid();
        var inputHash = WorkflowCollaborationCallPolicy.Hash(input);
        var existing = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationCallRecord>(WorkflowCollaborationCallSql.FindAtNode,
            P(("InstanceId", instance.Id), ("ExecutionEpochId", epoch), ("TenantScopeKey", scope.TenantScopeKey), ("NodeKey", wait.NodeKey)), token).ConfigureAwait(false);
        if (existing is not null)
        {
            WorkflowCollaborationCallBinding? binding;
            try { binding = JsonSerializer.Deserialize(existing.BindingJson, WorkflowJsonSerializerContext.Default.WorkflowCollaborationCallBinding); }
            catch (JsonException) { return Invalid(); }
            return binding is not null && existing.StatusKey is ("preparing" or "requested") && existing.BindingHash == WorkflowCollaborationCallPolicy.Hash(existing.BindingJson) &&
                existing.InputHash == inputHash && existing.InputFormRevision == inputRevision && binding.GrantId == wait.Policy.GrantId &&
                binding.GrantRevision == wait.Policy.GrantRevision && binding.PolicyHash == wait.Policy.PolicyHash && binding.ParallelJoinId == joinId &&
                binding.ParallelBranchKey == branchKey && binding.ContinuationNodeKey == wait.ContinuationNodeKey
                ? Result<int>.Success(0) : Invalid();
        }
        var grant = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find,
            P(("Id", wait.Policy.GrantId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (grant is null || !WorkflowCollaborationGrantPolicy.TryAuthorize(grant, scope.TenantScopeKey, false, wait.Policy.GrantRevision, now,
                out var offer, out var acceptance) || grant.PolicyHash != wait.Policy.PolicyHash || instance.FormVersionId != offer!.SourceFormVersionId) return Invalid();
        var sourceVersion = await query.QuerySingleOrDefaultAsync<WorkflowDefinitionVersionRecord>(WorkflowSql.FindDefinitionVersionById,
            P(("Id", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (sourceVersion?.DefinitionId != offer.SourceDefinitionId || sourceVersion.FormVersionId != offer.SourceFormVersionId ||
            !WorkflowCollaborationCallPolicy.TryProject(wait.SourceSchema, offer, acceptance!, values, out var projected, preparingFiles: true)) return Invalid();
        var summary = JsonSerializer.Deserialize(grant.TargetSummaryJson!, WorkflowJsonSerializerContext.Default.WorkflowCollaborationTargetSummary);
        var fieldTypes = wait.SourceSchema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, field => field.FieldTypeKey, StringComparer.Ordinal);
        if (summary is null || acceptance!.InputFields.Any(pair => !summary.InputFields.TryGetValue(pair.Value, out var type) || type != fieldTypes[pair.Key])) return Invalid();
        var depth = await query.QuerySingleOrDefaultAsync<int?>(WorkflowSubprocessSql.FindDepth,
            P(("InstanceId", instance.Id), ("ExecutionEpochId", epoch), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) ?? 0;
        // 协作源仅支持普通根；子流程或协作目标不能再次新开跨根切片，避免放大已同意的执行额度。
        if (depth != 0 || instance.BusinessType == WorkflowChildStartContext.BusinessType || acceptance.MaximumActivities < 2) return Invalid();
        // 固定政策虽不可变，撤销仍须与源预留和准入写入同一把授权行锁裁决。
        if (await command.ExecuteAsync(WorkflowCollaborationControlSql.LockSourceGrant,
                P(("GrantId", grant.Id), ("TenantScopeKey", scope.TenantScopeKey), ("TargetTenantScopeKey", grant.TargetTenantScopeKey),
                    ("GrantRevision", grant.Revision), ("PolicyHash", grant.PolicyHash), ("Now", now)), token).ConfigureAwait(false) != 1) return Invalid();
        var budget = await query.QuerySingleOrDefaultAsync<WorkflowExecutionBudgetRecord>(WorkflowSubprocessSql.FindBudget,
            P(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (budget is null)
        {
            if (await command.ExecuteAsync(WorkflowSubprocessSql.InsertBudget, P(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                    ("Granted", WorkflowActivityScheduler.InstanceActivityBudget)), token).ConfigureAwait(false) != 1) return Invalid();
            budget = new(instance.Id, WorkflowActivityScheduler.InstanceActivityBudget, 0, 1);
        }
        var used = await query.QuerySingleOrDefaultAsync<int>(WorkflowActivitySql.CountActivations, P(("InstanceId", instance.Id)), token).ConfigureAwait(false);
        if (await command.ExecuteAsync(WorkflowSubprocessSql.ReserveBudget, P(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                ("Revision", budget.Revision), ("Reserve", acceptance.MaximumActivities), ("UsedCount", used + 1), ("KeepRemaining", 1)), token).ConfigureAwait(false) != 1) return Invalid();
        var id = ids.NewId(); var delivery = ids.NewId(); var projectionHash = WorkflowCollaborationCallPolicy.Hash(projected!);
        var preparing = wait.SourceSchema.Sections.SelectMany(section => section.Fields).Any(field => field.FieldTypeKey == "attachment" &&
            acceptance.InputFields.ContainsKey(field.FieldKey) && values.TryGetValue(field.FieldKey, out var files) &&
            files.ValueKind == JsonValueKind.Array && files.GetArrayLength() > 0);
        var proof = new WorkflowCollaborationCallBinding(id, delivery, instance.Id, instance.DefinitionVersionId, instance.FormVersionId!.Value, epoch,
            scope.TenantScopeKey, grant.TargetTenantScopeKey, scope.TenantId, grant.TargetTenantId, wait.NodeKey, wait.ContinuationNodeKey, joinId, branchKey,
            inputRevision, inputHash, grant.Id, grant.Revision, grant.PolicyHash!, acceptance.TargetDefinitionVersionId, grant.TargetSummaryHash!, acceptance.MaximumActivities, 1, projectionHash);
        var json = JsonSerializer.Serialize(proof, WorkflowJsonSerializerContext.Default.WorkflowCollaborationCallBinding);
        var hash = WorkflowCollaborationCallPolicy.Hash(json);
        var parameters = P(("Id", id), ("InstanceId", instance.Id), ("ExecutionEpochId", epoch), ("TenantScopeKey", scope.TenantScopeKey),
            ("NodeKey", wait.NodeKey), ("ContinuationNodeKey", wait.ContinuationNodeKey), ("GrantId", grant.Id), ("GrantRevision", grant.Revision), ("PolicyHash", grant.PolicyHash),
            ("DeliveryId", delivery), ("TargetTenantScopeKey", grant.TargetTenantScopeKey), ("BindingJson", json), ("BindingHash", hash), ("InputJson", input), ("InputHash", inputHash), ("InputFormRevision", inputRevision),
            ("ProjectionJson", projected), ("ProjectionHash", projectionHash), ("ReservedActivities", acceptance.MaximumActivities), ("ExecutionSequence", sequence),
            ("ParallelJoinId", joinId), ("ParallelBranchKey", branchKey), ("StatusKey", preparing ? "preparing" : "requested"), ("Now", now));
        if (await command.ExecuteAsync(WorkflowCollaborationCallSql.InsertStep, parameters, token).ConfigureAwait(false) != 1 ||
            await command.ExecuteAsync(WorkflowCollaborationCallSql.Insert, parameters, token).ConfigureAwait(false) != 1) return Invalid();
        if (!preparing && await command.ExecuteAsync(WorkflowCollaborationRelaySql.InsertAdmission, parameters, token).ConfigureAwait(false) != 1) return Invalid();
        await WorkflowActivationBindingWriter.BindAsync(command, instance.Id, scope.TenantScopeKey, epoch, id, now, token, trace, ids).ConfigureAwait(false);
        if (!preparing) await outbox.AddAsync(WorkflowCollaborationIntegrationEventTypes.Requested, 1, new WorkflowCollaborationRequestedIntegrationEvent(id, delivery, hash),
            IntegrationEventMetadata.Create(instance.Id.ToString("D"), "fullnet.workflow"), token).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowSql.InsertDomainAudit, P(("Id", ids.NewId()), ("TenantId", instance.TenantId), ("ScopeKey", instance.ScopeKey),
            ("InstanceId", instance.Id), ("OperationKey", "activity.execute"), ("ActorUserId", null), ("ResourceTypeKey", "step"), ("ResourceId", id),
            ("OutcomeKey", "succeeded"), ("DetailJson", preparing ? "{\"nodeTypeKey\":\"process.collaboration\",\"phaseKey\":\"preparing\"}" : "{\"nodeTypeKey\":\"process.collaboration\",\"phaseKey\":\"requested\"}"),
            ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        return Result<int>.Success(acceptance.MaximumActivities);
    }

    private static Dictionary<string, object?> P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<int> Invalid() => Result<int>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
}
