using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Workflow.Serialization;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Features.ManageInstances;

/// <summary>在可信作用域内执行工作流实例强制恢复与活动待办改派，并保持审计原子一致。</summary>
/// <param name="queryExecutor">受控查询执行器。</param>
/// <param name="commandExecutor">显式 SQL 命令执行器。</param>
/// <param name="transaction">Workflow 本地事务边界。</param>
/// <param name="currentTenant">可信当前租户上下文。</param>
/// <param name="clock">统一 UTC 时钟。</param>
/// <param name="idGenerator">UUID v7 标识生成器。</param>
/// <param name="hostUserDirectory">Host 活动用户批量目录。</param>
/// <param name="tenantUserDirectory">当前 Tenant 活动用户批量目录。</param>
/// <param name="handoffs">在实例锁内保留原待办身份的交接写入器。</param>
/// <param name="branchStates">当前主表单或真实分支输入读取器。</param>
internal sealed class WorkflowInstanceRecoveryService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IHostUserBatchSelectionDirectory hostUserDirectory,
    ITenantMemberSelectionDirectory tenantUserDirectory,
    WorkflowTodoHandoffWriter handoffs,
    WorkflowBranchStateCoordinator branchStates)
{
    private const string ActionKey = "reassign";
    private const string TransitionKey = "todo.reassign";
    private const string RecoverActionKey = "recover";
    private const string RecoverTransitionKey = "instance.recover";

    /// <summary>把显式指定的活动待办交接给同一可信作用域内的活动用户。</summary>
    /// <param name="instanceId">待改派的工作流实例标识。</param>
    /// <param name="actorUserId">执行高权限操作的用户标识。</param>
    /// <param name="request">目标办理人、并发修订号、原因和幂等键。</param>
    /// <param name="cancellationToken">取消当前异步操作的令牌。</param>
    /// <returns>改派后的实例快照或稳定业务错误。</returns>
    public async Task<Result<WorkflowInstanceResponse>> ReassignAsync(
        Guid instanceId,
        Guid actorUserId,
        ReassignWorkflowInstanceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsValid(instanceId, actorUserId, request)) return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var hash = HashRequest(request);
        // 首次结果优先于后来目标身份/实例状态变化，且必须仍属于当前可信作用域。
        var existingInstance = await FindInstanceAsync(instanceId, scope, cancellationToken).ConfigureAwait(false);
        if (existingInstance?.FormVersionId is null) return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        var existingReceipt = await FindReceiptAsync(instanceId, request.IdempotencyKey.Trim(), cancellationToken).ConfigureAwait(false);
        if (existingReceipt is not null) return ReplayReassignment(existingInstance, request, actorUserId, hash, existingReceipt);
        if (!await IsActiveTargetAsync(request.AssigneeUserId, scope, cancellationToken).ConfigureAwait(false))
            return Failure(WorkflowErrorCodes.TodoAssigneeNotFound, ErrorType.Validation);
        try
        {
            return await transaction.ExecuteResultAsync(async token =>
            {
                if (await commandExecutor.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                    Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 1)
                    return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
                return await ReassignCoreAsync(instanceId, actorUserId, request, scope, hash, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return await ResolveReplayAsync(instanceId, actorUserId, request, scope, hash, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>强制恢复当前作用域内已暂停的实例，必须记录原因且不得新建待办或通知。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="actorUserId">执行高权限操作的用户标识。</param>
    /// <param name="request">修订号、原因和幂等键。</param>
    /// <param name="cancellationToken">取消当前异步操作的令牌。</param>
    /// <returns>恢复后的实例快照或稳定业务错误。</returns>
    public async Task<Result<WorkflowInstanceResponse>> RecoverAsync(
        Guid instanceId,
        Guid actorUserId,
        RecoverWorkflowInstanceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsValid(instanceId, actorUserId, request))
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var requestHash = HashRecoverRequest(request);
        try
        {
            var result = await transaction.ExecuteResultAsync(
                token => RecoverCoreAsync(instanceId, actorUserId, request, scope, requestHash, token),
                cancellationToken).ConfigureAwait(false);
            return !result.IsSuccess && result.Error?.Code == WorkflowErrorCodes.RevisionConflict
                ? await ResolveRecoverReplayAsync(
                    instanceId, actorUserId, request, scope, requestHash, cancellationToken)
                    .ConfigureAwait(false)
                : result;
        }
        catch (DataCommandException exception) when (
            exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return await ResolveRecoverReplayAsync(
                instanceId, actorUserId, request, scope, requestHash, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>在进入 Workflow 本地事务前，通过 Identity 最小契约确认目标用户属于可信作用域。</summary>
    /// <param name="assigneeUserId">目标办理人标识。</param>
    /// <param name="scope">由当前租户上下文解析出的可信作用域。</param>
    /// <param name="cancellationToken">取消当前目录查询的令牌。</param>
    /// <returns>目标用户仍处于活动状态且属于当前作用域时返回 <see langword="true"/>。</returns>
    private async Task<bool> IsActiveTargetAsync(
        Guid assigneeUserId,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid> ids = [assigneeUserId];
        if (scope.TenantId.HasValue)
        {
            var users = await tenantUserDirectory.FindActiveTenantMembersAsync(ids, cancellationToken)
                .ConfigureAwait(false);
            return users.ContainsKey(assigneeUserId);
        }

        var hostUsers = await hostUserDirectory.FindActiveHostUsersAsync(ids, cancellationToken)
            .ConfigureAwait(false);
        return hostUsers.ContainsKey(assigneeUserId);
    }

    /// <summary>在单一本地事务内更新待办和实例修订，并追加回执、轨迹、审计与通知事实。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="actorUserId">执行改派的用户标识。</param>
    /// <param name="request">已完成边界校验的改派请求。</param>
    /// <param name="scope">可信工作流作用域。</param>
    /// <param name="requestHash">用于幂等语义比对的规范请求摘要。</param>
    /// <param name="cancellationToken">取消当前事务操作的令牌。</param>
    /// <returns>改派后的实例快照或导致事务回滚的失败结果。</returns>
    private async Task<Result<WorkflowInstanceResponse>> ReassignCoreAsync(
        Guid instanceId,
        Guid actorUserId,
        ReassignWorkflowInstanceRequest request,
        WorkflowManagementScope scope,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var instance = await FindInstanceAsync(instanceId, scope, cancellationToken).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersionId) return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        var key = request.IdempotencyKey.Trim();
        var receipt = await FindReceiptAsync(instanceId, key, cancellationToken).ConfigureAwait(false);
        if (receipt is not null) return ReplayReassignment(instance, request, actorUserId, requestHash, receipt);
        if (instance.StatusKey == "suspended") return Failure(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        if (instance.StatusKey != "active") return Failure(WorkflowErrorCodes.InstanceTerminal, ErrorType.Conflict);
        if (instance.Revision != request.ExpectedRevision) return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById,
            Parameters(("Id", request.TodoId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        if (todo is null || todo.InstanceId != instanceId) return Failure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        if (todo.StatusKey != "active" || todo.Revision != request.ExpectedTodoRevision) return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        var planned = await handoffs.ResolveAsync(todo, instance, scope, actorUserId, request.AssigneeUserId,
            WorkflowTodoHandoffAuthority.AdministrativeReassignment, cancellationToken).ConfigureAwait(false);
        if (!planned.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(planned.Error!);
        var submission = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        submission = await branchStates.ReadAsync(todo, submission, scope.TenantScopeKey, cancellationToken).ConfigureAwait(false);
        var values = submission is null ? null : JsonSerializer.Deserialize(submission.SubmissionJson,
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (submission is null || values is null) return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        var now = clock.UtcNow;
        var written = await handoffs.WriteAsync(planned.Value!, todo, instance, scope, values, submission.Revision, now, cancellationToken).ConfigureAwait(false);
        if (!written.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(written.Error!);
        var next = written.Value;
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertApprovalActionRecord,
            Parameters(("Id", idGenerator.NewId()), ("InstanceId", instanceId), ("StepId", todo.StepId), ("TodoId", todo.Id),
                ("ActionKey", ActionKey), ("ActorUserId", actorUserId), ("InstanceRevision", instance.Revision + 1),
                ("IdempotencyKey", key), ("CommentSummary", NormalizeReason(request.Reason)), ("CreatedAtUtc", now),
                ("ResultStatusKey", "active"), ("ResultTodoId", next)), cancellationToken).ConfigureAwait(false);
        var correlation = Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson();
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            Parameters(("CorrelationJson", correlation), ("Id", idGenerator.NewId()), ("InstanceId", instanceId), ("StepId", todo.StepId),
                ("TransitionKey", TransitionKey), ("FromStatusKey", "active"), ("ToStatusKey", "active"), ("IdempotencyKey", key),
                ("Summary", requestHash), ("CreatedAtUtc", now)), cancellationToken).ConfigureAwait(false);
        var detail = new WorkflowTodoTransferAuditDetail(planned.Value!.OriginTodoId, todo.Id, next,
            planned.Value.OriginalAssigneeUserId, todo.AssigneeUserId, request.AssigneeUserId, actorUserId,
            planned.Value.Depth, WorkflowPermissions.InstancesRecover, submission.Revision);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            Parameters(("CorrelationJson", correlation), ("Id", idGenerator.NewId()), ("TenantId", scope.TenantId), ("ScopeKey", scope.ScopeKey),
                ("InstanceId", instanceId), ("OperationKey", "instance.reassign"), ("ActorUserId", actorUserId), ("ResourceTypeKey", "todo"),
                ("ResourceId", todo.Id), ("OutcomeKey", "succeeded"), ("DetailJson", JsonSerializer.Serialize(detail,
                    WorkflowJsonSerializerContext.Default.WorkflowTodoTransferAuditDetail)), ("CreatedAtUtc", now)), cancellationToken).ConfigureAwait(false);
        return Result<WorkflowInstanceResponse>.Success(new(instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, "active", instance.Revision + 1, next, instance.StartedAtUtc));
    }

    /// <summary>
    /// 在事务内把暂停实例恢复为运行，保留原活动待办，并把原因写入回执与审计。
    /// </summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="actorUserId">执行强制恢复的用户标识。</param>
    /// <param name="request">已通过边界校验的请求。</param>
    /// <param name="scope">可信工作流作用域。</param>
    /// <param name="requestHash">规范请求摘要。</param>
    /// <param name="cancellationToken">取消当前事务操作的令牌。</param>
    /// <returns>恢复后的实例快照或导致回滚的失败结果。</returns>
    private async Task<Result<WorkflowInstanceResponse>> RecoverCoreAsync(
        Guid instanceId,
        Guid actorUserId,
        RecoverWorkflowInstanceRequest request,
        WorkflowManagementScope scope,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var instance = await FindInstanceAsync(instanceId, scope, cancellationToken).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersionId)
        {
            return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        var idempotencyKey = request.IdempotencyKey.Trim();
        var receipt = await FindReceiptAsync(instanceId, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (receipt is not null)
        {
            if (receipt.ActionKey != RecoverActionKey || receipt.ActorUserId != actorUserId ||
                receipt.RequestHash != requestHash)
            {
                return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            }

            var replayWork = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActiveWorkRecord>(
                WorkflowSql.FindActiveWorkByInstance,
                Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken).ConfigureAwait(false);
            return Result<WorkflowInstanceResponse>.Success(Map(instance, formVersionId, replayWork?.TodoId));
        }

        if (instance.StatusKey is "completed" or "rejected" or "cancelled")
        {
            return Failure(WorkflowErrorCodes.InstanceTerminal, ErrorType.Conflict);
        }

        if (instance.StatusKey != "suspended")
        {
            return Failure(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        }

        if (instance.Revision != request.ExpectedRevision)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        if (await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowVoteSql.CountUnresolved,
            Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false) > 0)
            return Failure(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        // 管理员恢复不能越过未知外部结果或失败屏障，须先完成原活动的显式对账。
        if (await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowRouteSql.CountUnknown,
                Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false)>0 ||
            await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowConnectorSql.CountBlocked,
                Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false)>0 ||
            await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowAsyncBarrierSql.CountBlocked,
                Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false)>0)
            return Failure(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        var work = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActiveWorkRecord>(
            WorkflowSql.FindActiveWorkByInstance,
            Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        if (work is null && await queryExecutor.QuerySingleOrDefaultAsync<int>(
                WorkflowActivitySql.CountRecoverableWork,
                Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken)
                .ConfigureAwait(false) == 0)
            return Failure(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);

        var instanceUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.ResumeInstanceWithRevision,
            Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey),
                ("Revision", request.ExpectedRevision)), cancellationToken).ConfigureAwait(false);
        if (instanceUpdated != 1)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(WorkflowActivitySql.RequeueFaultedWork,
            Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey), ("Now", now)), cancellationToken)
            .ConfigureAwait(false);
        var reason = request.Reason.Trim();
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertActionRecord,
            Parameters(("Id", idGenerator.NewId()), ("InstanceId", instanceId),
                ("StepId", work?.StepId), ("TodoId", work?.TodoId), ("ActionKey", RecoverActionKey),
                ("ActorUserId", actorUserId), ("InstanceRevision", request.ExpectedRevision + 1),
                ("IdempotencyKey", idempotencyKey), ("CommentSummary", reason),
                ("CreatedAtUtc", now)), cancellationToken).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            Parameters(("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()), ("Id", idGenerator.NewId()), ("InstanceId", instanceId),
                ("StepId", work?.StepId), ("TransitionKey", RecoverTransitionKey),
                ("FromStatusKey", "suspended"), ("ToStatusKey", "active"),
                ("IdempotencyKey", idempotencyKey), ("Summary", requestHash),
                ("CreatedAtUtc", now)), cancellationToken).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            Parameters(("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()), ("Id", idGenerator.NewId()), ("TenantId", scope.TenantId),
                ("ScopeKey", scope.ScopeKey), ("InstanceId", instanceId),
                ("OperationKey", RecoverTransitionKey), ("ActorUserId", actorUserId),
                ("ResourceTypeKey", "instance"), ("ResourceId", instanceId),
                ("OutcomeKey", "succeeded"),
                ("DetailJson", CreateRecoverAuditDetail(reason)),
                ("CreatedAtUtc", now)), cancellationToken).ConfigureAwait(false);

        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, "active",
            request.ExpectedRevision + 1, work?.TodoId, instance.StartedAtUtc));
    }

    /// <summary>在并发冲突或唯一键竞争后读取强制恢复回执，判断是否为同语义重放。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="actorUserId">执行强制恢复的用户标识。</param>
    /// <param name="request">原始强制恢复请求。</param>
    /// <param name="scope">可信工作流作用域。</param>
    /// <param name="requestHash">规范请求摘要。</param>
    /// <param name="cancellationToken">取消当前查询的令牌。</param>
    /// <returns>匹配回执对应的当前实例快照，否则返回修订冲突。</returns>
    private async Task<Result<WorkflowInstanceResponse>> ResolveRecoverReplayAsync(
        Guid instanceId,
        Guid actorUserId,
        RecoverWorkflowInstanceRequest request,
        WorkflowManagementScope scope,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var instance = await FindInstanceAsync(instanceId, scope, cancellationToken).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersionId)
        {
            return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        var receipt = await FindReceiptAsync(
            instanceId, request.IdempotencyKey.Trim(), cancellationToken).ConfigureAwait(false);
        if (receipt?.ActionKey != RecoverActionKey || receipt.ActorUserId != actorUserId ||
            receipt.RequestHash != requestHash)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var work = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActiveWorkRecord>(
            WorkflowSql.FindActiveWorkByInstance,
            Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        return Result<WorkflowInstanceResponse>.Success(Map(instance, formVersionId, work?.TodoId));
    }

    /// <summary>在并发冲突或唯一键竞争后读取已提交回执，判断是否为同语义重放。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="actorUserId">执行改派的用户标识。</param>
    /// <param name="request">原始改派请求。</param>
    /// <param name="scope">可信工作流作用域。</param>
    /// <param name="requestHash">规范请求摘要。</param>
    /// <param name="cancellationToken">取消当前查询的令牌。</param>
    /// <returns>匹配回执对应的当前实例快照，否则返回修订冲突。</returns>
    private async Task<Result<WorkflowInstanceResponse>> ResolveReplayAsync(
        Guid instanceId,
        Guid actorUserId,
        ReassignWorkflowInstanceRequest request,
        WorkflowManagementScope scope,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var instance = await FindInstanceAsync(instanceId, scope, cancellationToken).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersionId)
        {
            return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        var receipt = await FindReceiptAsync(
            instanceId, request.IdempotencyKey.Trim(), cancellationToken).ConfigureAwait(false);
        return receipt is null ? Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict)
            : ReplayReassignment(instance, request, actorUserId, requestHash, receipt);
    }

    private static Result<WorkflowInstanceResponse> ReplayReassignment(WorkflowInstanceRecord instance,
        ReassignWorkflowInstanceRequest request, Guid actor, string hash, WorkflowActionReceiptRecord receipt)
    {
        if (receipt.ActionKey != ActionKey || receipt.ActorUserId != actor || receipt.TodoId != request.TodoId ||
            receipt.RequestHash != hash || receipt.ResultStatusKey is null || receipt.ResultTodoId is null || instance.FormVersionId is not { } form)
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        return Result<WorkflowInstanceResponse>.Success(new(instance.Id, instance.DefinitionVersionId, form,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, receipt.ResultStatusKey,
            receipt.InstanceRevision, receipt.ResultTodoId, instance.StartedAtUtc));
    }

    /// <summary>读取当前可信作用域内的实例。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="scope">可信工作流作用域。</param>
    /// <param name="cancellationToken">取消当前查询的令牌。</param>
    /// <returns>实例持久化投影；不存在时返回空。</returns>
    private Task<WorkflowInstanceRecord?> FindInstanceAsync(
        Guid instanceId,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindInstanceById,
            Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken);

    /// <summary>按实例和幂等键读取动作回执。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="idempotencyKey">规范化幂等键。</param>
    /// <param name="cancellationToken">取消当前查询的令牌。</param>
    /// <returns>已提交动作回执；不存在时返回空。</returns>
    private Task<WorkflowActionReceiptRecord?> FindReceiptAsync(
        Guid instanceId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
            WorkflowSql.FindActionReceipt,
            Parameters(("InstanceId", instanceId), ("IdempotencyKey", idempotencyKey)),
            cancellationToken);

    /// <summary>验证输入长度、标识与乐观锁修订号。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="actorUserId">操作人标识。</param>
    /// <param name="request">待校验请求。</param>
    /// <returns>所有边界条件满足时返回 <see langword="true"/>。</returns>
    private static bool IsValid(
        Guid instanceId,
        Guid actorUserId,
        ReassignWorkflowInstanceRequest request) =>
        instanceId != Guid.Empty && actorUserId != Guid.Empty &&
        request.AssigneeUserId != Guid.Empty && request.TodoId != Guid.Empty && request.ExpectedRevision >= 1 && request.ExpectedTodoRevision >= 1 &&
        request.IdempotencyKey?.Trim() is { Length: >= 1 and <= 128 } &&
        request.Reason?.Trim() is not { Length: > 512 };

    /// <summary>校验强制恢复请求必须携带非空原因、修订号和幂等键。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="actorUserId">操作人标识。</param>
    /// <param name="request">待校验请求。</param>
    /// <returns>所有边界条件满足时返回 <see langword="true"/>。</returns>
    private static bool IsValid(
        Guid instanceId,
        Guid actorUserId,
        RecoverWorkflowInstanceRequest request) =>
        instanceId != Guid.Empty && actorUserId != Guid.Empty &&
        request.ExpectedRevision >= 1 &&
        request.IdempotencyKey.Trim() is { Length: >= 1 and <= 128 } &&
        request.Reason?.Trim() is { Length: >= 1 and <= 512 };

    /// <summary>生成与传输格式无关的强制恢复请求摘要。</summary>
    /// <param name="request">已通过边界校验的请求。</param>
    /// <returns>小写十六进制 SHA-256 摘要。</returns>
    private static string HashRecoverRequest(RecoverWorkflowInstanceRequest request)
    {
        var value = $"{request.ExpectedRevision}\n{request.Reason.Trim()}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>把强制恢复原因写入审计详情，原因只作为人类可读说明。</summary>
    /// <param name="reason">已规范化的恢复原因。</param>
    /// <returns>序列化后的 JSON 文档。</returns>
    private static string CreateRecoverAuditDetail(string reason) =>
        $"{{\"reason\":\"{EscapeJson(reason)}\"}}";

    /// <summary>转义审计详情中的原因文本，避免破坏 JSON 对象边界。</summary>
    /// <param name="value">已规范化的人类可读原因。</param>
    /// <returns>可作为 JSON 字符串内容的转义文本。</returns>
    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>生成与传输格式无关的改派请求摘要。</summary>
    /// <param name="request">已通过边界校验的请求。</param>
    /// <returns>小写十六进制 SHA-256 摘要。</returns>
    private static string HashRequest(ReassignWorkflowInstanceRequest request)
    {
        var value = $"{request.TodoId:D}\n{request.AssigneeUserId:D}\n{request.ExpectedRevision}\n{request.ExpectedTodoRevision}\n{NormalizeReason(request.Reason)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>判断回执是否属于相同操作人和相同请求语义。</summary>
    /// <param name="receipt">可能为空的已提交回执。</param>
    /// <param name="actorUserId">当前操作人标识。</param>
    /// <param name="requestHash">当前请求摘要。</param>
    /// <returns>动作键、操作人和摘要全部匹配时返回 <see langword="true"/>。</returns>
    private static bool IsReplay(
        WorkflowActionReceiptRecord? receipt,
        Guid actorUserId,
        string requestHash) =>
        receipt?.ActionKey == ActionKey && receipt.ActorUserId == actorUserId &&
        receipt.RequestHash == requestHash;

    /// <summary>去除原因首尾空白，并把空白原因收敛为空。</summary>
    /// <param name="reason">调用方提供的可选原因。</param>
    /// <returns>规范化原因或空。</returns>
    private static string? NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    /// <summary>生成不包含显示名和原因原文的最小审计详情。</summary>
    /// <param name="previousAssigneeUserId">原办理人标识。</param>
    /// <param name="newAssigneeUserId">新办理人标识。</param>
    /// <returns>稳定字段顺序的 JSON 对象。</returns>
    private static string CreateAuditDetail(Guid previousAssigneeUserId, Guid newAssigneeUserId) =>
        $"{{\"previousAssigneeUserId\":\"{previousAssigneeUserId:D}\",\"newAssigneeUserId\":\"{newAssigneeUserId:D}\"}}";

    /// <summary>把实例持久化投影映射为 HTTP 响应。</summary>
    /// <param name="instance">实例持久化投影。</param>
    /// <param name="formVersionId">非空表单版本标识。</param>
    /// <param name="activeTodoId">当前活动待办标识。</param>
    /// <returns>稳定实例响应。</returns>
    private static WorkflowInstanceResponse Map(
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        Guid? activeTodoId) =>
        new(instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, instance.StatusKey,
            instance.Revision, activeTodoId, instance.StartedAtUtc);

    /// <summary>构造受控 SQL 参数字典。</summary>
    /// <param name="pairs">参数名称和值。</param>
    /// <returns>可供 Dapper 边界消费的参数字典。</returns>
    private static Dictionary<string, object?> Parameters(
        params (string Name, object? Value)[] pairs) =>
        WorkflowSqlParameters.Create(pairs);

    /// <summary>创建统一的工作流实例失败结果。</summary>
    /// <param name="code">稳定机器错误码。</param>
    /// <param name="type">错误分类。</param>
    /// <returns>不包含敏感数据的失败结果。</returns>
    private static Result<WorkflowInstanceResponse> Failure(string code, ErrorType type) =>
        Result<WorkflowInstanceResponse>.Failure(
            new Error(code, "The workflow instance recovery operation failed.", type));
}
