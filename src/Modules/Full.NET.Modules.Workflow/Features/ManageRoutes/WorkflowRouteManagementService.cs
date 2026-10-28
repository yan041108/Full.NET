using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Observability;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageRoutes;

/// <summary>未知路由只能在根锁内由精确授权修复；失败Result回滚根恢复、选择、换代与后继。</summary>
internal sealed class WorkflowRouteManagementService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    ICurrentTenant tenant, IClock clock, IIdGenerator ids, ITenantFeatureEntitlementPort entitlements, WorkflowRouteDecisionExecutor executor)
{
    public async Task<Result<WorkflowRouteDecisionResponse>> GetAsync(Guid instanceId, Guid id, CancellationToken token)
    {
        if (instanceId == Guid.Empty || id == Guid.Empty) return Failure<WorkflowRouteDecisionResponse>(ErrorType.Validation);
        var scope = WorkflowManagementScope.Resolve(tenant);
        var instance = await InstanceAsync(instanceId, scope.TenantScopeKey, token).ConfigureAwait(false);
        var row = await RouteAsync(instanceId, id, scope.TenantScopeKey, token).ConfigureAwait(false);
        if (instance is null || row is null) return Failure<WorkflowRouteDecisionResponse>(ErrorType.NotFound);
        var plan = await PlanAsync(instance, token).ConfigureAwait(false);
        if (plan is null || !plan.DynamicRoutes.Routes.TryGetValue(row.NodeKey, out var route)) return Failure<WorkflowRouteDecisionResponse>(ErrorType.Conflict);
        return Result<WorkflowRouteDecisionResponse>.Success(new(row.Id, instanceId, row.NodeKey, row.StatusKey, row.Revision,
            instance.StatusKey, instance.Revision, row.ExecutionEpochId, row.TargetEpochId, row.SelectedTargetKey, row.NextNodeKey, row.NextWorkId,
            route.Targets.Select(target => new WorkflowRouteTargetResponse(target.Target.TargetKey, target.Target.NodeKey, target.Target.RegionKey,
                target.Reenters, target.Target.MaximumIterations)).ToArray(), route.NormalNextNodeKey, row.CreatedAtUtc, row.AppliedAtUtc));
    }

    public async Task<Result<WorkflowRouteResolutionResponse>> ReconcileAsync(Guid instanceId, Guid id, Guid actorId,
        ReconcileWorkflowRouteRequest request, CancellationToken token)
    {
        if (instanceId == Guid.Empty || id == Guid.Empty || actorId == Guid.Empty || request.ExpectedInstanceRevision < 1 || request.ExpectedRevision < 1 ||
            request.SelectedTargetKey is { } key && (!WorkflowNodeKeyValidator.IsValid(key) || key.Length > 64) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 512 || request.Reason.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || !Regex.IsMatch(request.IdempotencyKey, "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$"))
            return Failure<WorkflowRouteResolutionResponse>(ErrorType.Validation);
        request = request with { Reason = request.Reason.Trim() };
        var scope = WorkflowManagementScope.Resolve(tenant);
        if (await WorkflowTenantFeatureEntitlementGate.TryGetDenialAsync(scope, entitlements, token).ConfigureAwait(false) is { } denial)
            return WorkflowTenantFeatureEntitlementGate.Deny<WorkflowRouteResolutionResponse>(denial);
        var hash = WorkflowJsonCanonicalizer.Compile(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("instanceId", instanceId); writer.WriteString("routeId", id);
            writer.WriteNumber("instanceRevision", request.ExpectedInstanceRevision); writer.WriteNumber("revision", request.ExpectedRevision);
            writer.WriteString("selectedTargetKey", request.SelectedTargetKey); writer.WriteString("reason", request.Reason); writer.WriteEndObject();
        }).ContentHash;
        return await transaction.ExecuteResultAsync(async inner =>
        {
            var parameters = WorkflowSqlParameters.Create(("Id", instanceId), ("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey));
            if (await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction, parameters, inner).ConfigureAwait(false) != 1)
                return Failure<WorkflowRouteResolutionResponse>(ErrorType.NotFound);
            var instance = await InstanceAsync(instanceId, scope.TenantScopeKey, inner).ConfigureAwait(false);
            var row = await RouteAsync(instanceId, id, scope.TenantScopeKey, inner).ConfigureAwait(false);
            if (instance is null || row is null) return Failure<WorkflowRouteResolutionResponse>(ErrorType.NotFound);
            var receiptParameters = WorkflowSqlParameters.Create(("RouteId", id), ("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey),
                ("ActorUserId", actorId), ("IdempotencyKey", request.IdempotencyKey));
            var receipt = await query.QuerySingleOrDefaultAsync<WorkflowRouteResolutionRecord>(WorkflowRouteSql.FindResolution, receiptParameters, inner).ConfigureAwait(false);
            if (receipt is not null) return receipt.RequestHash == hash && row.StatusKey == "applied" && row.NextWorkId is not null && row.TargetEpochId is not null
                ? Success(row) : Failure<WorkflowRouteResolutionResponse>(ErrorType.Conflict);
            if (instance.StatusKey != "suspended" || instance.Revision != request.ExpectedInstanceRevision || row.Revision != request.ExpectedRevision ||
                row.StatusKey != "decision_unknown" || row.DefinitionVersionId != instance.DefinitionVersionId)
                return Failure<WorkflowRouteResolutionResponse>(ErrorType.Conflict);
            var plan = await PlanAsync(instance, inner).ConfigureAwait(false);
            if (plan is null || !plan.DynamicRoutes.Routes.TryGetValue(row.NodeKey, out var route)) return Failure<WorkflowRouteResolutionResponse>(ErrorType.Conflict);
            var target = request.SelectedTargetKey is null ? null : route.Targets.SingleOrDefault(item => item.Target.TargetKey == request.SelectedTargetKey);
            var draftNode = await ConfigurationAsync(instance, row.NodeKey, inner).ConfigureAwait(false);
            if (request.SelectedTargetKey is not null && target is null || draftNode is null ||
                WorkflowJsonCanonicalizer.Compile(draftNode.Value).ContentHash != row.PolicyHash || WorkflowRouteDecisionExecutor.Hash(row.InputJson) != row.InputHash)
                return Failure<WorkflowRouteResolutionResponse>(ErrorType.Conflict);
            // 其他未知业务效果不能被路由修复顺带恢复；保留其原有人工处置入口。
            foreach (var blocked in new[] { WorkflowConnectorSql.CountBlocked, WorkflowAsyncBarrierSql.CountBlocked,
                         WorkflowBusinessCommandSql.CountBlocked, WorkflowSubprocessSql.CountBlocked, WorkflowVoteSql.CountUnresolved })
                if (await query.QuerySingleOrDefaultAsync<int>(blocked, parameters, inner).ConfigureAwait(false) > 0)
                    return Failure<WorkflowRouteResolutionResponse>(ErrorType.Conflict);
            var now = clock.UtcNow;
            var release = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
                WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false);
            if (release is null || !WorkflowDesignerRelease.AllowsExecution(await WorkflowDesignerRelease.ReadBindingsAsync(query, release.FormVersionId, scope, inner).ConfigureAwait(false)))
                return Failure<WorkflowRouteResolutionResponse>(ErrorType.Conflict);
            if (await command.ExecuteAsync(WorkflowSql.ResumeInstanceWithRevision,
                WorkflowSqlParameters.Create(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey), ("Revision", instance.Revision)), inner).ConfigureAwait(false) != 1)
                return Failure<WorkflowRouteResolutionResponse>(ErrorType.Conflict);
            var nextNode = target?.Target.NodeKey ?? route.NormalNextNodeKey;
            var workId = ids.NewId();
            var write = WorkflowSqlParameters.Create(("Id", id), ("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey),
                ("Revision", row.Revision), ("SelectedTargetKey", request.SelectedTargetKey), ("NextNodeKey", nextNode), ("NextWorkId", workId), ("Now", now));
            if (await command.ExecuteAsync(WorkflowRouteSql.CompleteStep, write, inner).ConfigureAwait(false) != 1 ||
                await command.ExecuteAsync(WorkflowRouteSql.FreezeSelection, write, inner).ConfigureAwait(false) != 1)
                return Failure<WorkflowRouteResolutionResponse>(ErrorType.Conflict);
            var frozen = row with { StatusKey = "ready", SelectedTargetKey = request.SelectedTargetKey, NextNodeKey = nextNode, NextWorkId = workId, Revision = row.Revision + 1 };
            var applied = await executor.ApplyReadyAsync(instance with { StatusKey = "active", Revision = instance.Revision + 1 }, frozen, plan.DynamicRoutes, null, now, inner).ConfigureAwait(false);
            if (!applied.Applied) return Failure<WorkflowRouteResolutionResponse>(ErrorType.Conflict);
            var updated = await RouteAsync(instanceId, id, scope.TenantScopeKey, inner).ConfigureAwait(false);
            if (updated is not { StatusKey: "applied", NextWorkId: not null, TargetEpochId: not null }) return Failure<WorkflowRouteResolutionResponse>(ErrorType.Conflict);
            if (await command.ExecuteAsync(WorkflowRouteSql.InsertResolution, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("RouteId", id),
                ("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey), ("ActorUserId", actorId), ("IdempotencyKey", request.IdempotencyKey),
                ("RequestHash", hash), ("Reason", request.Reason), ("Now", now)), inner).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("workflow.route.resolution_conflict");
            var detail = WorkflowJsonCanonicalizer.Compile(writer =>
            {
                writer.WriteStartObject(); writer.WriteString("reason", request.Reason); writer.WriteString("selectedTargetKey", request.SelectedTargetKey);
                writer.WriteString("requestHash", hash); writer.WriteString("nextWorkId", updated.NextWorkId.Value); writer.WriteEndObject();
            }).CanonicalJson;
            await command.ExecuteAsync(WorkflowSql.InsertDomainAudit, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", scope.TenantId),
                ("ScopeKey", scope.ScopeKey), ("InstanceId", instanceId), ("OperationKey", "route.reconcile"), ("ActorUserId", actorId),
                ("ResourceTypeKey", "step"), ("ResourceId", id), ("OutcomeKey", "succeeded"), ("DetailJson", detail), ("CreatedAtUtc", now),
                ("CorrelationJson", LogCorrelationContext.Capture().ToJson())), inner).ConfigureAwait(false);
            return Success(updated);
        }, token).ConfigureAwait(false);
    }

    private Task<WorkflowInstanceRecord?> InstanceAsync(Guid id, string scope, CancellationToken token) => query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
        WorkflowSql.FindInstanceById, WorkflowSqlParameters.Create(("Id", id), ("TenantScopeKey", scope)), token);
    private Task<WorkflowRouteDecisionRecord?> RouteAsync(Guid root, Guid id, string scope, CancellationToken token) => query.QuerySingleOrDefaultAsync<WorkflowRouteDecisionRecord>(
        WorkflowRouteSql.Find, WorkflowSqlParameters.Create(("Id", id), ("InstanceId", root), ("TenantScopeKey", scope)), token);
    private async Task<WorkflowDefinitionDraft?> DraftAsync(WorkflowInstanceRecord instance, CancellationToken token)
    {
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        return asset is null ? null : JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
    }
    private async Task<WorkflowRuntimePlan?> PlanAsync(WorkflowInstanceRecord instance, CancellationToken token)
    {
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null) return null;
        var draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        return draft is not null && schema is not null && WorkflowRuntimePlan.TryCreate(draft, schema, out var plan) ? plan : null;
    }
    private async Task<JsonElement?> ConfigurationAsync(WorkflowInstanceRecord instance, string key, CancellationToken token) =>
        (await DraftAsync(instance, token).ConfigureAwait(false))?.Nodes.SingleOrDefault(node => node.NodeKey == key)?.Config;
    private static Result<WorkflowRouteResolutionResponse> Success(WorkflowRouteDecisionRecord row) =>
        Result<WorkflowRouteResolutionResponse>.Success(new(row.Id, row.NextWorkId!.Value, row.TargetEpochId!.Value, row.NextNodeKey!));
    private static Result<T> Failure<T>(ErrorType type) => Result<T>.Failure(new Error(type switch
    {
        ErrorType.Validation => WorkflowErrorCodes.SchemaInvalid,
        ErrorType.NotFound => WorkflowErrorCodes.InstanceNotFound,
        _ => WorkflowErrorCodes.InvalidTransition,
    }, "The workflow route operation is not valid.", type));
}
