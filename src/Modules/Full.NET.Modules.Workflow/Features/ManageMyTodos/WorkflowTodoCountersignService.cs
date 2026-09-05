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
    ITenantUserSelectionDirectory tenantUserDirectory,
    WorkflowNotificationOutboxPublisher notificationPublisher,
    WorkflowAutomaticTransitionWriter automaticTransitionWriter)
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
    public Task<Result<WorkflowInstanceResponse>> CountersignAsync(
        Guid todoId,
        Guid actorUserId,
        CountersignWorkflowTodoRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => CountersignCoreAsync(todoId, actorUserId, request, token),
            cancellationToken);

    /// <summary>由原办理人取消尚未完成的活动加签链。</summary>
    public Task<Result<WorkflowInstanceResponse>> CancelAsync(
        Guid todoId,
        Guid actorUserId,
        CancelWorkflowTodoCountersignRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => CancelCoreAsync(todoId, actorUserId, request, token),
            cancellationToken);

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
        CancellationToken token)
    {
        if (actionKey != "approve")
        {
            return null;
        }

        var item = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(
            WorkflowSql.FindCountersignItemByTodoId,
            WorkflowSqlParameters.Create(("TodoId", todo.Id),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (item is not null)
        {
            return await CompleteCountersignItemApproveAsync(
                todo, instance, formVersionId, patchedSubmission, request, actorUserId, scope, item, token)
                .ConfigureAwait(false);
        }

        var afterChain = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignChainRecord>(
            WorkflowSql.FindActiveCountersignChainByOriginTodo,
            WorkflowSqlParameters.Create(("OriginTodoId", todo.Id),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (afterChain is not null && afterChain.DirectionKey == "after")
        {
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
        if (!await ValidateAssigneesAsync(assignees, scope, token).ConfigureAwait(false))
        {
            return InstanceFailure(WorkflowErrorCodes.TodoCountersignAssigneeInvalid, ErrorType.Validation);
        }

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
            return ReplayCountersign(instance, formVersionId, actorUserId, requestHash, receipt);
        }

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
                firstTodoId!.Value, instance.Id, todo.StepId, assignees[0], todo.NodeKey,
                instance.DefinitionVersionId, scope, now, token).ConfigureAwait(false);
            await notificationPublisher.PublishTodoAssignedAsync(
                instance.Id, firstTodoId.Value, assignees[0], instance.BusinessType,
                instance.BusinessId, now, token).ConfigureAwait(false);
        }

        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertActionRecord,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instance.Id),
                ("StepId", todo.StepId), ("TodoId", todo.Id),
                ("ActionKey", $"countersign.{direction}"),
                ("ActorUserId", actorUserId), ("InstanceRevision", instance.Revision + 1),
                ("IdempotencyKey", idempotencyKey),
                ("CommentSummary", request.Comment?.Trim()), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instance.Id),
                ("StepId", todo.StepId), ("TransitionKey", $"todo.countersign.{direction}"),
                ("FromStatusKey", "active"), ("ToStatusKey", "active"),
                ("IdempotencyKey", idempotencyKey), ("Summary", requestHash),
                ("CreatedAtUtc", now)), token).ConfigureAwait(false);

        var activeTodoId = direction == "before" ? firstTodoId : todo.Id;
        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, "active",
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

        var chain = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowCountersignChainRecord>(
            WorkflowSql.FindActiveCountersignChainByOriginTodo,
            WorkflowSqlParameters.Create(("OriginTodoId", todo.Id),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (chain is null)
        {
            return InstanceFailure(WorkflowErrorCodes.TodoCountersignChainNotFound, ErrorType.NotFound);
        }

        if ((chain.DirectionKey == "before" && todo.StatusKey != "awaiting_before_countersign") ||
            (chain.DirectionKey == "after" && (todo.StatusKey != "active" || todo.Revision != request.ExpectedRevision)))
        {
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var now = clock.UtcNow;
        var items = await queryExecutor.QueryAsync<WorkflowCountersignItemRecord>(
            WorkflowSql.ListCountersignItemsByChain,
            WorkflowSqlParameters.Create(("ChainId", chain.Id)), token).ConfigureAwait(false);
        foreach (var item in items.Where(item => item.TodoId is not null && item.StatusKey == "active"))
        {
            var activeTodo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindTodoById,
                WorkflowSqlParameters.Create(("Id", item.TodoId), ("TenantScopeKey", scope.TenantScopeKey)),
                token).ConfigureAwait(false);
            if (activeTodo is null)
            {
                continue;
            }

            await commandExecutor.ExecuteAsync(
                WorkflowSql.CancelTodoWithRevision,
                WorkflowSqlParameters.Create(("Id", activeTodo.Id), ("InstanceId", instance.Id),
                    ("TenantScopeKey", scope.TenantScopeKey), ("CompletedAtUtc", now),
                    ("Revision", activeTodo.Revision)), token).ConfigureAwait(false);
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

        if (chain.DirectionKey == "before")
        {
            await commandExecutor.ExecuteAsync(
                WorkflowSql.ReactivateOriginTodoAfterBeforeCountersign,
                WorkflowSqlParameters.Create(("Id", todo.Id),
                    ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        }

        await commandExecutor.ExecuteAsync(
            WorkflowSql.AdvanceInstanceWithRevision,
            WorkflowSqlParameters.Create(("Id", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey), ("Revision", instance.Revision)),
            token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertActionRecord,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instance.Id),
                ("StepId", todo.StepId), ("TodoId", todo.Id), ("ActionKey", "countersign.cancel"),
                ("ActorUserId", actorUserId), ("InstanceRevision", instance.Revision + 1),
                ("IdempotencyKey", idempotencyKey),
                ("CommentSummary", request.Comment?.Trim()), ("CreatedAtUtc", now)), token).ConfigureAwait(false);

        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, "active",
            instance.Revision + 1, todo.Id, instance.StartedAtUtc));
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
            var activated = await commandExecutor.ExecuteAsync(
                WorkflowSql.ActivateCountersignItem,
                WorkflowSqlParameters.Create(("Id", nextItem.Id), ("ChainId", item.ChainId),
                    ("StatusKey", "active"), ("TodoId", nextTodoId)), token).ConfigureAwait(false);
            if (activated != 1)
            {
                return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            }

            await InsertCountersignTodoAsync(
                nextTodoId, instance.Id, item.StepId, nextItem.AssigneeUserId, todo.NodeKey,
                instance.DefinitionVersionId, scope, now, token).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(
                WorkflowSql.AdvanceInstanceWithRevision,
                WorkflowSqlParameters.Create(("Id", instance.Id),
                    ("TenantScopeKey", scope.TenantScopeKey), ("Revision", instance.Revision)),
                token).ConfigureAwait(false);
            await notificationPublisher.PublishTodoAssignedAsync(
                instance.Id, nextTodoId, nextItem.AssigneeUserId, instance.BusinessType,
                instance.BusinessId, now, token).ConfigureAwait(false);
            return Result<WorkflowInstanceResponse>.Success(new(
                instance.Id, instance.DefinitionVersionId, formVersionId,
                instance.BusinessType, instance.BusinessId, "active",
                instance.Revision + 1, nextTodoId, instance.StartedAtUtc));
        }

        await commandExecutor.ExecuteAsync(
            WorkflowSql.CompleteCountersignChain,
            WorkflowSqlParameters.Create(("Id", item.ChainId)), token).ConfigureAwait(false);

        if (item.DirectionKey == "before")
        {
            await commandExecutor.ExecuteAsync(
                WorkflowSql.ReactivateOriginTodoAfterBeforeCountersign,
                WorkflowSqlParameters.Create(("Id", item.OriginTodoId),
                    ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(
                WorkflowSql.AdvanceInstanceWithRevision,
                WorkflowSqlParameters.Create(("Id", instance.Id),
                    ("TenantScopeKey", scope.TenantScopeKey), ("Revision", instance.Revision)),
                token).ConfigureAwait(false);
            return Result<WorkflowInstanceResponse>.Success(new(
                instance.Id, instance.DefinitionVersionId, formVersionId,
                instance.BusinessType, instance.BusinessId, "active",
                instance.Revision + 1, item.OriginTodoId, instance.StartedAtUtc));
        }

        return await CompleteAfterChainAsync(
            todo, instance, formVersionId, patchedSubmission, request, actorUserId, scope, token)
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
        await commandExecutor.ExecuteAsync(
            WorkflowSql.ActivateCountersignItem,
            WorkflowSqlParameters.Create(("Id", firstItem.Id), ("ChainId", chain.Id),
                ("StatusKey", "active"), ("TodoId", nextTodoId)), token).ConfigureAwait(false);
        await InsertCountersignTodoAsync(
            nextTodoId, instance.Id, todo.StepId, firstItem.AssigneeUserId, todo.NodeKey,
            instance.DefinitionVersionId, scope, now, token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
            WorkflowSql.AdvanceInstanceWithRevision,
            WorkflowSqlParameters.Create(("Id", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey), ("Revision", instance.Revision)),
            token).ConfigureAwait(false);
        await notificationPublisher.PublishTodoAssignedAsync(
            instance.Id, nextTodoId, firstItem.AssigneeUserId, instance.BusinessType,
            instance.BusinessId, now, token).ConfigureAwait(false);
        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, "active",
            instance.Revision + 1, nextTodoId, instance.StartedAtUtc));
    }

    private async Task<Result<WorkflowInstanceResponse>> CompleteAfterChainAsync(
        WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        string patchedSubmission,
        ActWorkflowTodoRequest request,
        Guid actorUserId,
        WorkflowManagementScope scope,
        CancellationToken token)
    {
        var asset = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
            WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var definition = asset is null
            ? null
            : JsonSerializer.Deserialize(
                asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var formSchema = asset is null
            ? null
            : JsonSerializer.Deserialize(
                asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        WorkflowRuntimePlan? runtimePlan = null;
        var transition = default(WorkflowApprovalTransition);
        if (definition is null || formSchema is null ||
            !WorkflowRuntimePlan.TryCreate(definition, formSchema, out runtimePlan) ||
            !runtimePlan!.TryResolveApproval(todo.NodeKey,
                JsonSerializer.Deserialize(
                    patchedSubmission,
                    WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement) ?? [],
                out transition))
        {
            return InstanceFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        }

        var now = clock.UtcNow;
        var advancesToNextApproval = !transition.CompletesInstance;
        var instanceStatus = advancesToNextApproval ? "active" : "completed";
        var nextStepId = advancesToNextApproval ? idGenerator.NewId() : (Guid?)null;
        var nextTodoId = advancesToNextApproval ? idGenerator.NewId() : (Guid?)null;
        var stepUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.CompleteStepWithRevision,
            WorkflowSqlParameters.Create(("Id", todo.StepId), ("InstanceId", instance.Id),
                ("StatusKey", "completed"), ("CompletedAtUtc", now),
                ("Revision", todo.StepRevision)), token).ConfigureAwait(false);
        var instanceStatement = advancesToNextApproval
            ? WorkflowSql.AdvanceInstanceWithRevision
            : WorkflowSql.CompleteInstanceWithRevision;
        var instanceParameters = advancesToNextApproval
            ? WorkflowSqlParameters.Create(("Id", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey), ("Revision", instance.Revision))
            : WorkflowSqlParameters.Create(("Id", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey), ("StatusKey", instanceStatus),
                ("CompletedAtUtc", now), ("Revision", instance.Revision));
        var instanceUpdated = await commandExecutor.ExecuteAsync(
            instanceStatement, instanceParameters, token).ConfigureAwait(false);
        if (stepUpdated != 1 || instanceUpdated != 1)
        {
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var nextExecutionSequence = await queryExecutor.QuerySingleOrDefaultAsync<long>(
            WorkflowSql.FindNextStepExecutionSequence,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id)), token).ConfigureAwait(false);
        var approvalExecutionSequence = await automaticTransitionWriter.WriteAsync(
            instance.Id, scope.TenantScopeKey, transition.AutomaticNodes,
            nextExecutionSequence, now, token).ConfigureAwait(false);
        if (advancesToNextApproval)
        {
            var timeoutSchedule = WorkflowTodoTimeoutSchedule.Create(now, transition.TimeoutPolicy);
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertStep,
                WorkflowSqlParameters.Create(("Id", nextStepId), ("InstanceId", instance.Id),
                    ("NodeKey", transition.NextApprovalNodeKey), ("AssignedUserId", actorUserId),
                    ("ExecutionSequence", approvalExecutionSequence), ("StartedAtUtc", now)),
                token).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertTodo,
                WorkflowSqlParameters.Create(("Id", nextTodoId), ("InstanceId", instance.Id),
                    ("StepId", nextStepId), ("AssigneeUserId", actorUserId),
                    ("ArrivedAtUtc", now), ("DueAtUtc", timeoutSchedule.DueAtUtc),
                    ("NextReminderAtUtc", timeoutSchedule.NextReminderAtUtc),
                    ("EscalateAtUtc", timeoutSchedule.EscalateAtUtc),
                    ("MaxReminderCount", timeoutSchedule.MaxReminderCount),
                    ("ReminderIntervalMinutes", timeoutSchedule.ReminderIntervalMinutes),
                    ("EscalationRecipientUserId", timeoutSchedule.EscalationRecipientUserId),
                    ("NextTimeoutSignalAtUtc", timeoutSchedule.NextTimeoutSignalAtUtc)), token).ConfigureAwait(false);
            await notificationPublisher.PublishTodoAssignedAsync(
                instance.Id, nextTodoId!.Value, actorUserId, instance.BusinessType,
                instance.BusinessId, now, token).ConfigureAwait(false);
        }
        else
        {
            await notificationPublisher.PublishInstanceCompletedAsync(
                instance.Id, instance.StartedById, instance.BusinessType,
                instance.BusinessId, now, token).ConfigureAwait(false);
        }

        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instanceStatus,
            instance.Revision + 1, nextTodoId, instance.StartedAtUtc));
    }

    private async Task InsertCountersignTodoAsync(
        Guid todoId,
        Guid instanceId,
        Guid stepId,
        Guid assigneeUserId,
        string nodeKey,
        Guid definitionVersionId,
        WorkflowManagementScope scope,
        DateTimeOffset now,
        CancellationToken token)
    {
        var asset = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
            WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", definitionVersionId),
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
                plan!.TryGetApprovalTimeoutPolicy(nodeKey, out timeoutPolicy);
            }
        }

        var timeoutSchedule = WorkflowTodoTimeoutSchedule.Create(now, timeoutPolicy);
        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertTodo,
            WorkflowSqlParameters.Create(("Id", todoId), ("InstanceId", instanceId),
                ("StepId", stepId), ("AssigneeUserId", assigneeUserId),
                ("ArrivedAtUtc", now), ("DueAtUtc", timeoutSchedule.DueAtUtc),
                ("NextReminderAtUtc", timeoutSchedule.NextReminderAtUtc),
                ("EscalateAtUtc", timeoutSchedule.EscalateAtUtc),
                ("MaxReminderCount", timeoutSchedule.MaxReminderCount),
                ("ReminderIntervalMinutes", timeoutSchedule.ReminderIntervalMinutes),
                ("EscalationRecipientUserId", timeoutSchedule.EscalationRecipientUserId),
                ("NextTimeoutSignalAtUtc", timeoutSchedule.NextTimeoutSignalAtUtc)), token).ConfigureAwait(false);
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

        var tenantUsers = await tenantUserDirectory.FindActiveTenantUsersAsync(assignees, token)
            .ConfigureAwait(false);
        return tenantUsers.Count == assignees.Count;
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
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        Guid actorUserId,
        string requestHash,
        WorkflowActionReceiptRecord receipt)
    {
        if (receipt.ActionKey is not ("countersign.before" or "countersign.after") ||
            receipt.ActorUserId != actorUserId ||
            receipt.RequestHash != requestHash)
        {
            return InstanceFailure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.StatusKey,
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
