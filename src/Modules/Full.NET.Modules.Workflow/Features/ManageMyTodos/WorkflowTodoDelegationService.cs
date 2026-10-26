using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>本人代理查询与收回；授权寿命由实际代理待办决定，不另建可能过期失真的权限缓存。</summary>
internal sealed class WorkflowTodoDelegationService(IQueryExecutor query, ICommandExecutor command,
    ICommandTransaction transaction, ICurrentTenant tenant, IClock clock, IIdGenerator ids,
    IHostUserBatchSelectionDirectory hostUsers, ITenantMemberSelectionDirectory tenantUsers,
    WorkflowBranchStateCoordinator branches, WorkflowTodoHandoffWriter handoffs)
{
    public async Task<Result<WorkflowTodoDelegationStateResponse>> GetAsync(Guid todoId, Guid actor, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        var owned = await FindTodoAsync(todoId, scope, token).ConfigureAwait(false);
        if (owned is null) return ReadFail(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        if (owned.AssigneeUserId != actor) return ReadFail(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        var grant = await FindGrantAsync(todoId, scope, token).ConfigureAwait(false) ??
            await query.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForProxy,
                P(("TodoId", todoId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (grant is null) return Result<WorkflowTodoDelegationStateResponse>.Success(new(null));
        var proxy = await FindTodoAsync(grant.ProxyTodoId, scope, token).ConfigureAwait(false);
        var instance = await FindInstanceAsync(owned.InstanceId, scope, token).ConfigureAwait(false);
        if (proxy is null || instance is null || proxy.InstanceId != owned.InstanceId || proxy.AssigneeUserId != grant.ProxyUserId)
            return ReadFail(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        var submission = proxy.StatusKey == "active" ? await SubmissionAsync(proxy, scope, token).ConfigureAwait(false) : null;
        var policy = await PolicyAsync(instance, proxy, scope, token).ConfigureAwait(false);
        var status = proxy.StatusKey == "active" ? "active" : proxy.ResultActionKey switch
        {
            "delegate_recall" => "recalled", "reassign" => "reassigned",
            _ => proxy.StatusKey == "completed" ? "completed" : "cancelled",
        };
        return Result<WorkflowTodoDelegationStateResponse>.Success(new(new(grant.OriginTodoId, grant.ProxyTodoId,
            grant.OriginalAssigneeUserId, grant.ProxyUserId, status, proxy.Revision, submission?.Revision ?? 0,
            actor == grant.OriginalAssigneeUserId && owned.Id == grant.OriginTodoId && proxy.StatusKey == "active" &&
            instance.StatusKey == "active" && submission is not null && policy?.Actions.Contains("delegate") == true, grant.CreatedAtUtc)));
    }

    /// <summary>收回是授权管理动作，不伪造代理人签名或决定；实际业务动作仍由恢复后的本人提交。</summary>
    public async Task<Result<WorkflowInstanceResponse>> RecallAsync(Guid originTodoId, Guid actor,
        RecallWorkflowTodoDelegationRequest request, CancellationToken token)
    {
        if (originTodoId == Guid.Empty || actor == Guid.Empty || request.ExpectedRevision < 1 || request.ExpectedFormRevision < 1 ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128 || request.Comment is { Length: > 512 })
            return Fail(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        request = request with { IdempotencyKey = request.IdempotencyKey.Trim(), Comment = request.Comment?.Trim() };
        var scope = WorkflowManagementScope.Resolve(tenant);
        var origin = await FindTodoAsync(originTodoId, scope, token).ConfigureAwait(false);
        if (origin is null) return Fail(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        if (origin.AssigneeUserId != actor) return Fail(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        var instance = await FindInstanceAsync(origin.InstanceId, scope, token).ConfigureAwait(false);
        if (instance?.FormVersionId is null) return Fail(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            request, WorkflowJsonSerializerContext.Default.RecallWorkflowTodoDelegationRequest))));
        var prior = await ReceiptAsync(instance.Id, request.IdempotencyKey, token).ConfigureAwait(false);
        if (prior is not null) return Replay(prior, originTodoId, actor, hash, instance);
        var grant = await FindGrantAsync(originTodoId, scope, token).ConfigureAwait(false);
        if (grant?.OriginalAssigneeUserId != actor) return Fail(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
        var proxy = await FindTodoAsync(grant.ProxyTodoId, scope, token).ConfigureAwait(false);
        WorkflowFormSubmissionRecord? submission = null;
        var prepared = false;
        if (proxy?.StatusKey == "active" && proxy.Revision == request.ExpectedRevision && instance.StatusKey == "active")
        {
            submission = await SubmissionAsync(proxy, scope, token).ConfigureAwait(false);
            if (submission?.Revision == request.ExpectedFormRevision)
            {
                // 身份目录只在事务外查询；准备失败仍须先在锁内检查已并发提交的首次回执。
                prepared = scope.TenantId is null
                    ? (await hostUsers.FindActiveHostUsersAsync([actor], token).ConfigureAwait(false)).ContainsKey(actor)
                    : (await tenantUsers.FindActiveTenantMembersAsync([actor], token).ConfigureAwait(false)).ContainsKey(actor);
            }
        }
        return await transaction.ExecuteResultAsync(async inner =>
        {
            if (await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                P(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false) != 1) return Conflict();
            var current = await FindInstanceAsync(instance.Id, scope, inner).ConfigureAwait(false);
            if (current?.FormVersionId is null) return Conflict();
            var receipt = await ReceiptAsync(instance.Id, request.IdempotencyKey, inner).ConfigureAwait(false);
            if (receipt is not null) return Replay(receipt, originTodoId, actor, hash, current);
            if (!prepared || submission is null) return Conflict();
            var actual = await FindTodoAsync(grant.ProxyTodoId, scope, inner).ConfigureAwait(false);
            if (current.StatusKey != "active" || actual?.StatusKey != "active" || actual.Revision != request.ExpectedRevision ||
                actual.InstanceId != current.Id || actual.StepId != origin.StepId || actual.AssigneeUserId != grant.ProxyUserId) return Conflict();
            var policy = await PolicyAsync(current, actual, scope, inner).ConfigureAwait(false);
            if (policy?.Actions.Contains("delegate") != true) return Fail(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
            if (policy.CommentRequired && string.IsNullOrWhiteSpace(request.Comment)) return Fail(WorkflowErrorCodes.TodoCommentRequired, ErrorType.Validation);
            var values = await SubmissionAsync(actual, scope, inner).ConfigureAwait(false);
            if (values?.Revision != request.ExpectedFormRevision || values.SubmissionJson != submission.SubmissionJson) return Conflict();
            var decoded = JsonSerializer.Deserialize(values.SubmissionJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
            if (decoded is null) return Fail(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
            var planned = await handoffs.ResolveAsync(actual, current, scope, actor, actor,
                WorkflowTodoHandoffAuthority.DelegationRecall, inner).ConfigureAwait(false);
            if (!planned.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(planned.Error!);
            var now = clock.UtcNow;
            var written = await handoffs.WriteAsync(planned.Value!, actual, current, scope, decoded, values.Revision, now, inner).ConfigureAwait(false);
            if (!written.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(written.Error!);
            var next = written.Value;
            await command.ExecuteAsync(WorkflowSql.InsertApprovalActionRecord,
                P(("Id", ids.NewId()), ("InstanceId", current.Id), ("StepId", actual.StepId), ("TodoId", originTodoId),
                    ("ActionKey", "delegate_recall"), ("ActorUserId", actor), ("InstanceRevision", current.Revision + 1),
                    ("IdempotencyKey", request.IdempotencyKey), ("CommentSummary", request.Comment), ("CreatedAtUtc", now),
                    ("ResultStatusKey", "active"), ("ResultTodoId", next)), inner).ConfigureAwait(false);
            await command.ExecuteAsync(WorkflowSql.InsertExecutionLog,
                P(("Id", ids.NewId()), ("InstanceId", current.Id), ("StepId", actual.StepId), ("TransitionKey", "todo.delegate_recall"),
                    ("FromStatusKey", "active"), ("ToStatusKey", "active"), ("IdempotencyKey", request.IdempotencyKey),
                    ("Summary", hash), ("CreatedAtUtc", now), ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson())), inner).ConfigureAwait(false);
            var detail = new WorkflowTodoTransferAuditDetail(planned.Value!.OriginTodoId, actual.Id, next,
                planned.Value.OriginalAssigneeUserId, actual.AssigneeUserId, actor, actor, planned.Value.Depth,
                WorkflowPermissions.TodosRecallDelegation, values.Revision);
            await command.ExecuteAsync(WorkflowSql.InsertDomainAudit,
                P(("Id", ids.NewId()), ("TenantId", scope.TenantId), ("ScopeKey", scope.ScopeKey), ("InstanceId", current.Id),
                    ("OperationKey", "todo.delegate_recall"), ("ActorUserId", actor), ("ResourceTypeKey", "todo"), ("ResourceId", originTodoId),
                    ("OutcomeKey", "succeeded"), ("DetailJson", JsonSerializer.Serialize(detail, WorkflowJsonSerializerContext.Default.WorkflowTodoTransferAuditDetail)),
                    ("CreatedAtUtc", now), ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson())), inner).ConfigureAwait(false);
            return Result<WorkflowInstanceResponse>.Success(Map(current, current.Revision + 1, next));
        }, token).ConfigureAwait(false);
    }

    private static Result<WorkflowInstanceResponse> Replay(WorkflowActionReceiptRecord receipt, Guid origin, Guid actor, string hash, WorkflowInstanceRecord instance) =>
        receipt.TodoId == origin && receipt.ActorUserId == actor && receipt.ActionKey == "delegate_recall" && receipt.RequestHash == hash &&
        receipt.ResultStatusKey == "active" && receipt.ResultTodoId is { } next
            ? Result<WorkflowInstanceResponse>.Success(Map(instance, receipt.InstanceRevision, next)) : Conflict();
    private Task<WorkflowTodoRuntimeRecord?> FindTodoAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, P(("Id", id), ("TenantScopeKey", scope.TenantScopeKey)), token);
    private Task<WorkflowInstanceRecord?> FindInstanceAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, P(("Id", id), ("TenantScopeKey", scope.TenantScopeKey)), token);
    private Task<WorkflowTodoDelegationRecord?> FindGrantAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForOrigin, P(("TodoId", id), ("TenantScopeKey", scope.TenantScopeKey)), token);
    private Task<WorkflowActionReceiptRecord?> ReceiptAsync(Guid instance, string key, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt, P(("InstanceId", instance), ("IdempotencyKey", key)), token);
    private async Task<WorkflowFormSubmissionRecord?> SubmissionAsync(WorkflowTodoRuntimeRecord todo, WorkflowManagementScope scope, CancellationToken token)
    {
        var value = await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            P(("InstanceId", todo.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        return await branches.ReadAsync(todo, value, scope.TenantScopeKey, token).ConfigureAwait(false);
    }
    private async Task<WorkflowHumanActionPolicy?> PolicyAsync(WorkflowInstanceRecord instance, WorkflowTodoRuntimeRecord todo,
        WorkflowManagementScope scope, CancellationToken token)
    {
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            P(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        return asset is not null && WorkflowHumanActionPolicy.TryResolve(asset.CanonicalJson, todo.NodeKey, out var policy) ? policy : null;
    }
    private static WorkflowInstanceResponse Map(WorkflowInstanceRecord instance, long revision, Guid next) =>
        new(instance.Id, instance.DefinitionVersionId, instance.FormVersionId!.Value, instance.BusinessType, instance.BusinessId,
            instance.BusinessTitle, "active", revision, next, instance.StartedAtUtc);
    private static object P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<WorkflowInstanceResponse> Conflict() => Fail(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
    private static Result<WorkflowInstanceResponse> Fail(string code, ErrorType type) =>
        Result<WorkflowInstanceResponse>.Failure(new Error(code, "The todo delegation cannot be recalled.", type));
    private static Result<WorkflowTodoDelegationStateResponse> ReadFail(string code, ErrorType type) =>
        Result<WorkflowTodoDelegationStateResponse>.Failure(new Error(code, "The todo delegation is unavailable.", type));
}
