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
    ITenantMemberSelectionDirectory tenantUserDirectory,
    IWorkflowRoleMemberDirectory roleMemberDirectory,
    IWorkflowOrganizationAssigneeDirectory unitLeaderDirectory)
{
    private const int MaximumResolvedUsers = 20;

    /// <summary>解析办理人策略并合并显式多人审批用户，返回去重后的活动用户标识。</summary>
    /// <param name="assigneePolicy">节点固化的办理人解析策略。</param>
    /// <param name="explicitApproverUserIds">多人审批策略中的显式用户标识。</param>
    /// <param name="scope">当前可信工作流管理作用域。</param>
    /// <param name="initiatorUserId">流程发起人或当前推进上下文的用户标识。</param>
    /// <param name="cancellationToken">取消当前解析的令牌。</param>
    /// <param name="allowStoredHostInitiator">仅运行时实例事实允许校验原 Host 发起人；设计器候选预览不得启用。</param>
    /// <param name="capture">可选的本次解析依据收集器；只在全部校验成功后用于激活。</param>
    /// <returns>稳定排序后的活动用户标识；解析失败时返回业务错误。</returns>
    public async Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
        WorkflowAssigneePolicy assigneePolicy,
        IReadOnlyList<Guid> explicitApproverUserIds,
        WorkflowManagementScope scope,
        Guid initiatorUserId,
        CancellationToken cancellationToken = default,
        bool allowStoredHostInitiator = false,
        WorkflowAssigneeResolutionCapture? capture = null,
        WorkflowAssigneeRuntimeContext? context = null,
        bool allowEmpty = false)
    {
        var collected = await CollectAsync(assigneePolicy, explicitApproverUserIds, scope, initiatorUserId,
            cancellationToken, capture, context, allowEmpty).ConfigureAwait(false);
        if (!collected.IsSuccess) return collected;
        if (allowEmpty && collected.Value!.Count == 0) return collected;
        return await ValidateResolvedAsync(collected.Value!, scope, initiatorUserId, allowStoredHostInitiator,
            assigneePolicy.Sources.Any(source => source.ResolverKindKey == WorkflowAssigneePolicy.Initiator), cancellationToken).ConfigureAwait(false)
            ? collected : Failure();
    }

    /// <summary>仅收集来源，结果尚非可信激活成员；组合组必须对最终并集调用批量活动成员验证。</summary>
    internal async Task<Result<IReadOnlyList<Guid>>> CollectAsync(WorkflowAssigneePolicy assigneePolicy,
        IReadOnlyList<Guid> explicitApproverUserIds, WorkflowManagementScope scope, Guid initiatorUserId,
        CancellationToken cancellationToken, WorkflowAssigneeResolutionCapture? capture, WorkflowAssigneeRuntimeContext? context = null,
        bool allowEmpty = false)
    {
        var resolved = new List<Guid>();
        var seen = new HashSet<Guid>();
        var sourceIndex = 0;
        foreach (var source in assigneePolicy.Sources)
        {
            var memberships = new List<WorkflowAssigneeMembershipBasis>();
            var sourceResult = await ResolveSourceAsync(
                    source,
                    scope,
                    initiatorUserId,
                    cancellationToken, memberships, context, allowEmpty)
                .ConfigureAwait(false);
            if (!sourceResult.IsSuccess)
            {
                return Result<IReadOnlyList<Guid>>.Failure(sourceResult.Error!);
            }

            foreach (var userId in sourceResult.Value!)
            {
                if (seen.Add(userId))
                {
                    resolved.Add(userId);
                    if (resolved.Count > MaximumResolvedUsers) return Failure();
                }
            }
            capture?.Add(sourceIndex, source, sourceResult.Value!, memberships);
            sourceIndex++;
        }

        foreach (var userId in explicitApproverUserIds)
        {
            if (seen.Add(userId))
            {
                resolved.Add(userId);
                if (resolved.Count > MaximumResolvedUsers) return Failure();
            }
        }

        if (resolved.Count > MaximumResolvedUsers || resolved.Count == 0 && !allowEmpty)
        {
            return Failure();
        }

        capture?.AddExplicit(explicitApproverUserIds);
        return Result<IReadOnlyList<Guid>>.Success(resolved);
    }

    /// <summary>组合活动对最终去重并集一次核对Identity；精确保存的Host发起人例外不能扩大候选。</summary>
    internal async Task<bool> ValidateResolvedAsync(IReadOnlyList<Guid> resolved, WorkflowManagementScope scope,
        Guid initiatorUserId, bool allowStoredHostInitiator, bool containsInitiatorSource, CancellationToken cancellationToken)
    {
        if (resolved.Count is < 1 or > MaximumResolvedUsers || resolved.Distinct().Count() != resolved.Count || resolved.Contains(Guid.Empty)) return false;
        return await ValidateObservedAsync(resolved, scope, initiatorUserId, allowStoredHostInitiator, containsInitiatorSource, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>本人排除和替补前观察到的身份也必须有效，避免把撤销身份伪装成无人而自动放行；组合组按并集批量校验。</summary>
    internal async Task<bool> ValidateObservedAsync(IReadOnlyList<Guid> resolved, WorkflowManagementScope scope,
        Guid initiatorUserId, bool allowStoredHostInitiator, bool containsInitiatorSource, CancellationToken cancellationToken)
    {
        if (resolved.Count > 640 || resolved.Distinct().Count() != resolved.Count || resolved.Contains(Guid.Empty)) return false;
        // Tenant 归属以 Identity 活动成员为准，历史租户角色不能为已移除成员保留办理资格。
        var validUsers = new HashSet<Guid>();
        foreach (var batch in resolved.Chunk(100))
            validUsers.UnionWith((await FindActiveUsersAsync(batch, scope, cancellationToken).ConfigureAwait(false)).Keys);
        // Worker 的发起人来自持久化实例，不是本次认证人。所有来源均复核活动状态，不能靠旧认证跳过目录。
        if (validUsers.Count != resolved.Count)
        {
            // 仅运行时已保存的发起人事实可保留 Host 自办身份；不为预览或指定用户扩大租户候选范围。
            var missing = resolved.Where(id => !validUsers.Contains(id)).ToArray();
            if (!allowStoredHostInitiator || !scope.TenantId.HasValue || missing.Length != 1 ||
                missing[0] != initiatorUserId || initiatorUserId == Guid.Empty ||
                !containsInitiatorSource)
                return false;
            var hostInitiator = await hostUserDirectory.FindActiveHostUsersAsync([initiatorUserId], cancellationToken).ConfigureAwait(false);
            if (!hostInitiator.ContainsKey(initiatorUserId)) return false;
        }

        return true;
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
        CancellationToken cancellationToken,
        List<WorkflowAssigneeMembershipBasis> memberships,
        WorkflowAssigneeRuntimeContext? context, bool allowEmpty)
    {
        switch (source.ResolverKindKey)
        {
            case WorkflowAssigneePolicy.StarterSelectedUsers:
                if (source.StarterSelection is not { } selection || context?.StarterSelections is not { } prepared ||
                    !prepared.Snapshot.Selections.TryGetValue(selection.SelectionKey, out var selected) ||
                    !prepared.Memberships.TryGetValue(selection.SelectionKey, out var selectionBasis)) return Failure();
                memberships.AddRange(selectionBasis);
                return Result<IReadOnlyList<Guid>>.Success(selected);
            case WorkflowAssigneePolicy.FormUsers:
                if (allowEmpty && context is not null && source.FieldKey is { } emptyUserField && IsEmptyField(context.Values, emptyUserField))
                    return Result<IReadOnlyList<Guid>>.Success([]);
                return context is not null && source.FieldKey is { } userField && context.Values.TryGetValue(userField, out var userValue) &&
                    WorkflowAssigneeFieldValues.TryRead(userValue, out var usersFromField)
                    ? Result<IReadOnlyList<Guid>>.Success(usersFromField) : Failure();
            case WorkflowAssigneePolicy.FormUnits:
            {
                if (allowEmpty && scope.TenantId.HasValue && context is not null && source.FieldKey is { } emptyUnitField && IsEmptyField(context.Values, emptyUnitField))
                    return Result<IReadOnlyList<Guid>>.Success([]);
                if (!scope.TenantId.HasValue || context is null || source.FieldKey is not { } unitField ||
                    !context.Values.TryGetValue(unitField, out var unitValue) || !WorkflowAssigneeFieldValues.TryRead(unitValue, out var units) || units.Count > 8) return Failure();
                var membersByUnit = await unitLeaderDirectory.FindActiveUnitMemberUserIdsAsync(units, MaximumResolvedUsers + 1, cancellationToken).ConfigureAwait(false);
                if (units.Any(unit => !membersByUnit.ContainsKey(unit))) return Failure();
                foreach (var unit in units) memberships.AddRange(membersByUnit[unit].Order().Select(user => new WorkflowAssigneeMembershipBasis(unit, user)));
                return Result<IReadOnlyList<Guid>>.Success(units.SelectMany(unit => membersByUnit[unit].Order()).Distinct().Take(MaximumResolvedUsers + 1).ToArray());
            }
            case WorkflowAssigneePolicy.PreviousActors:
                if (context is null || source.PreviousNodeKey is not { } previousKey || !context.PreviousActors.TryGetValue(previousKey, out var actors) ||
                    actors.Count is < 1 or > MaximumResolvedUsers) return Failure();
                memberships.AddRange(actors.Select(actor => new WorkflowAssigneeMembershipBasis(actor.StepId, actor.ActorUserId)));
                return Result<IReadOnlyList<Guid>>.Success(actors.Select(actor => actor.ActorUserId).Distinct().ToArray());
            case WorkflowAssigneePolicy.SpecifiedUsers:
                return Result<IReadOnlyList<Guid>>.Success(source.UserIds);
            case WorkflowAssigneePolicy.Initiator:
                return Result<IReadOnlyList<Guid>>.Success([initiatorUserId]);
            case WorkflowAssigneePolicy.RoleMembers:
            {
                var members = await roleMemberDirectory
                    .FindActiveMemberUserIdsByRoleIdsAsync(source.RoleIds, MaximumResolvedUsers + 1, cancellationToken)
                    .ConfigureAwait(false);
                if (source.RoleIds.Any(roleId => !members.ContainsKey(roleId)) ||
                    !allowEmpty && members.Values.All(userIds => userIds.Count == 0))
                {
                    return Failure();
                }

                foreach (var role in source.RoleIds)
                    memberships.AddRange(members[role].Order().Select(user => new WorkflowAssigneeMembershipBasis(role, user)));
                return Result<IReadOnlyList<Guid>>.Success(
                    source.RoleIds.SelectMany(role => members[role].Order()).Distinct().Take(MaximumResolvedUsers + 1).ToArray());
            }
            case WorkflowAssigneePolicy.PositionMembers:
            {
                if (!scope.TenantId.HasValue || source.PositionIds is not { Count: >= 1 and <= 5 } positions) return Failure();
                var members = await unitLeaderDirectory.FindActivePositionMemberUserIdsAsync(positions, MaximumResolvedUsers+1, cancellationToken).ConfigureAwait(false);
                if (positions.Any(id => !members.ContainsKey(id)) || !allowEmpty && members.Values.All(ids => ids.Count == 0)) return Failure();
                foreach (var position in positions)
                    memberships.AddRange(members[position].Order().Select(user => new WorkflowAssigneeMembershipBasis(position, user)));
                return Result<IReadOnlyList<Guid>>.Success(positions.SelectMany(id => members[id].Order()).Distinct().Take(MaximumResolvedUsers+1).ToArray());
            }
            case WorkflowAssigneePolicy.OrganizationUnitMembers:
            {
                if (!scope.TenantId.HasValue || source.UnitId is not { } membersUnitId) return Failure();
                var members = await unitLeaderDirectory.FindActiveUnitMemberUserIdsAsync([membersUnitId], MaximumResolvedUsers+1, cancellationToken).ConfigureAwait(false);
                if (members.TryGetValue(membersUnitId, out var membersForBasis))
                    memberships.AddRange(membersForBasis.Order().Select(user => new WorkflowAssigneeMembershipBasis(membersUnitId, user)));
                return members.TryGetValue(membersUnitId, out var users) && (users.Count > 0 || allowEmpty)
                    ? Result<IReadOnlyList<Guid>>.Success(users.Order().ToArray()) : Failure();
            }
            case WorkflowAssigneePolicy.FlowAdministrators:
                return context?.FlowAdministratorUserIds is { Count: <= 20 } administrators && (allowEmpty || administrators.Count > 0)
                    ? Result<IReadOnlyList<Guid>>.Success(administrators.ToArray()) : Failure();
            case WorkflowAssigneePolicy.InitiatorDirectManager:
            {
                if (!scope.TenantId.HasValue) return Failure();
                var manager = await unitLeaderDirectory.ResolveDirectManagerAsync(initiatorUserId, cancellationToken).ConfigureAwait(false);
                if (manager is null || !manager.SourceIsValid || manager.ManagerUserId == Guid.Empty || manager.ManagerUserId == initiatorUserId) return Failure();
                if (manager.ManagerUserId is null) return allowEmpty ? Result<IReadOnlyList<Guid>>.Success([]) : Failure();
                if (manager.RelationId is null || manager.RelationId == Guid.Empty || manager.Revision < 1) return Failure();
                memberships.Add(new(manager.RelationId.Value, manager.ManagerUserId.Value, SourceRevision: manager.Revision));
                return Result<IReadOnlyList<Guid>>.Success([manager.ManagerUserId.Value]);
            }
            case WorkflowAssigneePolicy.InitiatorLeaderChain:
            case WorkflowAssigneePolicy.OrganizationUnitLeader:
            case WorkflowAssigneePolicy.InitiatorPrimaryUnitLeader:
            case WorkflowAssigneePolicy.InitiatorAncestorUnitLeader:
            {
                if (!scope.TenantId.HasValue || WorkflowOrganizationLeaderSources.Create(source) is not { } request) return Failure();
                var resolved = await unitLeaderDirectory.ResolveLeaderSourceAsync(initiatorUserId, request, cancellationToken).ConfigureAwait(false);
                if (resolved is null || !resolved.SourceIsValid || resolved.Leaders is null || resolved.Leaders.Count > 20 || !allowEmpty && resolved.Leaders.Count == 0)
                    return Failure();
                memberships.AddRange(resolved.Leaders.Select(leader => new WorkflowAssigneeMembershipBasis(leader.UnitId, leader.UserId, leader.Level)));
                return Result<IReadOnlyList<Guid>>.Success(resolved.Leaders.Select(leader => leader.UserId).Distinct().ToArray());
            }
            default:
                return Failure();
        }
    }

    /// <summary>字段已由发布架构绑定；未填、清空及空集合属于正常无人，其他非法值仍须拒绝。</summary>
    private static bool IsEmptyField(IReadOnlyDictionary<string, System.Text.Json.JsonElement> values, string key) =>
        !values.TryGetValue(key, out var value) || value.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined ||
        value.ValueKind == System.Text.Json.JsonValueKind.Array && value.GetArrayLength() == 0 ||
        value.ValueKind == System.Text.Json.JsonValueKind.String && value.GetString() == string.Empty;

    /// <summary>批量校验解析出的身份仍属于当前可信作用域。</summary>
    private async Task<IReadOnlyDictionary<Guid, object>> FindActiveUsersAsync(
        IReadOnlyList<Guid> userIds,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.TenantId.HasValue)
        {
            var users = await tenantUserDirectory
                .FindActiveTenantMembersAsync(userIds, cancellationToken)
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
