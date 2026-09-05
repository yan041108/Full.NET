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
    ITenantUserSelectionDirectory tenantUserDirectory,
    IWorkflowRoleMemberDirectory roleMemberDirectory,
    IWorkflowUnitLeaderDirectory unitLeaderDirectory)
{
    /// <summary>校验单条办理人来源在发布作用域内可解析。</summary>
    /// <param name="source">固化来源配置。</param>
    /// <param name="scope">发布作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>来源闭合且实体有效时返回 <see langword="true"/>。</returns>
    public async Task<bool> ValidateSourceAsync(
        WorkflowAssigneeSource source,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken)
    {
        switch (source.ResolverKindKey)
        {
            case WorkflowAssigneePolicy.SpecifiedUsers:
                return await ValidateUsersAsync(source.UserIds, scope, cancellationToken)
                    .ConfigureAwait(false);
            case WorkflowAssigneePolicy.Initiator:
            case WorkflowAssigneePolicy.InitiatorPrimaryUnitLeader:
                return scope.TenantId.HasValue || source.ResolverKindKey == WorkflowAssigneePolicy.Initiator;
            case WorkflowAssigneePolicy.RoleMembers:
                return await ValidateRolesAsync(source.RoleIds, cancellationToken).ConfigureAwait(false);
            case WorkflowAssigneePolicy.OrganizationUnitLeader:
                return scope.TenantId.HasValue &&
                    source.UnitId is { } unitId &&
                    await ValidateUnitLeaderAsync(unitId, cancellationToken).ConfigureAwait(false);
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
            or WorkflowAssigneePolicy.RoleMembers
            or WorkflowAssigneePolicy.Initiator;

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
                .FindActiveTenantUsersAsync(userIds, cancellationToken)
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
        CancellationToken cancellationToken)
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

        var members = await roleMemberDirectory
            .FindActiveMemberUserIdsByRoleIdsAsync(roleIds, cancellationToken)
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
        CancellationToken cancellationToken)
    {
        var units = await unitLeaderDirectory
            .FindActiveUnitsAsync([unitId], cancellationToken)
            .ConfigureAwait(false);
        if (!units.ContainsKey(unitId))
        {
            return false;
        }

        var leaders = await unitLeaderDirectory
            .FindActiveUnitLeaderUserIdsAsync([unitId], cancellationToken)
            .ConfigureAwait(false);
        return leaders.ContainsKey(unitId);
    }
}
