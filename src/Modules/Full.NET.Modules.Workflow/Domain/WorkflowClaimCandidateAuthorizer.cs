using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Features;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>领取时按指定用户检查当前原来源；不重新截取前二十名替代成员资格证明。</summary>
internal sealed class WorkflowClaimCandidateAuthorizer(WorkflowAssigneeResolver identities,
    IWorkflowRoleMemberDirectory roles, IWorkflowOrganizationAssigneeDirectory organizations, IDataTransactionState transaction)
{
    public async Task<bool> IsCurrentCandidateAsync(WorkflowAssigneePolicy policy, WorkflowManagementScope scope,
        Guid initiator, Guid actor, CancellationToken token, WorkflowAssigneeResolutionSnapshot? frozenResolution = null)
    {
        if (transaction.HasTransaction) throw new InvalidOperationException("Workflow claim authorization must run outside its transaction.");
        if (policy.Handling is not null && (frozenResolution is null || frozenResolution.Handling is not null ||
            frozenResolution.InitiatorUserId != initiator || !frozenResolution.MemberUserIds.Contains(actor))) return false;
        if (!await identities.ValidateResolvedAsync([actor], scope, initiator, true,
            policy.EnumerateReferencedSources().Any(source => source.ResolverKindKey == WorkflowAssigneePolicy.Initiator), token).ConfigureAwait(false)) return false;
        foreach (var (origin, index, source) in Sources(policy))
        {
            // 未采用的替补声明不授予领取资格；成员必须同时属于本次最终池与实际来源快照。
            var captured = frozenResolution?.Sources.Where(item => item.OriginKey == origin && item.SourceIndex == index &&
                SameSource(item.Config, source) && item.UserIds.Contains(actor)).ToArray() ?? [];
            if (policy.Handling is not null && captured.Length == 0) continue;
            if (source.ResolverKindKey is WorkflowAssigneePolicy.FormUsers or WorkflowAssigneePolicy.FormUnits or WorkflowAssigneePolicy.PreviousActors or WorkflowAssigneePolicy.StarterSelectedUsers or WorkflowAssigneePolicy.FlowAdministrators)
            {
                // 动态值固定于激活来源，不用后来的共享表单重算；身份与部门成员关系仍检查当前状态。
                var basis = captured;
                if (source.StarterSelection is { } selection)
                {
                    if (basis.Length == 0) continue;
                    if (selection.Range.KindKey == "all" || selection.Range.KindKey == "specified_users" && selection.Range.UserIds.Contains(actor)) return true;
                    if (selection.Range.KindKey == "role_members" && await roles.IsActiveMemberOfAnyRoleAsync(actor, selection.Range.RoleIds, token).ConfigureAwait(false)) return true;
                    continue;
                }
                if (source.ResolverKindKey != WorkflowAssigneePolicy.FormUnits && basis.Length > 0) return true;
                var units = basis.SelectMany(item => item.Memberships).Where(item => item.UserId == actor).Select(item => item.SourceId).Distinct().ToArray();
                if (scope.TenantId.HasValue && units.Length is > 0 and <= 8 && await organizations.IsActiveMemberOfAnyUnitAsync(actor, units, token).ConfigureAwait(false)) return true;
                continue;
            }
            var matched = source.ResolverKindKey switch
            {
                WorkflowAssigneePolicy.SpecifiedUsers => source.UserIds.Contains(actor),
                WorkflowAssigneePolicy.Initiator => actor == initiator,
                WorkflowAssigneePolicy.InitiatorDirectManager => scope.TenantId.HasValue && await IsCurrentManagerAsync().ConfigureAwait(false),
                WorkflowAssigneePolicy.RoleMembers => await roles.IsActiveMemberOfAnyRoleAsync(actor, source.RoleIds, token).ConfigureAwait(false),
                WorkflowAssigneePolicy.PositionMembers => scope.TenantId.HasValue && source.PositionIds is { } positions &&
                    await organizations.IsActiveMemberOfAnyPositionAsync(actor, positions, token).ConfigureAwait(false),
                WorkflowAssigneePolicy.OrganizationUnitMembers => scope.TenantId.HasValue && source.UnitId is { } unit &&
                    await organizations.IsActiveMemberOfAnyUnitAsync(actor, [unit], token).ConfigureAwait(false),
                WorkflowAssigneePolicy.OrganizationUnitLeader or WorkflowAssigneePolicy.InitiatorPrimaryUnitLeader or
                WorkflowAssigneePolicy.InitiatorAncestorUnitLeader or WorkflowAssigneePolicy.InitiatorLeaderChain =>
                    scope.TenantId.HasValue && await IsCurrentLeaderAsync(source).ConfigureAwait(false),
                _ => false,
            };
            if (matched) return true;
        }
        return false;

        async Task<bool> IsCurrentLeaderAsync(WorkflowAssigneeSource source)
        {
            if (WorkflowOrganizationLeaderSources.Create(source) is not { } request) return false;
            var resolution = await organizations.ResolveLeaderSourceAsync(initiator, request, token).ConfigureAwait(false);
            return resolution is { SourceIsValid: true, Leaders: not null } && resolution.Leaders.Any(leader => leader.UserId == actor);
        }
        async Task<bool> IsCurrentManagerAsync()
        {
            var resolution = await organizations.ResolveDirectManagerAsync(initiator, token).ConfigureAwait(false);
            return resolution is { SourceIsValid: true, RelationId: not null, Revision: > 0 } &&
                resolution.RelationId != Guid.Empty && resolution.ManagerUserId == actor && actor != initiator;
        }
    }

    private static IEnumerable<(string Origin, int Index, WorkflowAssigneeSource Source)> Sources(WorkflowAssigneePolicy policy)
    {
        for (var index = 0; index < policy.Sources.Count; index++) yield return ("assignee_policy", index, policy.Sources[index]);
        if (policy.Handling is not { } handling) yield break;
        foreach (var (phase, action) in new[] { ("empty", handling.WhenEmpty), ("initiator", handling.WhenInitiator) })
            for (var attempt = 0; attempt < action.FallbackPolicies.Count; attempt++)
                for (var index = 0; index < action.FallbackPolicies[attempt].Sources.Count; index++)
                    yield return ($"handling.{phase}.{attempt}", index, action.FallbackPolicies[attempt].Sources[index]);
    }

    private static bool SameSource(WorkflowAssigneeSource first, WorkflowAssigneeSource second) =>
        first.ResolverKindKey == second.ResolverKindKey && first.FieldKey == second.FieldKey && first.PreviousNodeKey == second.PreviousNodeKey &&
        first.UnitId == second.UnitId && first.AncestorLevel == second.AncestorLevel &&
        first.UserIds.SequenceEqual(second.UserIds) && first.RoleIds.SequenceEqual(second.RoleIds) &&
        (first.PositionIds ?? []).SequenceEqual(second.PositionIds ?? []) && SameSelection(first.StarterSelection, second.StarterSelection);

    private static bool SameSelection(WorkflowStarterSelectionPolicy? first, WorkflowStarterSelectionPolicy? second) =>
        first is null ? second is null : second is not null && first.SelectionKey == second.SelectionKey && first.ModeKey == second.ModeKey &&
        first.MaximumUsers == second.MaximumUsers && first.Range.KindKey == second.Range.KindKey &&
        first.Range.UserIds.SequenceEqual(second.Range.UserIds) && first.Range.RoleIds.SequenceEqual(second.Range.RoleIds);
}
