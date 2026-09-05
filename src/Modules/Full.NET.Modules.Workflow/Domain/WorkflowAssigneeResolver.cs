using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在 Workflow 边界内把固化办理人策略解析为可信活动用户标识集合。</summary>
/// <param name="hostUserDirectory">Host 活动用户批量目录。</param>
/// <param name="tenantUserDirectory">Tenant 活动用户批量目录。</param>
/// <param name="roleMemberDirectory">角色成员批量目录。</param>
/// <param name="unitLeaderDirectory">机构负责人批量目录。</param>
internal sealed class WorkflowAssigneeResolver(
    IHostUserBatchSelectionDirectory hostUserDirectory,
    ITenantUserSelectionDirectory tenantUserDirectory,
    IWorkflowRoleMemberDirectory roleMemberDirectory,
    IWorkflowUnitLeaderDirectory unitLeaderDirectory)
{
    private const int MaximumResolvedUsers = 20;

    /// <summary>解析办理人策略并合并显式多人审批用户，返回去重后的活动用户标识。</summary>
    /// <param name="assigneePolicy">节点固化的办理人解析策略。</param>
    /// <param name="explicitApproverUserIds">多人审批策略中的显式用户标识。</param>
    /// <param name="scope">当前可信工作流管理作用域。</param>
    /// <param name="initiatorUserId">流程发起人或当前推进上下文的用户标识。</param>
    /// <param name="cancellationToken">取消当前解析的令牌。</param>
    /// <returns>稳定排序后的活动用户标识；解析失败时返回业务错误。</returns>
    public async Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
        WorkflowAssigneePolicy assigneePolicy,
        IReadOnlyList<Guid> explicitApproverUserIds,
        WorkflowManagementScope scope,
        Guid initiatorUserId,
        CancellationToken cancellationToken = default)
    {
        var resolved = new List<Guid>();
        foreach (var source in assigneePolicy.Sources)
        {
            var sourceResult = await ResolveSourceAsync(
                    source,
                    scope,
                    initiatorUserId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!sourceResult.IsSuccess)
            {
                return Result<IReadOnlyList<Guid>>.Failure(sourceResult.Error!);
            }

            foreach (var userId in sourceResult.Value!)
            {
                if (!resolved.Contains(userId))
                {
                    resolved.Add(userId);
                }
            }
        }

        foreach (var userId in explicitApproverUserIds)
        {
            if (!resolved.Contains(userId))
            {
                resolved.Add(userId);
            }
        }

        if (resolved.Count is < 1 or > MaximumResolvedUsers)
        {
            return Failure();
        }

        var validUsers = await FindActiveUsersAsync(resolved, scope, cancellationToken).ConfigureAwait(false);
        if (validUsers.Count != resolved.Count)
        {
            return Failure();
        }

        return Result<IReadOnlyList<Guid>>.Success(resolved);
    }

    /// <summary>解析单条办理人来源。</summary>
    /// <param name="source">固化来源配置。</param>
    /// <param name="scope">当前可信作用域。</param>
    /// <param name="initiatorUserId">发起人标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该来源解析出的用户标识列表。</returns>
    private async Task<Result<IReadOnlyList<Guid>>> ResolveSourceAsync(
        WorkflowAssigneeSource source,
        WorkflowManagementScope scope,
        Guid initiatorUserId,
        CancellationToken cancellationToken)
    {
        switch (source.ResolverKindKey)
        {
            case WorkflowAssigneePolicy.SpecifiedUsers:
                return Result<IReadOnlyList<Guid>>.Success(source.UserIds);
            case WorkflowAssigneePolicy.Initiator:
                return Result<IReadOnlyList<Guid>>.Success([initiatorUserId]);
            case WorkflowAssigneePolicy.RoleMembers:
            {
                var members = await roleMemberDirectory
                    .FindActiveMemberUserIdsByRoleIdsAsync(source.RoleIds, cancellationToken)
                    .ConfigureAwait(false);
                if (source.RoleIds.Any(roleId => !members.ContainsKey(roleId)) ||
                    members.Values.All(userIds => userIds.Count == 0))
                {
                    return Failure();
                }

                return Result<IReadOnlyList<Guid>>.Success(
                    members.Values.SelectMany(userIds => userIds).Distinct().ToArray());
            }
            case WorkflowAssigneePolicy.OrganizationUnitLeader:
                if (!scope.TenantId.HasValue || source.UnitId is not { } unitId)
                {
                    return Failure();
                }

                var leaders = await unitLeaderDirectory
                    .FindActiveUnitLeaderUserIdsAsync([unitId], cancellationToken)
                    .ConfigureAwait(false);
                return leaders.TryGetValue(unitId, out var leaderUserId)
                    ? Result<IReadOnlyList<Guid>>.Success([leaderUserId])
                    : Failure();
            case WorkflowAssigneePolicy.InitiatorPrimaryUnitLeader:
                if (!scope.TenantId.HasValue)
                {
                    return Failure();
                }

                var primaryLeader = await unitLeaderDirectory
                    .FindInitiatorPrimaryUnitLeaderUserIdAsync(initiatorUserId, cancellationToken)
                    .ConfigureAwait(false);
                return primaryLeader is { } leaderId
                    ? Result<IReadOnlyList<Guid>>.Success([leaderId])
                    : Failure();
            default:
                return Failure();
        }
    }

    /// <summary>批量校验解析结果中的用户是否仍处于活动状态。</summary>
    /// <param name="userIds">待校验用户标识。</param>
    /// <param name="scope">当前可信作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>有效用户目录项。</returns>
    private async Task<IReadOnlyDictionary<Guid, object>> FindActiveUsersAsync(
        IReadOnlyList<Guid> userIds,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.TenantId.HasValue)
        {
            var users = await tenantUserDirectory
                .FindActiveTenantUsersAsync(userIds, cancellationToken)
                .ConfigureAwait(false);
            return users.ToDictionary(pair => pair.Key, _ => (object)string.Empty);
        }

        var hostUsers = await hostUserDirectory
            .FindActiveHostUsersAsync(userIds, cancellationToken)
            .ConfigureAwait(false);
        return hostUsers.ToDictionary(pair => pair.Key, _ => (object)string.Empty);
    }

    /// <summary>构造办理人解析失败的统一业务错误。</summary>
    /// <returns>稳定验证错误。</returns>
    private static Result<IReadOnlyList<Guid>> Failure() =>
        Result<IReadOnlyList<Guid>>.Failure(new Error(
            WorkflowErrorCodes.DefinitionAssigneePolicyInvalid,
            "The assignee policy could not be resolved.",
            ErrorType.Validation));
}
