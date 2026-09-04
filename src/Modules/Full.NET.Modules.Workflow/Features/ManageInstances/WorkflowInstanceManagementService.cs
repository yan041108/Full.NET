using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.ManageInstances;

/// <summary>在可信作用域内固定发布版本，并原子创建实例、首步骤和本人待办。</summary>
/// <param name="queryExecutor">受控查询执行器。</param>
/// <param name="commandExecutor">显式 SQL 命令执行器。</param>
/// <param name="transaction">Workflow 本地事务边界。</param>
/// <param name="currentTenant">可信当前租户上下文。</param>
/// <param name="clock">统一 UTC 时钟。</param>
/// <param name="idGenerator">UUID v7 标识生成器。</param>
/// <param name="databaseOptions">数据库提供程序配置。</param>
/// <param name="automaticTransitionWriter">自动节点迁移写入器。</param>
internal sealed class WorkflowInstanceManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions,
    WorkflowAutomaticTransitionWriter automaticTransitionWriter)
{
    /// <summary>按已发布版本启动实例，并在同一本地事务内建立首待办和起始抄送。</summary>
    /// <param name="actorUserId">发起人的稳定用户标识。</param>
    /// <param name="request">包含版本、业务标识和表单数据的启动请求。</param>
    /// <param name="cancellationToken">取消当前异步操作的令牌。</param>
    /// <returns>新建实例或幂等重放实例的结果。</returns>
    public async Task<Result<WorkflowInstanceResponse>> StartAsync(
        Guid actorUserId,
        StartWorkflowInstanceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsValid(request))
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var asset = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
            WorkflowSql.FindRuntimeAsset,
            Parameters(("DefinitionVersionId", request.DefinitionVersionId),
                ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.Validation);
        }

        var definition = JsonSerializer.Deserialize(
            asset.CanonicalJson,
            WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var formSchema = JsonSerializer.Deserialize(
            asset.FormSchemaJson,
            WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        if (definition is null || formSchema is null ||
            !WorkflowRuntimePlan.TryCreate(definition, formSchema, out var runtimePlan) ||
            !WorkflowFormValueValidator.Validate(formSchema, request.InitialValues))
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var initialValues = request.InitialValues.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        if (!runtimePlan!.TryResolveStart(initialValues, out var startTransition) ||
            startTransition.NextApprovalNodeKey is not { } approvalNodeKey)
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var instanceId = idGenerator.NewId();
        var stepId = idGenerator.NewId();
        var todoId = idGenerator.NewId();
        var now = clock.UtcNow;
        var requestHash = HashStartRequest(request);
        try
        {
            return await transaction.ExecuteResultAsync(async token =>
            {
                await commandExecutor.ExecuteAsync(WorkflowSql.InsertInstance,
                    Parameters(("Id", instanceId), ("TenantId", scope.TenantId),
                        ("ScopeKey", scope.ScopeKey), ("TenantScopeKey", scope.TenantScopeKey),
                        ("DefinitionVersionId", asset.DefinitionVersionId),
                        ("FormVersionId", asset.FormVersionId),
                        ("BusinessType", request.BusinessType.Trim()),
                        ("BusinessId", request.BusinessId.Trim()),
                        ("StartedById", actorUserId), ("StartedAtUtc", now)), token).ConfigureAwait(false);
                await commandExecutor.ExecuteAsync(WorkflowSql.InsertStep,
                    Parameters(("Id", stepId), ("InstanceId", instanceId),
                        ("NodeKey", approvalNodeKey), ("AssignedUserId", actorUserId),
                        ("StartedAtUtc", now)), token).ConfigureAwait(false);
                await commandExecutor.ExecuteAsync(WorkflowSql.InsertTodo,
                    Parameters(("Id", todoId), ("InstanceId", instanceId), ("StepId", stepId),
                        ("AssigneeUserId", actorUserId), ("ArrivedAtUtc", now)), token).ConfigureAwait(false);
                await commandExecutor.ExecuteAsync(WorkflowSql.InsertFormSubmission,
                    Parameters(("Id", idGenerator.NewId()), ("InstanceId", instanceId),
                        ("FormVersionId", asset.FormVersionId),
                        ("SubmissionJson", request.InitialValues.GetRawText()),
                        ("UpdatedById", actorUserId), ("UpdatedAtUtc", now)), token).ConfigureAwait(false);
                await commandExecutor.ExecuteAsync(WorkflowSql.InsertActionRecord,
                    Parameters(("Id", idGenerator.NewId()), ("InstanceId", instanceId),
                        ("StepId", stepId), ("TodoId", todoId), ("ActionKey", "start"),
                        ("ActorUserId", actorUserId), ("InstanceRevision", 1L),
                        ("IdempotencyKey", request.IdempotencyKey.Trim()),
                        ("CommentSummary", null), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
                await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
                    Parameters(("Id", idGenerator.NewId()), ("InstanceId", instanceId),
                        ("StepId", stepId), ("TransitionKey", "instance.start"),
                        ("FromStatusKey", null), ("ToStatusKey", "active"),
                        ("IdempotencyKey", request.IdempotencyKey.Trim()),
                        ("Summary", requestHash), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
                await automaticTransitionWriter.WriteAsync(
                    instanceId,
                    scope.TenantScopeKey,
                    startTransition.AutomaticNodes,
                    now,
                    token).ConfigureAwait(false);
                await commandExecutor.ExecuteAsync(WorkflowSql.InsertDomainAudit,
                    Parameters(("Id", idGenerator.NewId()), ("TenantId", scope.TenantId),
                        ("ScopeKey", scope.ScopeKey), ("InstanceId", instanceId),
                        ("OperationKey", "instance.start"), ("ActorUserId", actorUserId),
                        ("ResourceTypeKey", "instance"), ("ResourceId", instanceId),
                        ("OutcomeKey", "succeeded"),
                        ("DetailJson", $"{{\"definitionVersionId\":\"{asset.DefinitionVersionId:D}\"}}"),
                        ("CreatedAtUtc", now)), token).ConfigureAwait(false);

                return Result<WorkflowInstanceResponse>.Success(new(
                    instanceId, asset.DefinitionVersionId, asset.FormVersionId,
                    request.BusinessType.Trim(), request.BusinessId.Trim(), "active", 1,
                    todoId, now));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return await ResolveStartConflictAsync(
                actorUserId, request, scope, requestHash, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<Result<WorkflowInstanceResponse>> CancelAsync(
        Guid instanceId,
        Guid actorUserId,
        CancelWorkflowInstanceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (instanceId == Guid.Empty || !IsValid(request))
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var requestHash = HashCancelRequest(request);
        try
        {
            var result = await transaction.ExecuteResultAsync(
                token => CancelCoreAsync(instanceId, actorUserId, request, scope, requestHash, token),
                cancellationToken).ConfigureAwait(false);
            return !result.IsSuccess && result.Error?.Code == WorkflowErrorCodes.RevisionConflict
                ? await ResolveCancelReplayAsync(
                    instanceId, actorUserId, request, scope, requestHash, cancellationToken).ConfigureAwait(false)
                : result;
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return await ResolveCancelReplayAsync(
                instanceId, actorUserId, request, scope, requestHash, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<Result<WorkflowInstanceResponse>> GetAsync(
        Guid instanceId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindInstanceById,
            Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersionId)
        {
            return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        if (!await CanReadAsync(instance, actorUserId, scope, cancellationToken).ConfigureAwait(false))
        {
            return Failure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
        }

        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
            WorkflowSql.FindActiveTodoByInstance,
            Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.StatusKey,
            instance.Revision, todo?.Id, instance.StartedAtUtc));
    }

    public async Task<Result<IReadOnlyList<WorkflowExecutionLogResponse>>> ListExecutionLogsAsync(
        Guid instanceId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindInstanceById,
            Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        if (instance is null)
        {
            return Result<IReadOnlyList<WorkflowExecutionLogResponse>>.Failure(
                new Error(WorkflowErrorCodes.VersionNotPublished,
                    "The workflow instance was not found.", ErrorType.NotFound));
        }

        if (!await CanReadAsync(instance, actorUserId, scope, cancellationToken).ConfigureAwait(false))
        {
            return Result<IReadOnlyList<WorkflowExecutionLogResponse>>.Failure(
                new Error(WorkflowErrorCodes.InstanceForbidden,
                    "The workflow instance cannot be read by the current user.", ErrorType.Forbidden));
        }

        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => WorkflowSql.ListExecutionLogsSqlServer,
            DatabaseProvider.MySql => WorkflowSql.ListExecutionLogsMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };
        var rows = await queryExecutor.QueryAsync<WorkflowExecutionLogRecord>(
            statement,
            Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey),
                ("Take", 200)), cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<WorkflowExecutionLogResponse>>.Success(rows.Select(row => new WorkflowExecutionLogResponse(
            row.Id, row.InstanceId, row.StepId, row.TransitionKey,
            row.FromStatusKey, row.ToStatusKey, row.CreatedAtUtc)).ToArray());
    }

    private static bool IsValid(StartWorkflowInstanceRequest request) =>
        request.DefinitionVersionId != Guid.Empty &&
        request.InitialValues.ValueKind == JsonValueKind.Object &&
        request.BusinessType.Trim() is { Length: >= 1 and <= 64 } &&
        request.BusinessId.Trim() is { Length: >= 1 and <= 128 } &&
        request.IdempotencyKey.Trim() is { Length: >= 1 and <= 128 };

    private static bool IsValid(CancelWorkflowInstanceRequest request) =>
        request.ExpectedRevision >= 1 &&
        request.IdempotencyKey.Trim() is { Length: >= 1 and <= 128 } &&
        request.Reason?.Trim() is not { Length: > 512 };

    private async Task<Result<WorkflowInstanceResponse>> CancelCoreAsync(
        Guid instanceId,
        Guid actorUserId,
        CancelWorkflowInstanceRequest request,
        WorkflowManagementScope scope,
        string requestHash,
        CancellationToken token)
    {
        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindInstanceById,
            Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)),
            token).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersionId)
        {
            return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        if (!await CanReadAsync(instance, actorUserId, scope, token).ConfigureAwait(false))
        {
            return Failure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
        }

        var idempotencyKey = request.IdempotencyKey.Trim();
        var receipt = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
            WorkflowSql.FindActionReceipt,
            Parameters(("InstanceId", instanceId), ("IdempotencyKey", idempotencyKey)),
            token).ConfigureAwait(false);
        if (receipt is not null)
        {
            return receipt.ActionKey == "cancel" && receipt.ActorUserId == actorUserId &&
                   receipt.RequestHash == requestHash
                ? Result<WorkflowInstanceResponse>.Success(Map(instance, formVersionId, null))
                : Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        if (instance.StatusKey != "active")
        {
            return Failure(WorkflowErrorCodes.InstanceTerminal, ErrorType.Conflict);
        }

        if (instance.Revision != request.ExpectedRevision)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var activeWork = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActiveWorkRecord>(
            WorkflowSql.FindActiveWorkByInstance,
            Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)),
            token).ConfigureAwait(false);
        if (activeWork is null)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var now = clock.UtcNow;
        var todoUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.CancelTodoWithRevision,
            Parameters(("Id", activeWork.TodoId), ("InstanceId", instanceId),
                ("TenantScopeKey", scope.TenantScopeKey), ("CompletedAtUtc", now),
                ("Revision", activeWork.TodoRevision)), token).ConfigureAwait(false);
        var stepUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.CancelStepWithRevision,
            Parameters(("Id", activeWork.StepId), ("InstanceId", instanceId),
                ("CompletedAtUtc", now), ("Revision", activeWork.StepRevision)), token).ConfigureAwait(false);
        var instanceUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.CancelInstanceWithRevision,
            Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey),
                ("CancelledById", actorUserId), ("CancelledAtUtc", now),
                ("CancellationReason", NormalizeReason(request.Reason)),
                ("Revision", request.ExpectedRevision)), token).ConfigureAwait(false);
        if (todoUpdated != 1 || stepUpdated != 1 || instanceUpdated != 1)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        await commandExecutor.ExecuteAsync(WorkflowSql.InsertActionRecord,
            Parameters(("Id", idGenerator.NewId()), ("InstanceId", instanceId),
                ("StepId", activeWork.StepId), ("TodoId", activeWork.TodoId),
                ("ActionKey", "cancel"), ("ActorUserId", actorUserId),
                ("InstanceRevision", request.ExpectedRevision + 1),
                ("IdempotencyKey", idempotencyKey),
                ("CommentSummary", NormalizeReason(request.Reason)), ("CreatedAtUtc", now)),
            token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            Parameters(("Id", idGenerator.NewId()), ("InstanceId", instanceId),
                ("StepId", activeWork.StepId), ("TransitionKey", "instance.cancel"),
                ("FromStatusKey", "active"), ("ToStatusKey", "cancelled"),
                ("IdempotencyKey", idempotencyKey), ("Summary", requestHash),
                ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            Parameters(("Id", idGenerator.NewId()), ("TenantId", scope.TenantId),
                ("ScopeKey", scope.ScopeKey), ("InstanceId", instanceId),
                ("OperationKey", "instance.cancel"), ("ActorUserId", actorUserId),
                ("ResourceTypeKey", "instance"), ("ResourceId", instanceId),
                ("OutcomeKey", "succeeded"),
                ("DetailJson", $"{{\"previousRevision\":{request.ExpectedRevision}}}"),
                ("CreatedAtUtc", now)), token).ConfigureAwait(false);

        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, "cancelled",
            request.ExpectedRevision + 1, null, instance.StartedAtUtc));
    }

    private async Task<Result<WorkflowInstanceResponse>> ResolveStartConflictAsync(
        Guid actorUserId,
        StartWorkflowInstanceRequest request,
        WorkflowManagementScope scope,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindActiveInstanceByBusinessKey,
            Parameters(("TenantScopeKey", scope.TenantScopeKey),
                ("BusinessType", request.BusinessType.Trim()),
                ("BusinessId", request.BusinessId.Trim())), cancellationToken).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersionId)
        {
            return Failure(WorkflowErrorCodes.ActiveInstanceExists, ErrorType.Conflict);
        }

        var receipt = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
            WorkflowSql.FindActionReceipt,
            Parameters(("InstanceId", instance.Id),
                ("IdempotencyKey", request.IdempotencyKey.Trim())), cancellationToken).ConfigureAwait(false);
        if (receipt is null || receipt.ActionKey != "start" || receipt.ActorUserId != actorUserId ||
            receipt.RequestHash != requestHash)
        {
            return Failure(WorkflowErrorCodes.ActiveInstanceExists, ErrorType.Conflict);
        }

        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
            WorkflowSql.FindActiveTodoByInstance,
            Parameters(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        return todo is null
            ? Failure(WorkflowErrorCodes.ActiveInstanceExists, ErrorType.Conflict)
            : Result<WorkflowInstanceResponse>.Success(new(
                instance.Id, instance.DefinitionVersionId, formVersionId,
                instance.BusinessType, instance.BusinessId, instance.StatusKey,
                instance.Revision, todo.Id, instance.StartedAtUtc));
    }

    private static string HashStartRequest(StartWorkflowInstanceRequest request)
    {
        var value = $"{request.DefinitionVersionId:D}\n{request.BusinessType.Trim()}\n{request.BusinessId.Trim()}\n{request.InitialValues.GetRawText()}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private async Task<Result<WorkflowInstanceResponse>> ResolveCancelReplayAsync(
        Guid instanceId,
        Guid actorUserId,
        CancelWorkflowInstanceRequest request,
        WorkflowManagementScope scope,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
            WorkflowSql.FindInstanceById,
            Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        if (instance?.FormVersionId is not { } formVersionId)
        {
            return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        if (!await CanReadAsync(instance, actorUserId, scope, cancellationToken).ConfigureAwait(false))
        {
            return Failure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
        }

        var receipt = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
            WorkflowSql.FindActionReceipt,
            Parameters(("InstanceId", instanceId),
                ("IdempotencyKey", request.IdempotencyKey.Trim())), cancellationToken).ConfigureAwait(false);
        return receipt?.ActionKey == "cancel" && receipt.ActorUserId == actorUserId &&
               receipt.RequestHash == requestHash
            ? Result<WorkflowInstanceResponse>.Success(Map(instance, formVersionId, null))
            : Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
    }

    private static string HashCancelRequest(CancelWorkflowInstanceRequest request)
    {
        var value = $"{request.ExpectedRevision}\n{NormalizeReason(request.Reason)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string? NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private static WorkflowInstanceResponse Map(
        WorkflowInstanceRecord instance,
        Guid formVersionId,
        Guid? activeTodoId) =>
        new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.StatusKey,
            instance.Revision, activeTodoId, instance.StartedAtUtc);

    private async Task<bool> CanReadAsync(
        WorkflowInstanceRecord instance,
        Guid actorUserId,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken)
    {
        if (instance.StartedById == actorUserId)
        {
            return true;
        }

        var participant = await queryExecutor.QuerySingleOrDefaultAsync<int>(
            WorkflowSql.IsInstanceParticipant,
            Parameters(("InstanceId", instance.Id), ("ActorUserId", actorUserId),
                ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        return participant == 1;
    }

    private static Dictionary<string, object?> Parameters(params (string Name, object? Value)[] pairs) =>
        WorkflowSqlParameters.Create(pairs);

    private static Result<WorkflowInstanceResponse> Failure(string code, ErrorType type) =>
        Result<WorkflowInstanceResponse>.Failure(new Error(code, "The workflow instance operation failed.", type));
}
