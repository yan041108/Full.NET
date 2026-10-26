using System.Text.Json;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>调用方必须在Workflow本地事务外准备；目录仅使用当前受信作用域，返回溢出哨兵也必须整体拒绝。</summary>
internal sealed class WorkflowMultiInstanceSourcePreparation(IWorkflowRoleMemberDirectory roles)
{
    public async Task<WorkflowMultiInstancePreparedSource?> PrepareAsync(WorkflowMultiInstancePolicy policy,
        IReadOnlyDictionary<string, JsonElement> input, CancellationToken token)
    {
        IReadOnlyList<Guid>? members = null;
        if (policy.SourceKey == "role_members")
        {
            if (policy.RoleId is not { } roleId) return null;
            var found = await roles.FindActiveMemberUserIdsByRoleIdsAsync([roleId], policy.MaximumCount + 1, token).ConfigureAwait(false);
            if (found.Count != 1 || !found.TryGetValue(roleId, out members)) return null;
        }
        if (!policy.TryFreeze(input, members, out var items)) return null;
        return new(items, policy.RoleId, members?.OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray() ?? []);
    }
}

/// <summary>只保存来源事实；实际持久化仍须在父锁下复核同一次激活和输入修订。</summary>
internal sealed record WorkflowMultiInstancePreparedSource(IReadOnlyList<WorkflowMultiInstanceSeed> Items, Guid? RoleId,
    IReadOnlyList<Guid> RoleMembers);
