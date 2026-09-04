using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Features.ManageInstances;

/// <summary>在可信作用域内执行工作流活动待办改派，并保持运行状态、审计和通知原子一致。</summary>
/// <param name="queryExecutor">受控查询执行器。</param>
/// <param name="commandExecutor">显式 SQL 命令执行器。</param>
/// <param name="transaction">Workflow 本地事务边界。</param>
/// <param name="currentTenant">可信当前租户上下文。</param>
/// <param name="clock">统一 UTC 时钟。</param>
/// <param name="idGenerator">UUID v7 标识生成器。</param>
/// <param name="hostUserDirectory">Host 活动用户批量目录。</param>
/// <param name="tenantUserDirectory">当前 Tenant 活动用户批量目录。</param>
/// <param name="notificationPublisher">工作流提醒事务 Outbox 发布器。</param>
internal sealed class WorkflowInstanceRecoveryService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IHostUserBatchSelectionDirectory hostUserDirectory,
    ITenantUserSelectionDirectory tenantUserDirectory,
    WorkflowNotificationOutboxPublisher notificationPublisher)
{
    private const string ActionKey = "reassign";
    private const string TransitionKey = "todo.reassign";

    /// <summary>把实例的唯一活动待办改派给同一可信作用域内的活动用户。</summary>
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
        if (!IsValid(instanceId, actorUserId, request))
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (!await IsActiveTargetAsync(request.AssigneeUserId, scope, cancellationToken)
                .ConfigureAwait(false))
        {
            return Failure(WorkflowErrorCodes.TodoAssigneeNotFound, ErrorType.Validation);
        }

        var requestHash = HashRequest(request);
        try
        {
            var result = await transaction.ExecuteResultAsync(
                token => ReassignCoreAsync(
                    instanceId, actorUserId, request, scope, requestHash, token),
                cancellationToken).ConfigureAwait(false);
            return !result.IsSuccess && result.Error?.Code == WorkflowErrorCodes.RevisionConflict
                ? await ResolveReplayAsync(
                    instanceId, actorUserId, request, scope, requestHash, cancellationToken)
                    .ConfigureAwait(false)
                : result;
        }
        catch (DataCommandException exception) when (
            exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return await ResolveReplayAsync(
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
            var users = await tenantUserDirectory.FindActiveTenantUsersAsync(ids, cancellationToken)
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
        if (instance?.FormVersionId is not { } formVersionId)
        {
            return Failure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        }

        var idempotencyKey = request.IdempotencyKey.Trim();
        var receipt = await FindReceiptAsync(instanceId, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (receipt is not null)
        {
            if (!IsReplay(receipt, actorUserId, requestHash))
            {
                return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            }

            var replayTodo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindActiveTodoByInstance,
                Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken).ConfigureAwait(false);
            return Result<WorkflowInstanceResponse>.Success(Map(instance, formVersionId, replayTodo?.Id));
        }

        if (instance.StatusKey != "active")
        {
            return Failure(WorkflowErrorCodes.InstanceTerminal, ErrorType.Conflict);
        }

        if (instance.Revision != request.ExpectedRevision)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
            WorkflowSql.FindActiveTodoByInstance,
            Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        if (todo is null)
        {
            return Failure(WorkflowErrorCodes.TodoNotActive, ErrorType.Conflict);
        }

        if (todo.AssigneeUserId == request.AssigneeUserId)
        {
            return Failure(WorkflowErrorCodes.TodoAssigneeUnchanged, ErrorType.Validation);
        }

        // 待办修订与实例修订必须同时命中，任何并发变化都会让 ExecuteResultAsync 回滚全部追加记录。
        var todoUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.ReassignTodoWithRevision,
            Parameters(("Id", todo.Id), ("InstanceId", instanceId),
                ("ExpectedAssigneeUserId", todo.AssigneeUserId),
                ("AssigneeUserId", request.AssigneeUserId), ("Revision", todo.Revision),
                ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        var instanceUpdated = await commandExecutor.ExecuteAsync(
            WorkflowSql.AdvanceInstanceWithRevision,
            Parameters(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey),
                ("Revision", request.ExpectedRevision)), cancellationToken).ConfigureAwait(false);
        if (todoUpdated != 1 || instanceUpdated != 1)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var now = clock.UtcNow;
        var reason = NormalizeReason(request.Reason);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertActionRecord,
            Parameters(("Id", idGenerator.NewId()), ("InstanceId", instanceId),
                ("StepId", todo.StepId), ("TodoId", todo.Id), ("ActionKey", ActionKey),
                ("ActorUserId", actorUserId), ("InstanceRevision", request.ExpectedRevision + 1),
                ("IdempotencyKey", idempotencyKey), ("CommentSummary", reason),
                ("CreatedAtUtc", now)), cancellationToken).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            Parameters(("Id", idGenerator.NewId()), ("InstanceId", instanceId),
                ("StepId", todo.StepId), ("TransitionKey", TransitionKey),
                ("FromStatusKey", "active"), ("ToStatusKey", "active"),
                ("IdempotencyKey", idempotencyKey), ("Summary", requestHash),
                ("CreatedAtUtc", now)), cancellationToken).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            Parameters(("Id", idGenerator.NewId()), ("TenantId", scope.TenantId),
                ("ScopeKey", scope.ScopeKey), ("InstanceId", instanceId),
                ("OperationKey", "instance.reassign"), ("ActorUserId", actorUserId),
                ("ResourceTypeKey", "todo"), ("ResourceId", todo.Id),
                ("OutcomeKey", "succeeded"),
                ("DetailJson", CreateAuditDetail(todo.AssigneeUserId, request.AssigneeUserId)),
                ("CreatedAtUtc", now)), cancellationToken).ConfigureAwait(false);

        // 新办理人的提醒事实与改派状态同事务提交，下游至少一次重试不改变 Workflow 权威状态。
        await notificationPublisher.PublishTodoAssignedAsync(
            instanceId, todo.Id, request.AssigneeUserId,
            instance.BusinessType, instance.BusinessId, now, cancellationToken).ConfigureAwait(false);

        return Result<WorkflowInstanceResponse>.Success(new(
            instance.Id, instance.DefinitionVersionId, formVersionId,
            instance.BusinessType, instance.BusinessId, instance.StatusKey,
            request.ExpectedRevision + 1, todo.Id, instance.StartedAtUtc));
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
        if (!IsReplay(receipt, actorUserId, requestHash))
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var todo = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
            WorkflowSql.FindActiveTodoByInstance,
            Parameters(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        return Result<WorkflowInstanceResponse>.Success(Map(instance, formVersionId, todo?.Id));
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
        request.AssigneeUserId != Guid.Empty && request.ExpectedRevision >= 1 &&
        request.IdempotencyKey.Trim() is { Length: >= 1 and <= 128 } &&
        request.Reason?.Trim() is not { Length: > 512 };

    /// <summary>生成与传输格式无关的改派请求摘要。</summary>
    /// <param name="request">已通过边界校验的请求。</param>
    /// <returns>小写十六进制 SHA-256 摘要。</returns>
    private static string HashRequest(ReassignWorkflowInstanceRequest request)
    {
        var value = $"{request.AssigneeUserId:D}\n{request.ExpectedRevision}\n{NormalizeReason(request.Reason)}";
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
            instance.BusinessType, instance.BusinessId, instance.StatusKey,
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
