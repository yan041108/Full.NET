using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageDefinitions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>先验证本人的活动动作资源，再开放可信作用域内的最小候选投影。</summary>
internal sealed class WorkflowTodoAssigneeCandidateQueryService(IQueryExecutor query, ICurrentTenant tenant,
    IHostUserSelectionDirectory hostUsers, ITenantMemberSelectionDirectory tenantUsers)
{
    public Task<Result<WorkflowRecipientCandidatePageResponse>> ListCountersignAsync(Guid todoId, Guid actorId,
        int page, int pageSize, CancellationToken token = default) => ListAsync(todoId, actorId, page, pageSize, "countersign", token);

    public Task<Result<WorkflowRecipientCandidatePageResponse>> ListTransferAsync(Guid todoId, Guid actorId,
        int page, int pageSize, CancellationToken token = default) => ListAsync(todoId, actorId, page, pageSize, "transfer", token);

    public Task<Result<WorkflowRecipientCandidatePageResponse>> ListDelegationAsync(Guid todoId, Guid actorId,
        int page, int pageSize, CancellationToken token = default) => ListAsync(todoId, actorId, page, pageSize, "delegate", token);

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
        // 查询目录不代表授权办理；提交加签时仍重新验证全部候选、修订和原待办状态。
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
