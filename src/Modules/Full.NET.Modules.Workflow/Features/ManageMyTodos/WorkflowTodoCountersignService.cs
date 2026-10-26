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
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>处理活动待办的前加签、后加签、取消与办理链推进。</summary>
internal sealed class WorkflowTodoCountersignService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions,
    IHostUserBatchSelectionDirectory hostUserDirectory,
    ITenantMemberSelectionDirectory tenantUserDirectory,
    WorkflowNotificationOutboxPublisher notificationPublisher,
    WorkflowBranchStateCoordinator branchStates,
    WorkflowApprovalStepResolutionCoordinator stepResolution)
{
    /// <summary>读取当前待办关联的活动加签链。</summary>
    public async Task<Result<WorkflowTodoCountersignChainResponse>> GetChainAsync(
        Guid todoId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var todo = await FindOwnedTodoAsync(todoId, actorUserId, scope, cancellationToken).ConfigureAwait(false);
        if (todo is null)
        {
            return ChainFailure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        }

        var chain = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignChainRecord>(
            WorkflowSql.FindActiveCountersignChainByOriginTodo,
            WorkflowSqlParameters.Create(("OriginTodoId", todo.Id),
                ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        if (chain is null)
        {
            return ChainFailure(WorkflowErrorCodes.TodoCountersignChainNotFound, ErrorType.NotFound);
        }

        return Result<WorkflowTodoCountersignChainResponse>.Success(await MapChainAsync(chain, cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>由原办理人发起前加签或后加签。</summary>
    public async Task<Result<WorkflowInstanceResponse>> CountersignAsync(
        Guid todoId,
        Guid actorUserId,
        CountersignWorkflowTodoRequest request,
        CancellationToken cancellationToken = default)
    {
        var assignees = request.AssigneeUserIds?.Distinct().ToArray() ?? [];
        if (request.ExpectedRevision < 1 || string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 128 || request.Comment is { Length: > 512 } ||
            request.DirectionKey is not ("before" or "after") || assignees.Length is < 1 or > 10 ||
            assignees.Length != request.AssigneeUserIds?.Count || assignees.Contains(Guid.Empty) || assignees.Contains(actorUserId))
            return InstanceFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var todo = await FindOwnedTodoAsync(todoId, actorUserId, scope, cancellationToken).ConfigureAwait(false);
        if (todo is null) return InstanceFailure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        // 目录只在事务外准备；重复请求由事务内不可变回执裁定，不因人员后来停用而改写首次结果。
        var validAssignees = await ValidateAssigneesAsync(assignees, scope, cancellationToken).ConfigureAwait(false);
        return await transaction.ExecuteResultAsync(async token =>
        {
            if (await commandExecutor.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                WorkflowSqlParameters.Create(("Id", todo.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 1)
                return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            return await CountersignCoreAsync(todoId, actorUserId, request, validAssignees, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>由原办理人取消尚未完成的活动加签链。</summary>
    public async Task<Result<WorkflowInstanceResponse>> CancelAsync(
        Guid todoId,
        Guid actorUserId,
        CancelWorkflowTodoCountersignRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ExpectedRevision < 1 || string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 128 || request.Comment is { Length: > 512 })
            return InstanceFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var todo = await FindOwnedTodoAsync(todoId, actorUserId, scope, cancellationToken).ConfigureAwait(false);
        if (todo is null) return InstanceFailure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        return await transaction.ExecuteResultAsync(async token =>
        {
            if (await commandExecutor.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                WorkflowSqlParameters.Create(("Id", todo.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 1)
                return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            return await CancelCoreAsync(todoId, actorUserId, request, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>加签项不持有新的审批席位；原成员一席等待整条后加签完成。</summary>
    public async Task<bool> ManagesAsync(WorkflowTodoRuntimeRecord todo, WorkflowManagementScope scope, CancellationToken token)
    {
        var parameters = WorkflowSqlParameters.Create(("TodoId", todo.Id), ("OriginTodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey));
        if (await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(
            WorkflowSql.FindCountersignItemByTodoId, parameters, token).ConfigureAwait(false) is { ChainStatusKey: "active", StatusKey: "active" })
            return true;
        return await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignChainRecord>(
            WorkflowSql.FindActiveCountersignChainByOriginTodo, parameters, token).ConfigureAwait(false) is { DirectionKey: "after" };
    }

    /// <summary>判断当前办理动作是否应由加签链接管。</summary>
    public async Task<Result<WorkflowInstanceResponse>?> TryHandleActAsync(
        WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        string patchedSubmission,
        ActWorkflowTodoRequest request,
        string actionKey,
        Guid actorUserId,
        WorkflowManagementScope scope,
        WorkflowPreparedHumanActivities preparedApprovals,
        CancellationToken token)
    {
        if (actionKey is not ("approve" or "reject"))
        {
            return null;
        }

        var item = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(
            WorkflowSql.FindCountersignItemByTodoId,
            WorkflowSqlParameters.Create(("TodoId", todo.Id),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (item is not null)
        {
            if (item.StatusKey != "active" || item.ChainStatusKey != "active" || item.InstanceId != instance.Id || item.StepId != todo.StepId)
                return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            if (actionKey == "reject")
                return await RejectCountersignItemAsync(todo, instance, formVersionId, patchedSubmission, request,
                    actorUserId, scope, item, preparedApprovals, token).ConfigureAwait(false);
            return await CompleteCountersignItemApproveAsync(
                todo, instance, formVersionId, patchedSubmission, request, actorUserId, scope, item, preparedApprovals, token)
                .ConfigureAwait(false);
        }

        var afterChain = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignChainRecord>(
            WorkflowSql.FindActiveCountersignChainByOriginTodo,
            WorkflowSqlParameters.Create(("OriginTodoId", todo.Id),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (afterChain is not null && afterChain.DirectionKey == "after")
        {
            if (actionKey == "reject")
            {
                var now = clock.UtcNow;
                if (await commandExecutor.ExecuteAsync(WorkflowSql.CompleteTodoWithRevision,
                    WorkflowSqlParameters.Create(("Id", todo.Id), ("AssigneeUserId", actorUserId), ("TenantScopeKey", scope.TenantScopeKey),
                        ("CompletedAtUtc", now), ("ResultActionKey", "reject"), ("Revision", request.ExpectedRevision)), token).ConfigureAwait(false) != 1)
                    return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
                await commandExecutor.ExecuteAsync(WorkflowSql.CancelPendingCountersignItems,
                    WorkflowSqlParameters.Create(("ChainId", afterChain.Id)), token).ConfigureAwait(false);
                if (await commandExecutor.ExecuteAsync(WorkflowSql.CancelCountersignChain,
                    WorkflowSqlParameters.Create(("Id", afterChain.Id), ("OriginTodoId", todo.Id)), token).ConfigureAwait(false) != 1)
                    return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
                return await CompleteOriginDecisionAsync(todo, instance, formVersionId, patchedSubmission, scope,
                    todo.Id, "reject", preparedApprovals, token).ConfigureAwait(false);
            }
            return await DeferOriginApproveForAfterChainAsync(
                todo, instance, formVersionId, request, actorUserId, scope, afterChain, token)
                .ConfigureAwait(false);
        }

        return null;
    }

    private async Task<Result<WorkflowInstanceResponse>> CountersignCoreAsync(
        Guid todoId,
        Guid actorUserId,
        CountersignWorkflowTodoRequest request,
        bool validAssignees,
        CancellationToken token)
    {
        var idempotencyKey = request.IdempotencyKey.Trim();
        var direction = request.DirectionKey.Trim();
        if (request.ExpectedRevision < 1 ||
            idempotencyKey is not { Length: >= 1 and <= 128 } ||
            request.Comment is { Length: > 512 } ||
            direction is not ("before" or "after") ||
            request.AssigneeUserIds is not { Count: >= 1 and <= 10 })
        {
            return InstanceFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var assignees = request.AssigneeUserIds.Distinct().ToArray();
        if (assignees.Length != request.AssigneeUserIds.Count || assignees.Contains(actorUserId))
        {
            return InstanceFailure(WorkflowErrorCodes.TodoCountersignAssigneeInvalid, ErrorType.Validation);
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var todo = await FindOwnedTodoAsync(todoId, actorUserId, scope, token).ConfigureAwait(false);
        if (todo is null)
        {
            return InstanceFailure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        }

        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", todo.InstanceId),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersionId)
        {
            return InstanceFailure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        var requestHash = HashCountersignRequest(direction, assignees, request.ExpectedRevision, request.Comment);
        var receipt = await FindReceiptAsync(instance.Id, idempotencyKey, token).ConfigureAwait(false);
        if (receipt is not null)
        {
            return ReplayCountersign(todo.Id, instance, formVersionId, actorUserId, $"countersign.{direction}", requestHash, receipt);
        }

        if (!validAssignees)
            return InstanceFailure(WorkflowErrorCodes.TodoCountersignAssigneeInvalid, ErrorType.Validation);
        if (!WorkflowHumanActivityKinds.IsApproval(todo.NodeTypeKey) ||
            todo.ApprovalModeKey is not ("single" or "all" or "any" or "nOfM" or "percent" or "sequential" or "group") ||
            await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(WorkflowSql.FindCountersignItemByTodoId,
                WorkflowSqlParameters.Create(("TodoId", todo.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null)
            return InstanceFailure(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);

        if (todo.StatusKey != "active" || todo.Revision != request.ExpectedRevision ||
            instance.StatusKey != "active")
        {
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignChainRecord>(
            WorkflowSql.FindActiveCountersignChainByOriginTodo,
            WorkflowSqlParameters.Create(("OriginTodoId", todo.Id),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (existing is not null)
        {
            return InstanceFailure(WorkflowErrorCodes.TodoCountersignChainActive, ErrorType.Conflict);
        }

        var now = clock.UtcNow;
        var chainId = idGenerator.NewId();
        var firstTodoId = direction == "before" ? idGenerator.NewId() : (Guid?)null;
        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertCountersignChain,
            WorkflowSqlParameters.Create(("Id", chainId), ("InstanceId", instance.Id),
                ("StepId", todo.StepId), ("OriginTodoId", todo.Id), ("DirectionKey", direction),
                ("CreatedByUserId", actorUserId), ("CreatedAtUtc", now)), token).ConfigureAwait(false);


        var instanceUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.AdvanceInstanceWithRevision,
            WorkflowSqlParameters.Create(("Id", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey), ("Revision", instance.Revision)),
            token).ConfigureAwait(false);
        if (instanceUpdated != 1)
        {
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        if (direction == "before")
        {
            var suspended = await commandExecutor.ExecuteAsync(
                WorkflowSql.SuspendOriginTodoForBeforeCountersign,
                WorkflowSqlParameters.Create(("Id", todo.Id), ("AssigneeUserId", actorUserId),
                    ("TenantScopeKey", scope.TenantScopeKey), ("Revision", request.ExpectedRevision)),
                token).ConfigureAwait(false);
            if (suspended != 1)
            {
                return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            }

            await InsertCountersignTodoAsync(
                firstTodoId!.Value, todo, instance, assignees[0], scope, now, token).ConfigureAwait(false);
            await notificationPublisher.PublishTodoAssignedAsync(
                instance.Id, firstTodoId.Value, assignees[0], instance.BusinessType,
                instance.BusinessId, now, token).ConfigureAwait(false);
        }

        // 先创建实际 Todo，再写引用它的加签项；两者由本事务一起提交或回滚。
        for (var index = 0; index < assignees.Length; index++)
        {
            var sequence = index + 1;
            var itemStatus = direction == "before" && sequence == 1 ? "active" : "pending";
            var itemTodoId = direction == "before" && sequence == 1 ? firstTodoId : null;
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertCountersignItem,
                WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("ChainId", chainId),
                    ("SequenceNo", sequence), ("AssigneeUserId", assignees[index]),
                    ("TodoId", itemTodoId), ("StatusKey", itemStatus)), token).ConfigureAwait(false);
        }


        var activeTodoId = direction == "before" ? firstTodoId : todo.Id;
        await RecordChainActionAsync(todo, instance, scope, actorUserId, $"countersign.{direction}",
            idempotencyKey, requestHash, request.Comment, activeTodoId, now, token).ConfigureAwait(false);
        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, "active",
            instance.Revision + 1, activeTodoId, instance.StartedAtUtc));
    }

    private async Task<Result<WorkflowInstanceResponse>> CancelCoreAsync(
        Guid todoId,
        Guid actorUserId,
        CancelWorkflowTodoCountersignRequest request,
        CancellationToken token)
    {
        var idempotencyKey = request.IdempotencyKey.Trim();
        if (request.ExpectedRevision < 1 ||
            idempotencyKey is not { Length: >= 1 and <= 128 } ||
            request.Comment is { Length: > 512 })
        {
            return InstanceFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var todo = await FindOwnedTodoAsync(todoId, actorUserId, scope, token).ConfigureAwait(false);
        if (todo is null)
        {
            return InstanceFailure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        }

        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", todo.InstanceId),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersionId)
        {
            return InstanceFailure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"countersign.cancel\n{request.ExpectedRevision}\n{request.Comment?.Trim()}")));
        var receipt = await FindReceiptAsync(instance.Id, idempotencyKey, token).ConfigureAwait(false);
        if (receipt is not null)
            return ReplayCountersign(todo.Id, instance, formVersionId, actorUserId, "countersign.cancel", requestHash, receipt);

        var chain = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignChainRecord>(
            WorkflowSql.FindActiveCountersignChainByOriginTodo,
            WorkflowSqlParameters.Create(("OriginTodoId", todo.Id),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (chain is null)
        {
            return InstanceFailure(WorkflowErrorCodes.TodoCountersignChainNotFound, ErrorType.NotFound);
        }

        if (instance.StatusKey != "active" || todo.Revision != request.ExpectedRevision ||
            (chain.DirectionKey == "before" && todo.StatusKey != "before_countersign") ||
            (chain.DirectionKey == "after" && todo.StatusKey != "active" && !(todo.StatusKey == "completed" && todo.ResultActionKey == "approve")))
        {
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var now = clock.UtcNow;
        var items = await queryExecutor.QueryAsync<WorkflowCountersignItemRecord>(
            WorkflowSql.ListCountersignItemsByChain,
            WorkflowSqlParameters.Create(("ChainId", chain.Id)), token).ConfigureAwait(false);
        foreach (var item in items.Where(item => item.TodoId is not null && item.StatusKey == "active"))
        {
            var activeTodo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(
                WorkflowSql.FindTodoById,
                WorkflowSqlParameters.Create(("Id", item.TodoId), ("TenantScopeKey", scope.TenantScopeKey)),
                token).ConfigureAwait(false);
            if (activeTodo is null || activeTodo.InstanceId != instance.Id || activeTodo.StepId != chain.StepId || activeTodo.StatusKey != "active")
            {
                return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            }

            if (await commandExecutor.ExecuteAsync(
                WorkflowSql.CancelTodoWithRevision,
                WorkflowSqlParameters.Create(("Id", activeTodo.Id), ("InstanceId", instance.Id),
                    ("TenantScopeKey", scope.TenantScopeKey), ("CompletedAtUtc", now),
                    ("Revision", activeTodo.Revision)), token).ConfigureAwait(false) != 1)
                return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        await commandExecutor.ExecuteAsync(
            WorkflowSql.CancelPendingCountersignItems,
            WorkflowSqlParameters.Create(("ChainId", chain.Id)), token).ConfigureAwait(false);
        var chainCancelled = await commandExecutor.ExecuteAsync(
            WorkflowSql.CancelCountersignChain,
            WorkflowSqlParameters.Create(("Id", chain.Id), ("OriginTodoId", todo.Id)), token).ConfigureAwait(false);
        if (chainCancelled != 1)
        {
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        if (chain.DirectionKey == "after" && todo.StatusKey == "completed")
        {
            // 原办理人的真实批准已经留下证据；撤销剩余咨询只释放该席，不替原办理人制造新动作。
            var submission = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            submission = await branchStates.ReadAsync(todo, submission, scope.TenantScopeKey, token).ConfigureAwait(false);
            if (submission is null) return InstanceFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
            var completed = await CompleteOriginDecisionAsync(todo, instance, formVersionId, submission.SubmissionJson, scope,
                todo.Id, "approve", WorkflowPreparedHumanActivities.Empty, token).ConfigureAwait(false);
            if (!completed.IsSuccess) return completed;
            await RecordChainActionAsync(todo, instance, scope, actorUserId, "countersign.cancel", idempotencyKey,
                requestHash, request.Comment, completed.Value!.ActiveTodoId, now, token, completed.Value.StatusKey).ConfigureAwait(false);
            return completed;
        }
        var resultTodoId = todo.Id;
        if (chain.DirectionKey == "before")
        {
            var resumed = await ResumeOriginAsync(todo.Id, todo, instance, scope, now, token).ConfigureAwait(false);
            if (!resumed.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(resumed.Error!);
            resultTodoId = resumed.Value;
        }

        if (await commandExecutor.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision,
            WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                ("Revision", instance.Revision)), token).ConfigureAwait(false) != 1)
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        await RecordChainActionAsync(todo, instance, scope, actorUserId, "countersign.cancel", idempotencyKey,
            requestHash, request.Comment, resultTodoId, now, token).ConfigureAwait(false);

        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, "active",
            instance.Revision + 1, resultTodoId, instance.StartedAtUtc));
    }

    /// <summary>附属拒绝裁定原成员一席，不增加人数，也不把或签和分组直接改成全局否决。</summary>
    private async Task<Result<WorkflowInstanceResponse>> RejectCountersignItemAsync(WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance, Guid formVersionId, string patchedSubmission, ActWorkflowTodoRequest request,
        Guid actor, WorkflowManagementScope scope, WorkflowCountersignItemContextRecord item,
        WorkflowPreparedHumanActivities prepared, CancellationToken token)
    {
        var now = clock.UtcNow;
        if (await commandExecutor.ExecuteAsync(WorkflowSql.CompleteCountersignItem,
            WorkflowSqlParameters.Create(("Id", item.Id), ("ChainId", item.ChainId)), token).ConfigureAwait(false) != 1 ||
            await commandExecutor.ExecuteAsync(WorkflowSql.CompleteTodoWithRevision,
            WorkflowSqlParameters.Create(("Id", todo.Id), ("AssigneeUserId", actor), ("TenantScopeKey", scope.TenantScopeKey),
                ("CompletedAtUtc", now), ("ResultActionKey", "reject"), ("Revision", request.ExpectedRevision)), token).ConfigureAwait(false) != 1)
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        await commandExecutor.ExecuteAsync(WorkflowCountersignClosureSql.CancelChainTodos,
            WorkflowSqlParameters.Create(("ChainId", item.ChainId), ("InstanceId", instance.Id), ("StepId", todo.StepId),
                ("TenantScopeKey", scope.TenantScopeKey), ("Now", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.CancelPendingCountersignItems,
            WorkflowSqlParameters.Create(("ChainId", item.ChainId)), token).ConfigureAwait(false);
        if (await commandExecutor.ExecuteAsync(WorkflowSql.CompleteCountersignChain,
            WorkflowSqlParameters.Create(("Id", item.ChainId)), token).ConfigureAwait(false) != 1)
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        return await CompleteOriginDecisionAsync(todo, instance, formVersionId, patchedSubmission, scope,
            item.OriginTodoId, "reject", prepared, token).ConfigureAwait(false);
    }

    private async Task<Result<WorkflowInstanceResponse>> CompleteCountersignItemApproveAsync(
        WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        string patchedSubmission,
        ActWorkflowTodoRequest request,
        Guid actorUserId,
        WorkflowManagementScope scope,
        WorkflowCountersignItemContextRecord item,
        WorkflowPreparedHumanActivities preparedApprovals,
        CancellationToken token)
    {
        var now = clock.UtcNow;
        var itemCompleted = await commandExecutor.ExecuteAsync(
            WorkflowSql.CompleteCountersignItem,
            WorkflowSqlParameters.Create(("Id", item.Id), ("ChainId", item.ChainId)), token).ConfigureAwait(false);
        var todoCompleted = await commandExecutor.ExecuteAsync(
            WorkflowSql.CompleteTodoWithRevision,
            WorkflowSqlParameters.Create(("Id", todo.Id), ("AssigneeUserId", actorUserId),
                ("TenantScopeKey", scope.TenantScopeKey), ("CompletedAtUtc", now),
                ("ResultActionKey", "approve"), ("Revision", request.ExpectedRevision)), token).ConfigureAwait(false);
        if (itemCompleted != 1 || todoCompleted != 1)
        {
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var nextItem = await FindNextPendingItemAsync(item.ChainId, token).ConfigureAwait(false);
        if (nextItem is not null)
        {
            var nextTodoId = idGenerator.NewId();
            await InsertCountersignTodoAsync(
                nextTodoId, todo, instance, nextItem.AssigneeUserId, scope, now, token).ConfigureAwait(false);
            var activated = await commandExecutor.ExecuteAsync(
                WorkflowSql.ActivateCountersignItem,
                WorkflowSqlParameters.Create(("Id", nextItem.Id), ("ChainId", item.ChainId),
                    ("StatusKey", "active"), ("TodoId", nextTodoId)), token).ConfigureAwait(false);
            if (activated != 1)
            {
                return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            }

            if (await commandExecutor.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision,
                WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                    ("Revision", instance.Revision)), token).ConfigureAwait(false) != 1)
                return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            await notificationPublisher.PublishTodoAssignedAsync(
                instance.Id, nextTodoId, nextItem.AssigneeUserId, instance.BusinessType,
                instance.BusinessId, now, token).ConfigureAwait(false);
            return Result<WorkflowInstanceResponse>.Success(new(
                instance.Id, instance.DefinitionVersionId, formVersionId,
                instance.BusinessType, instance.BusinessId, instance.BusinessTitle, "active",
                instance.Revision + 1, nextTodoId, instance.StartedAtUtc));
        }

        if (await commandExecutor.ExecuteAsync(WorkflowSql.CompleteCountersignChain,
            WorkflowSqlParameters.Create(("Id", item.ChainId)), token).ConfigureAwait(false) != 1)
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);

        if (item.DirectionKey == "before")
        {
            var resumed = await ResumeOriginAsync(item.OriginTodoId, todo, instance, scope, now, token).ConfigureAwait(false);
            if (!resumed.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(resumed.Error!);
            if (await commandExecutor.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision,
                    WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                        ("Revision", instance.Revision)), token).ConfigureAwait(false) != 1)
                return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            return Result<WorkflowInstanceResponse>.Success(new(
                instance.Id, instance.DefinitionVersionId, formVersionId,
                instance.BusinessType, instance.BusinessId, instance.BusinessTitle, "active",
                instance.Revision + 1, resumed.Value, instance.StartedAtUtc));
        }

        return await CompleteOriginDecisionAsync(
            todo, instance, formVersionId, patchedSubmission, scope, item.OriginTodoId, "approve", preparedApprovals, token)
            .ConfigureAwait(false);
    }

    private async Task<Result<WorkflowInstanceResponse>> DeferOriginApproveForAfterChainAsync(
        WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        ActWorkflowTodoRequest request,
        Guid actorUserId,
        WorkflowManagementScope scope,
        WorkflowCountersignChainRecord chain,
        CancellationToken token)
    {
        var now = clock.UtcNow;
        var todoCompleted = await commandExecutor.ExecuteAsync(
            WorkflowSql.CompleteTodoWithRevision,
            WorkflowSqlParameters.Create(("Id", todo.Id), ("AssigneeUserId", actorUserId),
                ("TenantScopeKey", scope.TenantScopeKey), ("CompletedAtUtc", now),
                ("ResultActionKey", "approve"), ("Revision", request.ExpectedRevision)), token).ConfigureAwait(false);
        if (todoCompleted != 1)
        {
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var firstItem = await FindNextPendingItemAsync(chain.Id, token).ConfigureAwait(false);
        if (firstItem is null)
        {
            return InstanceFailure(WorkflowErrorCodes.TodoCountersignChainNotFound, ErrorType.BusinessRule);
        }

        var nextTodoId = idGenerator.NewId();
        await InsertCountersignTodoAsync(
            nextTodoId, todo, instance, firstItem.AssigneeUserId, scope, now, token).ConfigureAwait(false);
        if (await commandExecutor.ExecuteAsync(WorkflowSql.ActivateCountersignItem,
            WorkflowSqlParameters.Create(("Id", firstItem.Id), ("ChainId", chain.Id),
                ("StatusKey", "active"), ("TodoId", nextTodoId)), token).ConfigureAwait(false) != 1)
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        if (await commandExecutor.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision,
            WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                ("Revision", instance.Revision)), token).ConfigureAwait(false) != 1)
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        await notificationPublisher.PublishTodoAssignedAsync(
            instance.Id, nextTodoId, firstItem.AssigneeUserId, instance.BusinessType,
            instance.BusinessId, now, token).ConfigureAwait(false);
        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, "active",
            instance.Revision + 1, nextTodoId, instance.StartedAtUtc));
    }

    private async Task<Result<WorkflowInstanceResponse>> CompleteOriginDecisionAsync(
        WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        string patchedSubmission,
        WorkflowManagementScope scope,
        Guid originTodoId,
        string decision,
        WorkflowPreparedHumanActivities preparedApprovals,
        CancellationToken token)
    {
        var asset = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
            WorkflowSql.FindRuntimeAsset, WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var definition = asset is null ? null : JsonSerializer.Deserialize(asset.CanonicalJson,
            WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var schema = asset is null ? null : JsonSerializer.Deserialize(asset.FormSchemaJson,
            WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var values = JsonSerializer.Deserialize(patchedSubmission, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (definition is null || schema is null || values is null ||
            !WorkflowRuntimePlan.TryCreate(definition, schema, out var plan))
            return InstanceFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        var origin = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById,
            WorkflowSqlParameters.Create(("Id", originTodoId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (origin is null || origin.StepId != todo.StepId || origin.InstanceId != instance.Id ||
            !(origin.StatusKey == "completed" && (origin.ResultActionKey == "approve" || decision == "reject" && origin.ResultActionKey == "reject") ||
                decision == "reject" && origin.StatusKey == "before_countersign"))
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        var now = clock.UtcNow;
        if (origin.StatusKey == "before_countersign" && await commandExecutor.ExecuteAsync(WorkflowSql.CloseOriginTodoAfterBeforeCountersign,
            WorkflowSqlParameters.Create(("Id", origin.Id), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                ("CompletedAtUtc", now), ("Revision", origin.Revision)), token).ConfigureAwait(false) != 1)
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        // 后加签只完成原办理人的一席，实际签署人与原办理人各自保留动作证据。
        if (origin.ApprovalModeKey is not null)
        {
            var slot = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(WorkflowSql.FindApprovalSlotByTodo,
                WorkflowSqlParameters.Create(("TodoId", origin.Id), ("AssigneeUserId", origin.AssigneeUserId),
                    ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (slot is null || await commandExecutor.ExecuteAsync(WorkflowSql.DecideApprovalSlotWithRevision,
                WorkflowSqlParameters.Create(("Id", slot.Id), ("StepId", origin.StepId), ("TodoId", origin.Id),
                    ("AssigneeUserId", origin.AssigneeUserId), ("DecisionKey", decision), ("DecidedAtUtc", now),
                    ("Revision", slot.Revision)), token).ConfigureAwait(false) != 1)
                return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }
        var resolution = await stepResolution.ResolveAsync(origin, instance, scope, plan!, values, preparedApprovals, now, token).ConfigureAwait(false);
        if (!resolution.IsSuccess) return Result<WorkflowInstanceResponse>.Failure(resolution.Error!);
        var result = resolution.Value!;
        var status = result.InstanceStatusKey;
        if (await commandExecutor.ExecuteAsync(status switch
            {
                "active" => WorkflowSql.AdvanceInstanceWithRevision,
                "suspended" => WorkflowSql.SuspendInstanceWithRevision,
                _ => WorkflowSql.CompleteInstanceWithRevision,
            }, WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                ("Revision", instance.Revision), ("StatusKey", status), ("CompletedAtUtc", now)), token).ConfigureAwait(false) != 1)
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        if (status == "rejected")
        {
            await WorkflowHumanActivityLifecycle.RejectAsync(commandExecutor, instance.Id, scope.TenantScopeKey, now, token).ConfigureAwait(false);
            await notificationPublisher.PublishInstanceRejectedAsync(instance.Id, instance.StartedById,
                instance.BusinessType, instance.BusinessId, now, token).ConfigureAwait(false);
        }
        else if (status == "completed")
            await notificationPublisher.PublishInstanceCompletedAsync(instance.Id, instance.StartedById,
                instance.BusinessType, instance.BusinessId, now, token).ConfigureAwait(false);
        if (status is "completed" or "rejected")
            await commandExecutor.ExecuteAsync(WorkflowActivitySql.CancelWork,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey), ("Now", now)), token).ConfigureAwait(false);
        return Result<WorkflowInstanceResponse>.Success(new(instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, status, instance.Revision + 1,
            result.NextTodoId, instance.StartedAtUtc));
    }

    /// <summary>每次恢复都捕获新输入；旧 Todo 和回执保持不可变，避免附件历史借用未来表单。</summary>
    private async Task<Result<Guid>> ResumeOriginAsync(Guid originTodoId, WorkflowTodoRuntimeRecord sourceTodo,
        WorkflowInstanceRecord instance, WorkflowManagementScope scope, DateTimeOffset now, CancellationToken token)
    {
        var origin = originTodoId == sourceTodo.Id ? sourceTodo :
            await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById,
                WorkflowSqlParameters.Create(("Id", originTodoId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (origin is null || origin.Id != originTodoId || origin.StepId != sourceTodo.StepId ||
            origin.InstanceId != instance.Id || origin.StatusKey != "before_countersign")
            return Result<Guid>.Failure(new Error(WorkflowErrorCodes.RevisionConflict, "The original todo changed.", ErrorType.Conflict));
        var newTodoId = idGenerator.NewId();
        await InsertCountersignTodoAsync(newTodoId, origin, instance, origin.AssigneeUserId, scope, now, token).ConfigureAwait(false);
        if (await commandExecutor.ExecuteAsync(WorkflowSql.CloseOriginTodoAfterBeforeCountersign,
            WorkflowSqlParameters.Create(("Id", origin.Id), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                ("CompletedAtUtc", now), ("Revision", origin.Revision)), token).ConfigureAwait(false) != 1)
            return Result<Guid>.Failure(new Error(WorkflowErrorCodes.RevisionConflict, "The original todo changed.", ErrorType.Conflict));
        if (origin.ApprovalModeKey is not null)
        {
            var slot = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(WorkflowSql.FindApprovalSlotByTodo,
                WorkflowSqlParameters.Create(("TodoId", origin.Id), ("AssigneeUserId", origin.AssigneeUserId),
                    ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (slot is null || await commandExecutor.ExecuteAsync(WorkflowSql.RebindApprovalSlotForCountersignResume,
                WorkflowSqlParameters.Create(("Id", slot.Id), ("InstanceId", instance.Id), ("TodoId", origin.Id),
                    ("NewTodoId", newTodoId), ("Revision", slot.Revision), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) != 1)
                return Result<Guid>.Failure(new Error(WorkflowErrorCodes.RevisionConflict, "The approval slot changed.", ErrorType.Conflict));
        }
        await notificationPublisher.PublishTodoAssignedAsync(instance.Id, newTodoId, origin.AssigneeUserId,
            instance.BusinessType, instance.BusinessId, now, token).ConfigureAwait(false);
        return Result<Guid>.Success(newTodoId);
    }

    private async Task InsertCountersignTodoAsync(
        Guid todoId,
        WorkflowTodoRuntimeRecord sourceTodo,
        WorkflowInstanceRecord instance,
        Guid assigneeUserId,
        WorkflowManagementScope scope,
        DateTimeOffset now,
        CancellationToken token)
    {
        var asset = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
            WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        WorkflowTodoTimeoutPolicy? timeoutPolicy = null;
        if (asset is not null)
        {
            var definition = JsonSerializer.Deserialize(
                asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
            var formSchema = JsonSerializer.Deserialize(
                asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            if (definition is not null && formSchema is not null &&
                WorkflowRuntimePlan.TryCreate(definition, formSchema, out var plan))
            {
                plan!.TryGetHumanTimeoutPolicy(sourceTodo.NodeKey, out timeoutPolicy);
            }
        }

        var timeoutSchedule = WorkflowTodoTimeoutSchedule.Create(now, timeoutPolicy);
        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertTodo,
            WorkflowSqlParameters.Create(("Id", todoId), ("InstanceId", instance.Id),
                ("StepId", sourceTodo.StepId), ("AssigneeUserId", assigneeUserId),
                ("ArrivedAtUtc", now), ("DueAtUtc", timeoutSchedule.DueAtUtc),
                ("NextReminderAtUtc", timeoutSchedule.NextReminderAtUtc),
                ("EscalateAtUtc", timeoutSchedule.EscalateAtUtc),
                ("MaxReminderCount", timeoutSchedule.MaxReminderCount),
                ("ReminderIntervalMinutes", timeoutSchedule.ReminderIntervalMinutes),
                ("EscalationRecipientUserId", timeoutSchedule.EscalationRecipientUserId),
                ("NextTimeoutSignalAtUtc", timeoutSchedule.NextTimeoutSignalAtUtc)), token).ConfigureAwait(false);
        var submission = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        submission = await branchStates.ReadAsync(sourceTodo, submission, scope.TenantScopeKey, token).ConfigureAwait(false);
        if (submission is null || await commandExecutor.ExecuteAsync(WorkflowTodoViewSql.Capture,
            WorkflowSqlParameters.Create(("Id", todoId), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                ("InputJson", submission.SubmissionJson)), token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("workflow.todo_view.input_missing");
    }

    private async Task<WorkflowCountersignItemRecord?> FindNextPendingItemAsync(
        Guid chainId,
        CancellationToken token)
    {
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => WorkflowSql.FindNextPendingCountersignItem,
            DatabaseProvider.MySql => WorkflowSql.FindNextPendingCountersignItemMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };
        return await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignItemRecord>(
            statement, WorkflowSqlParameters.Create(("ChainId", chainId)), token).ConfigureAwait(false);
    }

    private async Task<WorkflowTodoCountersignChainResponse> MapChainAsync(
        WorkflowCountersignChainRecord chain,
        CancellationToken token)
    {
        var items = await queryExecutor.QueryAsync<WorkflowCountersignItemRecord>(
            WorkflowSql.ListCountersignItemsByChain,
            WorkflowSqlParameters.Create(("ChainId", chain.Id)), token).ConfigureAwait(false);
        return new WorkflowTodoCountersignChainResponse(
            chain.Id,
            chain.DirectionKey,
            chain.StatusKey,
            items.Select(item => new WorkflowTodoCountersignItemResponse(
                item.Id, item.SequenceNo, item.AssigneeUserId, item.StatusKey, item.TodoId)).ToArray());
    }

    private async Task<WorkflowTodoRuntimeRecord?> FindOwnedTodoAsync(
        Guid todoId,
        Guid actorUserId,
        WorkflowManagementScope scope,
        CancellationToken token)
    {
        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(
            WorkflowSql.FindTodoById,
            WorkflowSqlParameters.Create(("Id", todoId), ("TenantScopeKey", scope.TenantScopeKey)),
            token).ConfigureAwait(false);
        return todo is null || todo.AssigneeUserId != actorUserId ? null : todo;
    }

    private async Task<bool> ValidateAssigneesAsync(
        IReadOnlyList<Guid> assignees,
        WorkflowManagementScope scope,
        CancellationToken token)
    {
        if (scope.ScopeKey == "host")
        {
            var users = await hostUserDirectory.FindActiveHostUsersAsync(assignees, token)
                .ConfigureAwait(false);
            return users.Count == assignees.Count;
        }

        var tenantUsers = await tenantUserDirectory.FindActiveTenantMembersAsync(assignees, token)
            .ConfigureAwait(false);
        return tenantUsers.Count == assignees.Count;
    }

    // 链配置/取消与办理动作采用相同的持久回执和 B0 审计约束，后续终态不能污染首次响应。
    private async Task RecordChainActionAsync(WorkflowTodoRuntimeRecord todo, WorkflowInstanceRecord instance,
        WorkflowManagementScope scope, Guid actorId, string actionKey, string idempotencyKey, string requestHash,
        string? comment, Guid? resultTodoId, DateTimeOffset now, CancellationToken token, string statusKey = "active")
    {
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertApprovalActionRecord,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instance.Id), ("StepId", todo.StepId),
                ("TodoId", todo.Id), ("ActionKey", actionKey), ("ActorUserId", actorId),
                ("InstanceRevision", instance.Revision + 1), ("IdempotencyKey", idempotencyKey),
                ("CommentSummary", comment?.Trim()), ("CreatedAtUtc", now), ("ResultStatusKey", statusKey),
                ("ResultTodoId", resultTodoId)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                ("Id", idGenerator.NewId()), ("InstanceId", instance.Id), ("StepId", todo.StepId),
                ("TransitionKey", $"todo.{actionKey}"), ("FromStatusKey", "active"), ("ToStatusKey", statusKey),
                ("IdempotencyKey", idempotencyKey), ("Summary", requestHash), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            WorkflowSqlParameters.Create(("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                ("Id", idGenerator.NewId()), ("TenantId", scope.TenantId), ("ScopeKey", scope.ScopeKey),
                ("InstanceId", instance.Id), ("OperationKey", $"todo.{actionKey}"), ("ActorUserId", actorId),
                ("ResourceTypeKey", "todo"), ("ResourceId", todo.Id), ("OutcomeKey", "succeeded"),
                ("DetailJson", null), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
    }

    private Task<WorkflowActionReceiptRecord?> FindReceiptAsync(
        Guid instanceId,
        string idempotencyKey,
        CancellationToken token) =>
        queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
            WorkflowSql.FindActionReceipt,
            WorkflowSqlParameters.Create(("InstanceId", instanceId),
                ("IdempotencyKey", idempotencyKey)), token);

    private static Result<WorkflowInstanceResponse> ReplayCountersign(
        Guid todoId,
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        Guid actorUserId,
        string actionKey,
        string requestHash,
        WorkflowActionReceiptRecord receipt)
    {
        if (receipt.TodoId != todoId || receipt.ActionKey != actionKey ||
            receipt.ActorUserId != actorUserId ||
            receipt.RequestHash != requestHash || receipt.ResultStatusKey is not ("active" or "suspended" or "completed" or "rejected" or "cancelled"))
        {
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.BusinessTitle, receipt.ResultStatusKey,
            receipt.InstanceRevision, receipt.ResultTodoId, instance.StartedAtUtc));
    }

    private static string HashCountersignRequest(
        string direction,
        IReadOnlyList<Guid> assignees,
        long expectedRevision,
        string? comment) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"countersign\n{direction}\n{expectedRevision}\n{comment?.Trim()}\n" +
            string.Join(',', assignees.Select(id => id.ToString("D"))))));

    private static Result<WorkflowTodoCountersignChainResponse> ChainFailure(string code, ErrorType type) =>
        Result<WorkflowTodoCountersignChainResponse>.Failure(
            new Error(code, "The workflow countersign chain operation failed.", type));

    private static Result<WorkflowInstanceResponse> InstanceFailure(string code, ErrorType type) =>
        Result<WorkflowInstanceResponse>.Failure(
            new Error(code, "The workflow countersign operation failed.", type));
}
