using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageDefinitions;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>先验证本人的活动动作资源，再开放可信作用域内的最小候选投影。</summary>
internal sealed class WorkflowTodoAssigneeCandidateQueryService(IQueryExecutor query, ICurrentTenant tenant,
    IHostUserSelectionDirectory hostUsers, ITenantMemberSelectionDirectory tenantUsers, IHostUserBatchSelectionDirectory hostBatch, IClock clock)
{
    public Task<Result<WorkflowRecipientCandidatePageResponse>> ListCountersignAsync(Guid todoId, Guid actorId,
        int page, int pageSize, CancellationToken token = default) => ListAsync(todoId, actorId, page, pageSize, "countersign", token);

    public Task<Result<WorkflowRecipientCandidatePageResponse>> ListTransferAsync(Guid todoId, Guid actorId,
        int page, int pageSize, CancellationToken token = default) => ListAsync(todoId, actorId, page, pageSize, "transfer", token);

    public Task<Result<WorkflowRecipientCandidatePageResponse>> ListDelegationAsync(Guid todoId, Guid actorId,
        int page, int pageSize, CancellationToken token = default) => ListAsync(todoId, actorId, page, pageSize, "delegate", token);

    /// <summary>已通过实例读取和恢复权限的管理入口，按明确实例与活动待办读取候选；交接写入仍独立重验。</summary>
    internal async Task<Result<WorkflowReassignmentCandidatePageResponse>> ListReassignmentAsync(Guid instanceId, Guid todoId,
        Guid actorId, int page, int pageSize, CancellationToken token = default)
    {
        if (instanceId == Guid.Empty || todoId == Guid.Empty || actorId == Guid.Empty
            || page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return ReassignmentFailure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var scope = WorkflowManagementScope.Resolve(tenant);
        var instance = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.FormVersionId is null) return ReassignmentFailure(WorkflowErrorCodes.VersionNotPublished, ErrorType.NotFound);
        var todo = await query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById,
            WorkflowSqlParameters.Create(("Id", todoId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (todo is null || todo.InstanceId != instanceId) return ReassignmentFailure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        if (instance.StatusKey != "active" || todo.StatusKey != "active") return ReassignmentFailure(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        if (!await WorkflowCollaborationRuntimeGate.AllowsAsync(instanceId, scope, actorId, clock.UtcNow, query, token).ConfigureAwait(false))
            return ReassignmentFailure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
        var result = await ReadPageAsync(instanceId, scope, page, pageSize, token).ConfigureAwait(false);
        if (!result.IsSuccess) return Result<WorkflowReassignmentCandidatePageResponse>.Failure(result.Error!);
        var value = result.Value!;
        return Result<WorkflowReassignmentCandidatePageResponse>.Success(new(instanceId, instance.Revision, todoId, todo.Revision,
            value.Items, value.Page, value.PageSize, value.Total));
    }

    private static Result<WorkflowReassignmentCandidatePageResponse> ReassignmentFailure(string code, ErrorType type) =>
        Result<WorkflowReassignmentCandidatePageResponse>.Failure(new Error(code, "Reassignment candidates are unavailable for this resource.", type));

    // 动作键只由上面的显式入口提供，不接受客户端指定权限或跳过资源校验。
    private async Task<Result<WorkflowRecipientCandidatePageResponse>> ListAsync(Guid todoId, Guid actorId,
        int page, int pageSize, string actionKey, CancellationToken token)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var scope = WorkflowManagementScope.Resolve(tenant);
        var todo = await query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById,
            WorkflowSqlParameters.Create(("Id", todoId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (todo is null) return Failure(WorkflowErrorCodes.TodoNotActive, ErrorType.NotFound);
        if (todo.AssigneeUserId != actorId) return Failure(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        if (todo.StatusKey != "active")
            return Failure(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        var instance = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", todo.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.StatusKey != "active") return Failure(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict);
        if (!await WorkflowCollaborationRuntimeGate.AllowsAsync(instance.Id, scope, actorId, clock.UtcNow, query, token).ConfigureAwait(false))
            return Failure(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null || !WorkflowHumanActionPolicy.TryResolve(asset.CanonicalJson, todo.NodeKey, out var policy))
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        if (!policy!.Actions.Contains(actionKey)) return Failure(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
        if (await query.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForProxy,
            WorkflowSqlParameters.Create(("TodoId", todoId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null)
            return Failure(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
        if (actionKey == "countersign" && (!WorkflowHumanActivityKinds.IsApproval(todo.NodeTypeKey) ||
            todo.ApprovalModeKey is not ("single" or "all" or "any" or "nOfM" or "percent" or "sequential" or "group")))
            return Result<WorkflowRecipientCandidatePageResponse>.Success(new([], page, pageSize, 0));
        if (await query.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(WorkflowSql.FindCountersignItemByTodoId,
                WorkflowSqlParameters.Create(("TodoId", todoId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null ||
            await query.QuerySingleOrDefaultAsync<WorkflowCountersignChainRecord>(WorkflowSql.FindActiveCountersignChainByOriginTodo,
                WorkflowSqlParameters.Create(("OriginTodoId", todoId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) is not null)
            return Result<WorkflowRecipientCandidatePageResponse>.Success(new([], page, pageSize, 0));
        return await ReadPageAsync(instance.Id, scope, page, pageSize, token).ConfigureAwait(false);
    }

    // 保留协作同意集合和当前成员过滤；管理改派与个人交接使用同一最小投影，不扩大候选范围。
    private async Task<Result<WorkflowRecipientCandidatePageResponse>> ReadPageAsync(Guid instanceId, WorkflowManagementScope scope,
        int page, int pageSize, CancellationToken token)
    {
        var acceptedMembers = await WorkflowCollaborationOriginPolicy.ReadMembersAsync(instanceId, scope, query, token).ConfigureAwait(false);
        if (acceptedMembers is not null)
        {
            // 先按固定同意集合批量核对当前活动身份，再分页；总数不能包含外部成员。
            var allowed = scope.TenantId is not null
                ? (await tenantUsers.FindActiveTenantMembersAsync(acceptedMembers, token).ConfigureAwait(false)).Values
                    .Select(x => new WorkflowRecipientCandidateResponse(x.Id, x.Username, x.DisplayName)).ToArray()
                : (await hostBatch.FindActiveHostUsersAsync(acceptedMembers, token).ConfigureAwait(false)).Values
                    .Select(x => new WorkflowRecipientCandidateResponse(x.Id, x.Username, x.DisplayName)).ToArray();
            return Result<WorkflowRecipientCandidatePageResponse>.Success(new(allowed.OrderBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArray(), page, pageSize, allowed.Length));
        }
        if (scope.TenantId is not null)
        {
            var users = await tenantUsers.ListActiveTenantMembersAsync(page, pageSize, token).ConfigureAwait(false);
            return Result<WorkflowRecipientCandidatePageResponse>.Success(new(users.Items.Select(x =>
                new WorkflowRecipientCandidateResponse(x.Id, x.Username, x.DisplayName)).ToArray(), users.Page, users.PageSize, users.Total));
        }
        var host = await hostUsers.ListActiveHostUsersAsync(page, pageSize, token).ConfigureAwait(false);
        return Result<WorkflowRecipientCandidatePageResponse>.Success(new(host.Items.Select(x =>
            new WorkflowRecipientCandidateResponse(x.Id, x.Username, x.DisplayName)).ToArray(), host.Page, host.PageSize, host.Total));
    }

    private static Result<WorkflowRecipientCandidatePageResponse> Failure(string code, ErrorType type) =>
        Result<WorkflowRecipientCandidatePageResponse>.Failure(new Error(code, "Assignee candidates are unavailable for this todo action.", type));
}
