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
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>个人转办和代理先准备可信目标与签名，再在实例锁事务内保留历史并原子交接同一席位。</summary>
internal sealed class WorkflowTodoTransferService(IQueryExecutor query, ICommandExecutor command,
    ICommandTransaction transaction, ICurrentTenant tenant, IClock clock, IIdGenerator ids,
    IHostUserBatchSelectionDirectory hostUsers, ITenantMemberSelectionDirectory tenantUsers,
    WorkflowBranchStateCoordinator branches, WorkflowTodoHandoffWriter handoffs,
    WorkflowSignatureCoordinator signatures)
{

    /// <summary>本人交接当前待办；固定首次回执优先于后来状态和候选变化，不新增审批票。</summary>
    public Task<Result<WorkflowInstanceResponse>> TransferAsync(Guid todoId, Guid actor,
        TransferWorkflowTodoRequest request, CancellationToken token = default) =>
        ExecuteAsync(todoId, actor, request, WorkflowTodoHandoffAuthority.PersonalTransfer, token);

    /// <summary>代理与转办共用交接事务，但动作、平台授权及不可变授权依据保持独立。</summary>
    public Task<Result<WorkflowInstanceResponse>> DelegateAsync(Guid todoId, Guid actor,
        DelegateWorkflowTodoRequest request, CancellationToken token = default) =>
        ExecuteAsync(todoId, actor, new(request.AssigneeUserId, request.ExpectedRevision, request.ExpectedFormRevision,
            request.Comment, request.IdempotencyKey, request.SignatureFileId), WorkflowTodoHandoffAuthority.Delegation, token);

    private async Task<Result<WorkflowInstanceResponse>> ExecuteAsync(Guid todoId, Guid actor,
        TransferWorkflowTodoRequest request, WorkflowTodoHandoffAuthority authority, CancellationToken token)
    {
        if (request.AssigneeUserId == Guid.Empty || request.ExpectedRevision < 1 || request.ExpectedFormRevision < 1 ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128 || request.Comment is { Length: > 512 })
            return Fail(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        request = request with { IdempotencyKey = request.IdempotencyKey.Trim(), Comment = request.Comment?.Trim() };
        var scope = WorkflowManagementScope.Resolve(tenant);
        var todo = await FindTodoAsync(todoId, scope, token).ConfigureAwait(false);
        if (todo is null) return Fail(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        if (todo.AssigneeUserId != actor) return Fail(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        var instance = await FindInstanceAsync(todo.InstanceId, scope, token).ConfigureAwait(false);
        if (instance?.FormVersionId is null) return Fail(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        var action = WorkflowTodoHandoffWriter.ActionKey(authority);
        var prepared = await PrepareAsync(todo, instance, scope, actor, request, action, token).ConfigureAwait(false);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            request, WorkflowJsonSerializerContext.Default.TransferWorkflowTodoRequest))));
        try
        {
            return await transaction.ExecuteResultAsync(async inner =>
            {
                if (await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                    P(("Id", todo.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false) != 1)
                    return Conflict();
                return await TransferCoreAsync(todoId, actor, request, scope, hash, prepared, authority, inner).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        finally
        {
            // 重放可能不消费本次准备；确认/释放只按本次证据身份探测，不由成功响应猜测提交。
            await signatures.FinishAsync(prepared.Signature, token).ConfigureAwait(false);
        }
    }

    /// <summary>只在事务外读取当前表单、可信目标和本人签名，准备结果不能替代事务内重验。</summary>
    private async Task<Preparation> PrepareAsync(WorkflowTodoRuntimeRecord todo, WorkflowInstanceRecord instance,
        WorkflowManagementScope scope, Guid actor, TransferWorkflowTodoRequest request, string action, CancellationToken token)
    {
        if (todo.StatusKey != "active" || todo.Revision != request.ExpectedRevision || instance.StatusKey != "active")
            return Preparation.Empty;
        var policy = await ReadPolicyAsync(instance, todo, scope, token).ConfigureAwait(false);
        if (policy is null) return Preparation.Invalid(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        if (await query.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForProxy,
            P(("TodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null)
            return Preparation.Invalid(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
        var valid = policy.Validate(action, request.Comment, request.SignatureFileId);
        if (!valid.IsSuccess) return new(valid.Error, null, null);
        if (request.AssigneeUserId == actor)
            return Preparation.Invalid(WorkflowErrorCodes.TodoAssigneeUnchanged, ErrorType.Validation);
        var submission = await ReadSubmissionAsync(instance, todo, scope, token).ConfigureAwait(false);
        if (submission is null || submission.Revision != request.ExpectedFormRevision)
            return Preparation.Invalid(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        var exists = scope.TenantId is null
            ? (await hostUsers.FindActiveHostUsersAsync([request.AssigneeUserId], token).ConfigureAwait(false)).ContainsKey(request.AssigneeUserId)
            : (await tenantUsers.FindActiveTenantMembersAsync([request.AssigneeUserId], token).ConfigureAwait(false)).ContainsKey(request.AssigneeUserId);
        if (!exists) return Preparation.Invalid(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid, ErrorType.Validation);
        var signature = await signatures.PrepareAsync(todo, instance, actor, action, request.IdempotencyKey,
            request.SignatureFileId, submission!.Revision, submission.SubmissionJson, token).ConfigureAwait(false);
        return new(signature.Error, signature.IsSuccess ? signature.Value : null, submission.SubmissionJson);
    }

    /// <summary>实例锁内重验资源和固定输入，原子关闭旧待办、创建新激活并重绑原席位。</summary>
    private async Task<Result<WorkflowInstanceResponse>> TransferCoreAsync(Guid todoId, Guid actor,
        TransferWorkflowTodoRequest request, WorkflowManagementScope scope, string hash, Preparation prepared,
        WorkflowTodoHandoffAuthority authority, CancellationToken token)
    {
        var action = WorkflowTodoHandoffWriter.ActionKey(authority);
        var todo = await FindTodoAsync(todoId, scope, token).ConfigureAwait(false);
        if (todo is null) return Fail(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        if (todo.AssigneeUserId != actor) return Fail(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        var instance = await FindInstanceAsync(todo.InstanceId, scope, token).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersion) return Fail(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        var receipt = await query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt,
            P(("InstanceId", instance.Id), ("IdempotencyKey", request.IdempotencyKey)), token).ConfigureAwait(false);
        if (receipt is not null)
        {
            if (receipt.TodoId != todoId || receipt.ActionKey != action || receipt.ActorUserId != actor ||
                receipt.RequestHash != hash || receipt.ResultStatusKey != "active" || receipt.ResultTodoId is null) return Conflict();
            return Result<WorkflowInstanceResponse>.Success(Map(instance, formVersion, receipt.InstanceRevision, receipt.ResultTodoId.Value));
        }
        if (todo.StatusKey != "active" || instance.StatusKey != "active")
            return Fail(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        if (todo.Revision != request.ExpectedRevision) return Conflict();
        if (await query.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForProxy,
            P(("TodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null)
            return Fail(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
        var policy = await ReadPolicyAsync(instance, todo, scope, token).ConfigureAwait(false);
        if (policy is null) return Fail(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var valid = policy.Validate(action, request.Comment, request.SignatureFileId);
        if (!valid.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(valid.Error!);
        if (prepared.Error is not null) return Result<WorkflowInstanceResponse>.Failure(prepared.Error);
        var submission = await ReadSubmissionAsync(instance, todo, scope, token).ConfigureAwait(false);
        if (submission is null || submission.Revision != request.ExpectedFormRevision || submission.SubmissionJson != prepared.SubmissionJson) return Conflict();
        var planned = await handoffs.ResolveAsync(todo, instance, scope, actor, request.AssigneeUserId,
            authority, token).ConfigureAwait(false);
        if (!planned.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(planned.Error!);
        var values = JsonSerializer.Deserialize(submission.SubmissionJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (values is null) return Fail(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var now = clock.UtcNow;
        var written = await handoffs.WriteAsync(planned.Value!, todo, instance, scope, values, submission.Revision, now, token).ConfigureAwait(false);
        if (!written.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(written.Error!);
        var nextTodo = written.Value;
        var signed = await signatures.WriteAsync(prepared.Signature, todo, instance, actor, action, request.IdempotencyKey,
            request.SignatureFileId, submission.Revision, submission.SubmissionJson, token).ConfigureAwait(false);
        if (!signed.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(signed.Error!);
        var detail = new WorkflowTodoTransferAuditDetail(planned.Value!.OriginTodoId, todoId, nextTodo,
            planned.Value.OriginalAssigneeUserId, actor, request.AssigneeUserId, actor,
            planned.Value.Depth, WorkflowTodoHandoffWriter.AuthorizationKey(authority), submission.Revision);
        await command.ExecuteAsync(WorkflowSql.InsertApprovalActionRecord,
            P(("Id", ids.NewId()), ("InstanceId", instance.Id), ("StepId", todo.StepId), ("TodoId", todoId), ("ActionKey", action),
                ("ActorUserId", actor), ("InstanceRevision", instance.Revision + 1), ("IdempotencyKey", request.IdempotencyKey),
                ("CommentSummary", request.Comment), ("CreatedAtUtc", now), ("ResultStatusKey", "active"), ("ResultTodoId", nextTodo)), token).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            P(("Id", ids.NewId()), ("InstanceId", instance.Id), ("StepId", todo.StepId), ("TransitionKey", "todo." + action),
                ("FromStatusKey", "active"), ("ToStatusKey", "active"), ("IdempotencyKey", request.IdempotencyKey),
                ("Summary", hash), ("CreatedAtUtc", now), ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson())), token).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            P(("Id", ids.NewId()), ("TenantId", scope.TenantId), ("ScopeKey", scope.ScopeKey), ("InstanceId", instance.Id),
                ("OperationKey", "todo." + action), ("ActorUserId", actor), ("ResourceTypeKey", "todo"), ("ResourceId", todoId),
                ("OutcomeKey", "succeeded"), ("DetailJson", JsonSerializer.Serialize(detail, WorkflowJsonSerializerContext.Default.WorkflowTodoTransferAuditDetail)),
                ("CreatedAtUtc", now), ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson())), token).ConfigureAwait(false);
        return Result<WorkflowInstanceResponse>.Success(Map(instance, formVersion, instance.Revision + 1, nextTodo));
    }

    private Task<WorkflowTodoRuntimeRecord?> FindTodoAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, P(("Id", id), ("TenantScopeKey", scope.TenantScopeKey)), token);
    private Task<WorkflowInstanceRecord?> FindInstanceAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, P(("Id", id), ("TenantScopeKey", scope.TenantScopeKey)), token);
    private async Task<WorkflowHumanActionPolicy?> ReadPolicyAsync(WorkflowInstanceRecord instance, WorkflowTodoRuntimeRecord todo,
        WorkflowManagementScope scope, CancellationToken token)
    {
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            P(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        return asset is not null && WorkflowHumanActionPolicy.TryResolve(asset.CanonicalJson, todo.NodeKey, out var policy) ? policy : null;
    }
    private async Task<WorkflowFormSubmissionRecord?> ReadSubmissionAsync(WorkflowInstanceRecord instance,
        WorkflowTodoRuntimeRecord todo, WorkflowManagementScope scope, CancellationToken token)
    {
        var submission = await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            P(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        return await branches.ReadAsync(todo, submission, scope.TenantScopeKey, token).ConfigureAwait(false);
    }
    private static object P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<WorkflowInstanceResponse> Conflict() => Fail(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
    private static Result<WorkflowInstanceResponse> Fail(string code, ErrorType type) =>
        Result<WorkflowInstanceResponse>.Failure(new Error(code, "The workflow todo cannot be transferred.", type));
    private static WorkflowInstanceResponse Map(WorkflowInstanceRecord instance, Guid formVersion, long revision, Guid todoId) =>
        new(instance.Id, instance.DefinitionVersionId, formVersion, instance.BusinessType, instance.BusinessId,
            instance.BusinessTitle, "active", revision, todoId, instance.StartedAtUtc);
    private sealed record Preparation(Error? Error, WorkflowPreparedSignature? Signature, string? SubmissionJson)
    {
        public static readonly Preparation Empty = new(null, null, null);
        public static Preparation Invalid(string code, ErrorType type) => new(new Error(code, "Transfer preparation failed.", type), null, null);
    }
}
