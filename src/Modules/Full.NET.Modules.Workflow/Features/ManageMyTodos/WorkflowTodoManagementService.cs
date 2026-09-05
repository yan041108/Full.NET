using Full.NET.Abstractions.Results;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>只读取可信作用域内当前认证用户的有界活动待办。</summary>
/// <param name="queryExecutor">受控查询执行器。</param>
/// <param name="commandExecutor">显式 SQL 命令执行器。</param>
/// <param name="transaction">Workflow 本地事务边界。</param>
/// <param name="currentTenant">可信当前租户上下文。</param>
/// <param name="clock">统一 UTC 时钟。</param>
/// <param name="idGenerator">UUID v7 标识生成器。</param>
/// <param name="databaseOptions">数据库提供程序配置。</param>
/// <param name="automaticTransitionWriter">自动节点迁移写入器。</param>
/// <param name="notificationPublisher">工作流提醒事务 Outbox 发布器。</param>
internal sealed class WorkflowTodoManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions,
    WorkflowAutomaticTransitionWriter automaticTransitionWriter,
    WorkflowNotificationOutboxPublisher notificationPublisher)
{
    public async Task<Result<IReadOnlyList<WorkflowTodoResponse>>> ListMineAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => WorkflowSql.ListMineSqlServer,
            DatabaseProvider.MySql => WorkflowSql.ListMineMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };
        var rows = await queryExecutor.QueryAsync<WorkflowTodoRecord>(
            statement,
            WorkflowSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey),
                ("AssigneeUserId", actorUserId), ("Take", 100)), cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<WorkflowTodoResponse>>.Success(rows.Select(Map).ToArray());
    }

    public Task<Result<WorkflowInstanceResponse>> ApproveAsync(
        Guid todoId,
        Guid actorUserId,
        ActWorkflowTodoRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => ActAsync(todoId, actorUserId, request, "approve", token),
            cancellationToken);

    public async Task<Result<WorkflowTodoDetailResponse>> GetAsync(
        Guid todoId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(
            WorkflowSql.FindTodoById,
            WorkflowSqlParameters.Create(("Id", todoId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        if (todo is null)
        {
            return DetailFailure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        }

        if (todo.AssigneeUserId != actorUserId)
        {
            return DetailFailure(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        }

        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", todo.InstanceId),
                ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        var asset = instance is null
            ? null
            : await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
                WorkflowSql.FindRuntimeAsset,
                WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId),
                    ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        var submission = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(
            WorkflowSql.FindFormSubmissionByInstance,
            WorkflowSqlParameters.Create(("InstanceId", todo.InstanceId),
                ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        if (asset is null || submission is null)
        {
            return DetailFailure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        var schema = JsonSerializer.Deserialize(
            asset.FormSchemaJson,
            WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var values = JsonSerializer.Deserialize(
            submission.SubmissionJson,
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (schema is null || values is null ||
            !WorkflowNodeFieldPolicy.TryResolve(asset.CanonicalJson, todo.NodeKey, schema, out var policy))
        {
            return DetailFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        }

        var view = policy!.CreateView(schema, values);
        var visibleSchema = JsonSerializer.SerializeToElement(
            view.Schema,
            WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var visibleSubmission = JsonSerializer.SerializeToElement(
            view.Values,
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        return Result<WorkflowTodoDetailResponse>.Success(new(
            todo.Id, todo.InstanceId, todo.StepId, todo.AssigneeUserId,
            todo.StatusKey, todo.Revision, asset.FormVersionId,
            visibleSchema, visibleSubmission, view.FieldPolicies,
            submission.Revision));
    }

    public async Task<Result<WorkflowTodoRuntimeResponse>> GetRuntimeAsync(
        Guid todoId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var result = await GetAsync(todoId, actorUserId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Result<WorkflowTodoRuntimeResponse>.Failure(result.Error!);
        }

        var detail = result.Value!;
        return Result<WorkflowTodoRuntimeResponse>.Success(new(
            detail.Id, detail.InstanceId, detail.StepId, detail.AssigneeUserId,
            detail.StatusKey, detail.Revision, detail.FormVersionId,
            HashUtf8(detail.FormSchema.GetRawText()), detail.FormSchema,
            detail.Submission, detail.FieldPolicies, detail.SubmissionRevision));
    }

    public Task<Result<WorkflowInstanceResponse>> RejectAsync(
        Guid todoId,
        Guid actorUserId,
        ActWorkflowTodoRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => ActAsync(todoId, actorUserId, request, "reject", token),
            cancellationToken);

    /// <summary>执行待办动作，并在批准迁移时同步写入沿途抄送。</summary>
    /// <param name="todoId">待办标识。</param>
    /// <param name="actorUserId">执行动作的当前用户标识。</param>
    /// <param name="request">包含并发版本、幂等键和字段补丁的动作请求。</param>
    /// <param name="actionKey">稳定动作码，只允许批准或驳回。</param>
    /// <param name="token">取消当前异步操作的令牌。</param>
    /// <returns>迁移后的实例状态或稳定业务错误。</returns>
    private async Task<Result<WorkflowInstanceResponse>> ActAsync(
        Guid todoId,
        Guid actorUserId,
        ActWorkflowTodoRequest request,
        string actionKey,
        CancellationToken token)
    {
        if (request.ExpectedRevision < 1 || request.FieldPatch.ValueKind != System.Text.Json.JsonValueKind.Object ||
            request.IdempotencyKey.Trim() is not { Length: >= 1 and <= 128 } ||
            request.Comment is { Length: > 512 })
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(
            WorkflowSql.FindTodoById,
            WorkflowSqlParameters.Create(("Id", todoId), ("TenantScopeKey", scope.TenantScopeKey)),
            token).ConfigureAwait(false);
        if (todo is null)
        {
            return Failure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        }

        if (todo.AssigneeUserId != actorUserId)
        {
            return Failure(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        }

        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", todo.InstanceId),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersionId)
        {
            return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        var requestHash = HashRequest(actionKey, request);
        var receipt = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
            WorkflowSql.FindActionReceipt,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                ("IdempotencyKey", request.IdempotencyKey.Trim())), token).ConfigureAwait(false);
        if (receipt is not null)
        {
            if (receipt.ActionKey != actionKey || receipt.ActorUserId != actorUserId ||
                receipt.RequestHash != requestHash)
            {
                return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            }

            var replayTodo = instance.StatusKey == "active"
                ? await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                    WorkflowSql.FindActiveTodoByInstance,
                    WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                        ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false)
                : null;
            return Result<WorkflowInstanceResponse>.Success(Map(instance, formVersionId, replayTodo?.Id));
        }

        if (todo.StatusKey != "active" || todo.Revision != request.ExpectedRevision)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        if (instance.StatusKey == "suspended")
        {
            return Failure(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        }

        if (instance.StatusKey != "active")
        {
            return Failure(WorkflowErrorCodes.InstanceTerminal, ErrorType.Conflict);
        }

        var asset = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
            WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var submission = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(
            WorkflowSql.FindFormSubmissionByInstance,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var definition = asset is null
            ? null
            : JsonSerializer.Deserialize(
                asset.CanonicalJson,
                WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var formSchema = asset is null
            ? null
            : JsonSerializer.Deserialize(
                asset.FormSchemaJson,
                WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        WorkflowRuntimePlan? runtimePlan = null;
        var transition = default(WorkflowApprovalTransition);
        var hasPlan = definition is not null && formSchema is not null &&
            WorkflowRuntimePlan.TryCreate(definition, formSchema, out runtimePlan) &&
            runtimePlan!.ContainsApprovalNode(todo.NodeKey);
        var patchedSubmission = asset is null || submission is null || !hasPlan
            ? null
            : BuildPatchedSubmission(
                asset.CanonicalJson,
                todo.NodeKey,
                asset.FormSchemaJson,
                submission.SubmissionJson,
                request.FieldPatch);
        if (patchedSubmission is null)
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
        }

        if (actionKey == "approve")
        {
            var patchedValues = JsonSerializer.Deserialize(
                patchedSubmission,
                WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
            if (patchedValues is null ||
                !runtimePlan!.TryResolveApproval(todo.NodeKey, patchedValues, out transition))
            {
                return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.BusinessRule);
            }
        }
        else
        {
            // 驳回直接终止当前实例，不得执行或求值审批节点之后的任何自动节点。
            transition = new WorkflowApprovalTransition(null, true, []);
        }

        var now = clock.UtcNow;
        var advancesToNextApproval = actionKey == "approve" && !transition.CompletesInstance;
        var instanceStatus = actionKey == "reject"
            ? "rejected"
            : advancesToNextApproval ? "active" : "completed";
        var stepStatus = actionKey == "reject" ? "rejected" : "completed";
        var nextStepId = advancesToNextApproval ? idGenerator.NewId() : (Guid?)null;
        var nextTodoId = advancesToNextApproval ? idGenerator.NewId() : (Guid?)null;
        var submissionUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.UpdateFormSubmissionWithRevision,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("FormVersionId", formVersionId),
                ("SubmissionJson", patchedSubmission), ("UpdatedById", actorUserId),
                ("UpdatedAtUtc", now), ("Revision", submission!.Revision)), token).ConfigureAwait(false);
        var todoUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.CompleteTodoWithRevision,
            WorkflowSqlParameters.Create(("Id", todo.Id), ("AssigneeUserId", actorUserId),
                ("TenantScopeKey", scope.TenantScopeKey), ("CompletedAtUtc", now),
                ("ResultActionKey", actionKey), ("Revision", request.ExpectedRevision)), token).ConfigureAwait(false);
        var stepUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.CompleteStepWithRevision,
            WorkflowSqlParameters.Create(("Id", todo.StepId), ("InstanceId", instance.Id),
                ("StatusKey", stepStatus), ("CompletedAtUtc", now), ("Revision", 1L)), token).ConfigureAwait(false);
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
        if (submissionUpdated != 1 || todoUpdated != 1 || stepUpdated != 1 || instanceUpdated != 1)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        if (advancesToNextApproval)
        {
            var timeoutSchedule = WorkflowTodoTimeoutSchedule.Create(now, transition.TimeoutPolicy);
            // 审批人解析器尚未开放前沿用当前办理人，避免从设计器的非权威展示字段推导身份。
            await commandExecutor.ExecuteAsync(WorkflowSql.InsertStep,
                WorkflowSqlParameters.Create(("Id", nextStepId), ("InstanceId", instance.Id),
                    ("NodeKey", transition.NextApprovalNodeKey), ("AssignedUserId", actorUserId),
                    ("StartedAtUtc", now)), token).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(WorkflowSql.InsertTodo,
                WorkflowSqlParameters.Create(("Id", nextTodoId), ("InstanceId", instance.Id),
                    ("StepId", nextStepId), ("AssigneeUserId", actorUserId),
                    ("ArrivedAtUtc", now), ("DueAtUtc", timeoutSchedule.DueAtUtc),
                    ("NextReminderAtUtc", timeoutSchedule.NextReminderAtUtc),
                    ("EscalateAtUtc", timeoutSchedule.EscalateAtUtc),
                    ("MaxReminderCount", timeoutSchedule.MaxReminderCount),
                    ("ReminderIntervalMinutes", timeoutSchedule.ReminderIntervalMinutes),
                    ("EscalationRecipientUserId", timeoutSchedule.EscalationRecipientUserId),
                    ("NextTimeoutSignalAtUtc", timeoutSchedule.NextTimeoutSignalAtUtc)), token).ConfigureAwait(false);
        }

        await commandExecutor.ExecuteAsync(WorkflowSql.InsertActionRecord,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instance.Id),
                ("StepId", todo.StepId), ("TodoId", todo.Id), ("ActionKey", actionKey),
                ("ActorUserId", actorUserId), ("InstanceRevision", instance.Revision + 1),
                ("IdempotencyKey", request.IdempotencyKey.Trim()),
                ("CommentSummary", request.Comment?.Trim()), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instance.Id),
                ("StepId", todo.StepId), ("TransitionKey", $"todo.{actionKey}"),
                ("FromStatusKey", "active"), ("ToStatusKey", instanceStatus),
                ("IdempotencyKey", request.IdempotencyKey.Trim()),
                ("Summary", requestHash), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        if (actionKey == "approve")
        {
            await automaticTransitionWriter.WriteAsync(
                instance.Id,
                scope.TenantScopeKey,
                transition.AutomaticNodes,
                now,
                token).ConfigureAwait(false);
        }
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("TenantId", scope.TenantId),
                ("ScopeKey", scope.ScopeKey), ("InstanceId", instance.Id),
                ("OperationKey", $"todo.{actionKey}"), ("ActorUserId", actorUserId),
                ("ResourceTypeKey", "todo"), ("ResourceId", todo.Id),
                ("OutcomeKey", "succeeded"), ("DetailJson", null),
                ("CreatedAtUtc", now)), token).ConfigureAwait(false);

        // 只在首次成功迁移后发布提醒；上方幂等回放分支不会再次追加 Outbox。
        if (advancesToNextApproval)
        {
            await notificationPublisher.PublishTodoAssignedAsync(
                instance.Id,
                nextTodoId!.Value,
                actorUserId,
                instance.BusinessType,
                instance.BusinessId,
                now,
                token).ConfigureAwait(false);
        }
        else if (actionKey == "reject")
        {
            await notificationPublisher.PublishInstanceRejectedAsync(
                instance.Id,
                instance.StartedById,
                instance.BusinessType,
                instance.BusinessId,
                now,
                token).ConfigureAwait(false);
        }
        else
        {
            await notificationPublisher.PublishInstanceCompletedAsync(
                instance.Id,
                instance.StartedById,
                instance.BusinessType,
                instance.BusinessId,
                now,
                token).ConfigureAwait(false);
        }

        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instanceStatus,
            instance.Revision + 1, nextTodoId, instance.StartedAtUtc));
    }

    private static string? BuildPatchedSubmission(
        string canonicalDefinitionJson,
        string nodeKey,
        string formSchemaJson,
        string submissionJson,
        JsonElement patch)
    {
        var schema = JsonSerializer.Deserialize(
            formSchemaJson,
            WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var values = JsonSerializer.Deserialize(
            submissionJson,
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (schema is null || values is null)
        {
            return null;
        }

        if (!WorkflowNodeFieldPolicy.TryResolve(
                canonicalDefinitionJson, nodeKey, schema, out var policy) ||
            !policy!.TryApplyPatch(schema, values, patch, out var patched))
        {
            return null;
        }

        return JsonSerializer.Serialize(
            patched,
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
    }

    private static string HashRequest(string actionKey, ActWorkflowTodoRequest request)
    {
        var value = $"{actionKey}\n{request.ExpectedRevision}\n{request.FieldPatch.GetRawText()}\n{request.Comment?.Trim()}";
        return HashUtf8(value);
    }

    private static string HashUtf8(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static WorkflowInstanceResponse Map(
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        Guid? activeTodoId) =>
        new(instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.StatusKey,
            instance.Revision, activeTodoId, instance.StartedAtUtc);

    private static WorkflowTodoResponse Map(WorkflowTodoRecord row) =>
        new(row.Id, row.InstanceId, row.StepId, row.AssigneeUserId, row.StatusKey,
            row.ArrivedAtUtc, row.CompletedAtUtc, row.ResultActionKey, row.Revision);

    private static Result<WorkflowInstanceResponse> Failure(string code, ErrorType type) =>
        Result<WorkflowInstanceResponse>.Failure(
            new Error(code, "The workflow todo operation failed.", type));

    private static Result<WorkflowTodoDetailResponse> DetailFailure(string code, ErrorType type) =>
        Result<WorkflowTodoDetailResponse>.Failure(
            new Error(code, "The workflow todo detail operation failed.", type));
}
