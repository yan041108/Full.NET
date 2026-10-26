using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Features;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>发起人目录解析必须在父事务外一次批量完成；保存前还须复核原激活与来源快照。</summary>
internal sealed class WorkflowSubprocessStarterPreparation(IHostUserBatchSelectionDirectory hostUsers,
    ITenantMemberSelectionDirectory tenantMembers, ICurrentTenant currentTenant)
{
    public async Task<WorkflowSubprocessPreparedStarters?> PrepareAsync(WorkflowSubprocessStarterPolicy? policy, Guid parentStarter,
        IReadOnlyDictionary<string, JsonElement> input, WorkflowMultiInstancePreparedSource source, WorkflowManagementScope scope, CancellationToken token)
    {
        if (WorkflowManagementScope.Resolve(currentTenant) != scope || parentStarter == Guid.Empty || source.Items.Count > WorkflowMultiInstancePolicy.MaximumItems) return null;
        var starters = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var seed in source.Items)
        {
            var starter = parentStarter;
            if (policy is not null && !policy.TryResolve(parentStarter, input, seed, out starter) ||
                policy?.SourceKey == "item_user" && (source.RoleId is null || !source.RoleMembers.Contains(starter)) ||
                !starters.TryAdd(seed.ItemKey, starter)) return null;
        }
        var users = starters.Values.Distinct().OrderBy(user => user.ToString("D"), StringComparer.Ordinal).ToArray();
        if (users.Length == 0) return new(starters);
        var active = scope.TenantId is null ? (await hostUsers.FindActiveHostUsersAsync(users, token).ConfigureAwait(false)).Keys.ToHashSet()
            : (await tenantMembers.FindActiveTenantMembersAsync(users, token).ConfigureAwait(false)).Keys.ToHashSet();
        return active.SetEquals(users) ? new(starters) : null;
    }
}

internal sealed record WorkflowSubprocessPreparedStarters(IReadOnlyDictionary<string, Guid> ByItemKey);
