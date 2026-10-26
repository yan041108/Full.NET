using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>释放只关闭本人的实际待办并重开同一个候选池，绝对SLA与剩余提醒预算原样保留。</summary>
internal sealed class WorkflowClaimReleaseService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    ICurrentTenant tenant, IClock clock, WorkflowBranchStateCoordinator branches, WorkflowSignatureCoordinator signatures,
    WorkflowClaimPoolService pools)
{
    public async Task<Result<WorkflowInstanceResponse>> ReleaseAsync(Guid todoId, Guid actor, ReleaseWorkflowTodoRequest request, CancellationToken token)
    {
        if (todoId == Guid.Empty || actor == Guid.Empty || request.ExpectedRevision < 1 || request.ExpectedPoolRevision < 1 || request.ExpectedFormRevision < 1 ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128 || request.Comment is { Length: > 512 })
            return Fail(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        request = request with { IdempotencyKey = request.IdempotencyKey.Trim(), Comment = request.Comment?.Trim() };
        var scope = WorkflowManagementScope.Resolve(tenant);
        var todo = await FindTodoAsync(todoId, scope, token).ConfigureAwait(false);
        if (todo is null) return Fail(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        if (todo.AssigneeUserId != actor) return Fail(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        if (todo.NodeTypeKey != WorkflowHumanActivityKinds.Claim) return Fail(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
        var instance = await FindInstanceAsync(todo.InstanceId, scope, token).ConfigureAwait(false);
        var pool = await FindPoolAsync(todo.StepId, scope, token).ConfigureAwait(false);
        if (instance?.FormVersionId is null || pool is null) return Conflict();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            todoId.ToString("D") + "|" + JsonSerializer.Serialize(request, WorkflowJsonSerializerContext.Default.ReleaseWorkflowTodoRequest))));
        var prior = await ReceiptAsync(instance.Id, request.IdempotencyKey, token).ConfigureAwait(false);
        if (prior is not null) return await pools.ReplayAsync(pool, instance, actor, "release", hash, prior, scope, token).ConfigureAwait(false);
        if (await query.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForProxy,
            P(("TodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null)
            return Fail(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            P(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null || !WorkflowHumanActionPolicy.TryResolve(asset.CanonicalJson, todo.NodeKey, out var policy))
            return Fail(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        var valid = policy!.Validate("release", request.Comment, request.SignatureFileId);
        if (!valid.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(valid.Error!);
        var submission = await ReadSubmissionAsync(todo, scope, token).ConfigureAwait(false);
        if (submission?.Revision != request.ExpectedFormRevision) return Conflict();
        var prepared = await signatures.PrepareAsync(todo, instance, actor, "release", request.IdempotencyKey, request.SignatureFileId,
            submission.Revision, submission.SubmissionJson, token).ConfigureAwait(false);
        if (!prepared.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(prepared.Error!);
        try
        {
            return await transaction.ExecuteResultAsync(async inner =>
            {
                if (await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                    P(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false) != 1) return Conflict();
                var actualTodo = await FindTodoAsync(todoId, scope, inner).ConfigureAwait(false);
                var actualPool = await FindPoolAsync(pool.Id, scope, inner).ConfigureAwait(false);
                var owned = await FindInstanceAsync(instance.Id, scope, inner).ConfigureAwait(false);
                if (actualTodo?.AssigneeUserId != actor || actualPool is null || owned?.FormVersionId is null) return Conflict();
                var receipt = await ReceiptAsync(instance.Id, request.IdempotencyKey, inner).ConfigureAwait(false);
                if (receipt is not null) return await pools.ReplayAsync(actualPool, owned, actor, "release", hash, receipt, scope, inner).ConfigureAwait(false);
                if (owned.StatusKey != "active" || actualTodo.StatusKey != "active" || actualTodo.Revision != request.ExpectedRevision ||
                    actualPool.CurrentTodoId != todoId || !WorkflowClaimRules.CanRelease(new(actualPool.AllowRelease), actualPool.StatusKey,
                        actor, actualPool.CurrentAssigneeUserId, actualPool.ClaimCycle, actualPool.Revision, request.ExpectedPoolRevision)) return Conflict();
                var current = await ReadSubmissionAsync(actualTodo, scope, inner).ConfigureAwait(false);
                if (current?.Revision != submission.Revision || current.SubmissionJson != submission.SubmissionJson) return Conflict();
                var attempt = await query.QuerySingleOrDefaultAsync<WorkflowClaimAttemptRecord>(WorkflowClaimSql.FindAttemptForTodo,
                    P(("PoolId", pool.Id), ("InstanceId", instance.Id), ("TodoId", todoId), ("ActorUserId", actor), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false);
                var schedule = await query.QuerySingleOrDefaultAsync<WorkflowClaimTodoScheduleRecord>(WorkflowClaimSql.ReadTodoSchedule,
                    P(("InstanceId", instance.Id), ("TodoId", todoId), ("ActorUserId", actor), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false);
                if (attempt?.StatusKey != "active" || schedule is null) return Conflict();
                var now = clock.UtcNow;
                var parameters = P(("Id", attempt.Id), ("PoolId", pool.Id), ("InstanceId", instance.Id), ("TodoId", todoId),
                    ("ActorUserId", actor), ("TenantScopeKey", scope.TenantScopeKey), ("Revision", attempt.Revision), ("StatusKey", "released"), ("Now", now));
                if (await command.ExecuteAsync(WorkflowSql.CompleteTodoWithRevision,
                        P(("Id", todoId), ("AssigneeUserId", actor), ("TenantScopeKey", scope.TenantScopeKey), ("CompletedAtUtc", now),
                            ("ResultActionKey", "release"), ("Revision", actualTodo.Revision)), inner).ConfigureAwait(false) != 1 ||
                    await command.ExecuteAsync(WorkflowClaimSql.CompleteAttempt, parameters, inner).ConfigureAwait(false) != 1 ||
                    await command.ExecuteAsync(WorkflowClaimSql.ReleasePool,
                        P(("PoolId", pool.Id), ("InstanceId", instance.Id), ("TodoId", todoId), ("ActorUserId", actor),
                            ("TenantScopeKey", scope.TenantScopeKey), ("Revision", actualPool.Revision), ("NextReminderAtUtc", schedule.NextReminderAtUtc),
                            ("ReminderCount", schedule.ReminderCount), ("EscalatedAtUtc", schedule.EscalatedAtUtc)), inner).ConfigureAwait(false) != 1 ||
                    await command.ExecuteAsync(WorkflowClaimSql.AssignStep,
                        P(("PoolId", pool.Id), ("InstanceId", instance.Id), ("AssigneeUserId", null), ("TenantScopeKey", scope.TenantScopeKey),
                            ("StepRevision", actualTodo.StepRevision)), inner).ConfigureAwait(false) != 1 ||
                    await command.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision,
                        P(("Id", owned.Id), ("TenantScopeKey", scope.TenantScopeKey), ("Revision", owned.Revision)), inner).ConfigureAwait(false) != 1) return Conflict();
                var signed = await signatures.WriteAsync(prepared.Value, actualTodo, owned, actor, "release", request.IdempotencyKey,
                    request.SignatureFileId, current.Revision, current.SubmissionJson, inner).ConfigureAwait(false);
                if (!signed.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(signed.Error!);
                await pools.WriteJournalAsync(actualPool, owned, actor, todoId, "release", request.IdempotencyKey, hash, now, scope, inner).ConfigureAwait(false);
                return Result<WorkflowInstanceResponse>.Success(new(owned.Id, owned.DefinitionVersionId, owned.FormVersionId.Value,
                    owned.BusinessType, owned.BusinessId, owned.BusinessTitle, "active", owned.Revision + 1, null, owned.StartedAtUtc));
            }, token).ConfigureAwait(false);
        }
        finally { await signatures.FinishAsync(prepared.Value, token).ConfigureAwait(false); }
    }

    private Task<WorkflowTodoRuntimeRecord?> FindTodoAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, P(("Id", id), ("TenantScopeKey", scope.TenantScopeKey)), token);
    private Task<WorkflowClaimPoolRecord?> FindPoolAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowClaimPoolRecord>(WorkflowClaimSql.FindPool, P(("PoolId", id), ("TenantScopeKey", scope.TenantScopeKey)), token);
    private Task<WorkflowInstanceRecord?> FindInstanceAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, P(("Id", id), ("TenantScopeKey", scope.TenantScopeKey)), token);
    private Task<WorkflowActionReceiptRecord?> ReceiptAsync(Guid instance, string key, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt, P(("InstanceId", instance), ("IdempotencyKey", key)), token);
    private async Task<WorkflowFormSubmissionRecord?> ReadSubmissionAsync(WorkflowTodoRuntimeRecord todo, WorkflowManagementScope scope, CancellationToken token)
    {
        var submission = await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            P(("InstanceId", todo.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        return await branches.ReadAsync(todo, submission, scope.TenantScopeKey, token).ConfigureAwait(false);
    }
    private static object P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<WorkflowInstanceResponse> Fail(string code, ErrorType type) => Result<WorkflowInstanceResponse>.Failure(new Error(code, "The claim cannot be released.", type));
    private static Result<WorkflowInstanceResponse> Conflict() => Fail(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
}

/// <summary>释放必须绑定当前个人待办、候选池和可签署表单的三个独立修订。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ReleaseWorkflowTodoRequest(long ExpectedRevision, long ExpectedPoolRevision, long ExpectedFormRevision,
    string IdempotencyKey, string? Comment = null, Guid? SignatureFileId = null);
