using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在发布前校验人工节点办理人策略的闭合实体引用。</summary>
/// <param name="hostUserDirectory">Host 活动用户批量目录。</param>
/// <param name="tenantUserDirectory">Tenant 活动用户批量目录。</param>
/// <param name="roleMemberDirectory">角色成员批量目录。</param>
/// <param name="unitLeaderDirectory">机构负责人批量目录。</param>
internal sealed class WorkflowAssigneePublishValidator(
    IHostUserBatchSelectionDirectory hostUserDirectory,
    ITenantMemberSelectionDirectory tenantUserDirectory,
    IWorkflowRoleMemberDirectory roleMemberDirectory,
    IWorkflowOrganizationAssigneeDirectory unitLeaderDirectory)
{
    /// <summary>显式无人策略允许活动空来源；替补逐级检查来源有效性，不要求每一级当前都有成员。</summary>
    public async Task<bool> ValidatePolicyAsync(WorkflowAssigneePolicy policy, WorkflowManagementScope scope, CancellationToken token,
        IReadOnlyList<Guid>? flowAdministratorUserIds = null)
    {
        var allowEmpty = policy.Handling?.WhenEmpty.ActionKey is not (null or "error");
        foreach (var source in policy.Sources)
            if (!IsScopeCompatible(source.ResolverKindKey, scope) || !await ValidateSourceAsync(source, scope, token, allowEmpty, flowAdministratorUserIds).ConfigureAwait(false)) return false;
        if (policy.Handling is not { } handling) return true;
        foreach (var source in handling.WhenEmpty.FallbackPolicies.Concat(handling.WhenInitiator.FallbackPolicies).SelectMany(fallback => fallback.Sources))
            if (!IsScopeCompatible(source.ResolverKindKey, scope) || !await ValidateSourceAsync(source, scope, token, allowEmpty: true, flowAdministratorUserIds: flowAdministratorUserIds).ConfigureAwait(false)) return false;
        return true;
    }

    /// <summary>校验单条办理人来源在发布作用域内可解析。</summary>
    /// <param name="source">固化来源配置。</param>
    /// <param name="scope">发布作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>来源闭合且实体有效时返回 <see langword="true"/>。</returns>
    public async Task<bool> ValidateSourceAsync(
        WorkflowAssigneeSource source,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken,
        bool allowEmpty = false,
        IReadOnlyList<Guid>? flowAdministratorUserIds = null)
    {
        switch (source.ResolverKindKey)
        {
            case WorkflowAssigneePolicy.FlowAdministrators:
                return flowAdministratorUserIds is not null && (allowEmpty && flowAdministratorUserIds.Count == 0 ||
                    await ValidateUsersAsync(flowAdministratorUserIds, scope, cancellationToken).ConfigureAwait(false));
            case WorkflowAssigneePolicy.StarterSelectedUsers:
                return source.StarterSelection is { } selection && (selection.Range.KindKey switch
                {
                    "all" => true,
                    "specified_users" => await ValidateUsersAsync(selection.Range.UserIds, scope, cancellationToken).ConfigureAwait(false),
                    "role_members" => await ValidateRolesAsync(selection.Range.RoleIds, cancellationToken).ConfigureAwait(false),
                    _ => false,
                });
            case WorkflowAssigneePolicy.FormUsers:
                return source.FieldKey is not null;
            case WorkflowAssigneePolicy.FormUnits:
                return scope.TenantId.HasValue && source.FieldKey is not null;
            case WorkflowAssigneePolicy.PreviousActors:
                return source.PreviousNodeKey is not null;
            case WorkflowAssigneePolicy.SpecifiedUsers:
                return await ValidateUsersAsync(source.UserIds, scope, cancellationToken)
                    .ConfigureAwait(false);
            case WorkflowAssigneePolicy.Initiator:
            case WorkflowAssigneePolicy.InitiatorPrimaryUnitLeader:
            case WorkflowAssigneePolicy.InitiatorDirectManager:
            case WorkflowAssigneePolicy.InitiatorAncestorUnitLeader:
            case WorkflowAssigneePolicy.InitiatorLeaderChain:
                return scope.TenantId.HasValue || source.ResolverKindKey == WorkflowAssigneePolicy.Initiator;
            case WorkflowAssigneePolicy.PositionMembers:
            {
                if (!scope.TenantId.HasValue || source.PositionIds is not { Count: >= 1 and <= 5 } positions) return false;
                var members = await unitLeaderDirectory.FindActivePositionMemberUserIdsAsync(positions, 21, cancellationToken).ConfigureAwait(false);
                var users = positions.Where(members.ContainsKey).SelectMany(id => members[id]).Distinct().Take(21).ToArray();
                return positions.All(members.ContainsKey) && (allowEmpty && users.Length == 0 || await ValidateUsersAsync(users, scope, cancellationToken).ConfigureAwait(false));
            }
            case WorkflowAssigneePolicy.OrganizationUnitMembers:
            {
                if (!scope.TenantId.HasValue || source.UnitId is not { } membersUnitId) return false;
                var members = await unitLeaderDirectory.FindActiveUnitMemberUserIdsAsync([membersUnitId], 21, cancellationToken).ConfigureAwait(false);
                return members.TryGetValue(membersUnitId, out var users) && (allowEmpty && users.Count == 0 || await ValidateUsersAsync(users, scope, cancellationToken).ConfigureAwait(false));
            }
            case WorkflowAssigneePolicy.RoleMembers:
                return await ValidateRolesAsync(source.RoleIds, cancellationToken, allowEmpty).ConfigureAwait(false);
            case WorkflowAssigneePolicy.OrganizationUnitLeader:
                return scope.TenantId.HasValue &&
                    source.UnitId is { } unitId &&
                    await ValidateUnitLeaderAsync(unitId, scope, cancellationToken, allowEmpty).ConfigureAwait(false);
            default:
                return false;
        }
    }

    /// <summary>校验 Host 作用域是否允许使用该来源键。</summary>
    /// <param name="resolverKindKey">办理人来源键。</param>
    /// <param name="scope">发布作用域。</param>
    /// <returns>来源键与作用域兼容时返回 <see langword="true"/>。</returns>
    public static bool IsScopeCompatible(string resolverKindKey, WorkflowManagementScope scope) =>
        scope.TenantId.HasValue ||
        resolverKindKey is WorkflowAssigneePolicy.SpecifiedUsers
            or WorkflowAssigneePolicy.FlowAdministrators
            or WorkflowAssigneePolicy.RoleMembers
            or WorkflowAssigneePolicy.Initiator or WorkflowAssigneePolicy.FormUsers or WorkflowAssigneePolicy.PreviousActors or WorkflowAssigneePolicy.StarterSelectedUsers;

    /// <summary>批量校验指定用户是否仍处于活动状态。</summary>
    /// <param name="userIds">用户标识集合。</param>
    /// <param name="scope">发布作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全部用户有效时返回 <see langword="true"/>。</returns>
    private async Task<bool> ValidateUsersAsync(
        IReadOnlyList<Guid> userIds,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken)
    {
        if (userIds.Count is < 1 or > 20)
        {
            return false;
        }

        if (scope.TenantId.HasValue)
        {
            var users = await tenantUserDirectory
                .FindActiveTenantMembersAsync(userIds, cancellationToken)
                .ConfigureAwait(false);
            return users.Count == userIds.Count;
        }

        var hostUsers = await hostUserDirectory
            .FindActiveHostUsersAsync(userIds, cancellationToken)
            .ConfigureAwait(false);
        return hostUsers.Count == userIds.Count;
    }

    /// <summary>校验角色存在且至少有一名活动成员。</summary>
    /// <param name="roleIds">角色标识集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全部角色有效且可解析成员时返回 <see langword="true"/>。</returns>
    private async Task<bool> ValidateRolesAsync(
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken,
        bool allowEmpty = false)
    {
        if (roleIds.Count is < 1 or > 5)
        {
            return false;
        }

        var roles = await roleMemberDirectory
            .FindActiveRolesAsync(roleIds, cancellationToken)
            .ConfigureAwait(false);
        if (roles.Count != roleIds.Count)
        {
            return false;
        }
        if (allowEmpty) return true;

        var members = await roleMemberDirectory
            .FindActiveMemberUserIdsByRoleIdsAsync(roleIds, 1, cancellationToken)
            .ConfigureAwait(false);
        return roleIds.All(roleId =>
            members.TryGetValue(roleId, out var userIds) && userIds.Count > 0);
    }

    /// <summary>校验机构单元存在且可解析负责人。</summary>
    /// <param name="unitId">机构单元标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>单元与负责人均可解析时返回 <see langword="true"/>。</returns>
    private async Task<bool> ValidateUnitLeaderAsync(
        Guid unitId,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken,
        bool allowEmpty)
    {
        var resolved = await unitLeaderDirectory.ResolveLeaderSourceAsync(Guid.Empty,
            new(WorkflowOrganizationLeaderSourceKind.Unit, unitId), cancellationToken).ConfigureAwait(false);
        if (resolved is null || !resolved.SourceIsValid || resolved.Leaders is null) return false;
        var users = resolved.Leaders.Select(leader => leader.UserId).Distinct().ToArray();
        return users.Length == 0 ? allowEmpty : await ValidateUsersAsync(users, scope, cancellationToken).ConfigureAwait(false);
    }
}
