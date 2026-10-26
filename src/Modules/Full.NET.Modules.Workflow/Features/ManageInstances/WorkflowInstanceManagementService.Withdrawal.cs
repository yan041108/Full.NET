using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageInstances;

/// <summary>发起人撤回是独立用例；平台权限、发布边界和外部副作用屏障同时成立才可关闭实例。</summary>
internal sealed partial class WorkflowInstanceManagementService
{
    public async Task<Result<WorkflowInstanceResponse>> WithdrawAsync(Guid instanceId, Guid actor,
        WithdrawWorkflowInstanceRequest request, CancellationToken token = default)
    {
        if (actor == Guid.Empty || request.ExpectedRevision < 1 || string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 128 || request.Reason is { Length: > 512 })
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        request = request with { IdempotencyKey = request.IdempotencyKey.Trim(), Reason = NormalizeReason(request.Reason) };
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.FormVersionId is null) return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        if (instance.StartedById != actor) return Failure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request,
            WorkflowJsonSerializerContext.Default.WithdrawWorkflowInstanceRequest))));
        return await transaction.ExecuteResultAsync(async inner =>
        {
            if (await commandExecutor.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false) != 1)
                return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            return await WithdrawCoreAsync(instanceId, actor, request, scope, hash, inner).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }

    private async Task<Result<WorkflowInstanceResponse>> WithdrawCoreAsync(Guid instanceId, Guid actor,
        WithdrawWorkflowInstanceRequest request, WorkflowManagementScope scope, string hash, CancellationToken token)
    {
        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersion) return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        if (instance.StartedById != actor) return Failure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
        var receipt = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt,
            Parameters(("InstanceId", instanceId), ("IdempotencyKey", request.IdempotencyKey)), token).ConfigureAwait(false);
        if (receipt is not null)
            return receipt.ActionKey == "withdraw" && receipt.ActorUserId == actor && receipt.RequestHash == hash && receipt.ResultStatusKey == "cancelled" && receipt.TodoId is null
                ? Result<WorkflowInstanceResponse>.Success(new(instance.Id, instance.DefinitionVersionId, formVersion, instance.BusinessType,
                    instance.BusinessId, instance.BusinessTitle, "cancelled", receipt.InstanceRevision, null, instance.StartedAtUtc))
                : Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        if (instance.StatusKey is not ("active" or "suspended")) return Failure(WorkflowErrorCodes.InstanceTerminal, ErrorType.Conflict);
        if (instance.Revision != request.ExpectedRevision) return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        var policy = await ReadWithdrawalPolicyAsync(instance, scope, token).ConfigureAwait(false);
        if (policy is null)
            return Failure(WorkflowErrorCodes.InstanceWithdrawalBoundaryInvalid, ErrorType.Conflict);
        var parameters = Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey), ("ActorUserId", actor));
        var reached = await queryExecutor.QueryAsync<WorkflowWithdrawalActivityBoundary>(WorkflowWithdrawalSql.ListReached, parameters, token).ConfigureAwait(false);
        var actions = await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowWithdrawalSql.CountHumanActions, parameters, token).ConfigureAwait(false);
        if (reached.Count > WorkflowDefinitionCompiler.MaximumNodes * 8 || actions < 0 || !policy.Allows(reached, actions != 0))
            return Failure(WorkflowErrorCodes.InstanceWithdrawalBoundaryInvalid, ErrorType.Conflict);
        var now = clock.UtcNow;
        if (await commandExecutor.ExecuteAsync(WorkflowSql.CancelInstanceWithRevision,
            Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey), ("CancelledById", actor), ("CancelledAtUtc", now),
                ("CancellationReason", request.Reason), ("Revision", instance.Revision)), token).ConfigureAwait(false) != 1)
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        await WorkflowHumanActivityLifecycle.CancelAsync(commandExecutor, instanceId, scope.TenantScopeKey, now, token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowActivitySql.CancelApprovalSlots, Parameters(("InstanceId", instanceId), ("Now", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.CancelAllActiveTodosByInstance,
            Parameters(("InstanceId", instanceId), ("CompletedAtUtc", now), ("ResultActionKey", "withdraw")), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.CancelAllActiveStepsByInstance,
            Parameters(("InstanceId", instanceId), ("StatusKey", "cancelled"), ("CompletedAtUtc", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowActivitySql.CancelWork,
            Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey), ("Now", now)), token).ConfigureAwait(false);
        await parallelJoinCoordinator.CancelWaitingJoinsAsync(instanceId, now, token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertApprovalActionRecord,
            Parameters(("Id", idGenerator.NewId()), ("InstanceId", instanceId), ("StepId", null), ("TodoId", null), ("ActionKey", "withdraw"),
                ("ActorUserId", actor), ("InstanceRevision", instance.Revision + 1), ("IdempotencyKey", request.IdempotencyKey),
                ("CommentSummary", request.Reason), ("CreatedAtUtc", now), ("ResultStatusKey", "cancelled"), ("ResultTodoId", null)), token).ConfigureAwait(false);
        var correlation = Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson();
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            Parameters(("CorrelationJson", correlation), ("Id", idGenerator.NewId()), ("InstanceId", instanceId), ("StepId", null),
                ("TransitionKey", "instance.withdraw"), ("FromStatusKey", instance.StatusKey), ("ToStatusKey", "cancelled"),
                ("IdempotencyKey", request.IdempotencyKey), ("Summary", hash), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            Parameters(("CorrelationJson", correlation), ("Id", idGenerator.NewId()), ("TenantId", scope.TenantId), ("ScopeKey", scope.ScopeKey),
                ("InstanceId", instanceId), ("OperationKey", "instance.withdraw"), ("ActorUserId", actor), ("ResourceTypeKey", "instance"),
                ("ResourceId", instanceId), ("OutcomeKey", "succeeded"), ("DetailJson", JsonSerializer.Serialize(new WorkflowWithdrawalAuditDetail(policy.ModeKey,
                    instance.Revision), WorkflowJsonSerializerContext.Default.WorkflowWithdrawalAuditDetail)), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        // 沿用实例取消终态通知契约，撤回身份、授权和发布边界由独立动作及审计明确保留。
        await notificationPublisher.PublishInstanceCancelledAsync(instance.Id, instance.StartedById, instance.BusinessType, instance.BusinessId, now, token).ConfigureAwait(false);
        return Result<WorkflowInstanceResponse>.Success(new(instance.Id, instance.DefinitionVersionId, formVersion, instance.BusinessType,
            instance.BusinessId, instance.BusinessTitle, "cancelled", instance.Revision + 1, null, instance.StartedAtUtc));
    }

    private async Task<WorkflowWithdrawalPolicy?> ReadWithdrawalPolicyAsync(WorkflowInstanceRecord instance, WorkflowManagementScope scope, CancellationToken token)
    {
        var asset = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            Parameters(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var draft = asset is null ? null : JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var starts = draft?.Nodes.Where(node => node.NodeTypeKey == "start").ToArray();
        return starts is [var start] && WorkflowWithdrawalPolicy.TryRead(start.Config, out var policy) ? policy : null;
    }

    private async Task<WorkflowWithdrawalAvailabilityResponse?> ReadWithdrawalAvailabilityAsync(WorkflowInstanceRecord instance,
        Guid actor, WorkflowManagementScope scope, CancellationToken token)
    {
        if (instance.StartedById != actor || instance.StatusKey is not ("active" or "suspended")) return null;
        var policy = await ReadWithdrawalPolicyAsync(instance, scope, token).ConfigureAwait(false);
        if (policy is null || policy.ModeKey == "disabled") return null;
        var parameters = Parameters(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey), ("ActorUserId", actor));
        var reached = await queryExecutor.QueryAsync<WorkflowWithdrawalActivityBoundary>(WorkflowWithdrawalSql.ListReached, parameters, token).ConfigureAwait(false);
        var actions = await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowWithdrawalSql.CountHumanActions, parameters, token).ConfigureAwait(false);
        return new(reached.Count <= WorkflowDefinitionCompiler.MaximumNodes * 8 && actions >= 0 && policy.Allows(reached, actions != 0), policy.ModeKey);
    }
}

/// <summary>撤回只携带本实例并发修订、原因及请求身份，发起人与租户均来自可信资源上下文。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record WithdrawWorkflowInstanceRequest(long ExpectedRevision, string IdempotencyKey, string? Reason = null);
internal sealed record WorkflowWithdrawalAuditDetail(string ModeKey, long PreviousRevision);
