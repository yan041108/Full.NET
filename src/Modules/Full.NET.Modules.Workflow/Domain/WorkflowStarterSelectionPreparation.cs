using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>自选成员必须同时满足固定发布范围和当前可信身份；角色交集按并集批量读取，不截断完整名册。</summary>
internal sealed class WorkflowStarterSelectionPreparation(IHostUserBatchSelectionDirectory hosts, ITenantMemberSelectionDirectory members,
    IWorkflowRoleMemberDirectory roles)
{
    public static bool TryNormalize(IReadOnlyDictionary<string, IReadOnlyList<Guid>>? input,
        out IReadOnlyDictionary<string, IReadOnlyList<Guid>> normalized)
    {
        var values = new SortedDictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal);
        normalized = values;
        if (input is null) return true;
        if (input.Count > WorkflowStarterSelectionPolicy.MaximumSelections) return false;
        var total = 0;
        foreach (var pair in input)
        {
            if (!WorkflowNodeKeyValidator.IsValid(pair.Key) || pair.Value is null || pair.Value.Count is < 1 or > 20 ||
                pair.Value.Contains(Guid.Empty) || pair.Value.Distinct().Count() != pair.Value.Count ||
                (total += pair.Value.Count) > WorkflowStarterSelectionPolicy.MaximumTotalSelectedUsers) return false;
            values.Add(pair.Key, pair.Value.ToArray());
        }
        return true;
    }

    public async Task<Result<WorkflowStarterSelectionResolution>> ResolveAsync(IReadOnlyList<WorkflowStarterSelectionPolicy> policies,
        IReadOnlyDictionary<string, IReadOnlyList<Guid>>? choices, WorkflowManagementScope scope, CancellationToken token, bool requireExactKeys)
    {
        if (!TryNormalize(choices, out var selected) || policies.Count > WorkflowStarterSelectionPolicy.MaximumSelections ||
            policies.Select(policy => policy.SelectionKey).Distinct(StringComparer.Ordinal).Count() != policies.Count ||
            requireExactKeys && (selected.Count != policies.Count || selected.Keys.Except(policies.Select(policy => policy.SelectionKey), StringComparer.Ordinal).Any())) return Invalid();
        var active = new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal);
        foreach (var policy in policies)
        {
            if (!selected.TryGetValue(policy.SelectionKey, out var users) || users.Count > policy.MaximumUsers || policy.ModeKey == "single" && users.Count != 1 ||
                policy.Range.KindKey == "specified_users" && users.Any(user => !policy.Range.UserIds.Contains(user))) return Invalid();
            active.Add(policy.SelectionKey, users);
        }
        var union = active.Values.SelectMany(users => users).Distinct().ToArray();
        var found = new HashSet<Guid>();
        foreach (var batch in union.Chunk(100))
        {
            if (scope.TenantId is null) found.UnionWith((await hosts.FindActiveHostUsersAsync(batch, token).ConfigureAwait(false)).Keys);
            else found.UnionWith((await members.FindActiveTenantMembersAsync(batch, token).ConfigureAwait(false)).Keys);
        }
        if (union.Any(user => !found.Contains(user))) return Invalid();
        var roleIds = policies.SelectMany(policy => policy.Range.RoleIds).Distinct().ToArray();
        var memberships = roleIds.Length == 0 ? new Dictionary<Guid, IReadOnlyList<Guid>>()
            : await roles.FindSelectedActiveMembersByRoleIdsAsync(roleIds, union, token).ConfigureAwait(false);
        var basis = new Dictionary<string, IReadOnlyList<WorkflowAssigneeMembershipBasis>>(StringComparer.Ordinal);
        foreach (var policy in policies)
        {
            var facts = new List<WorkflowAssigneeMembershipBasis>();
            if (policy.Range.KindKey == "role_members")
            {
                foreach (var user in active[policy.SelectionKey])
                {
                    var matching = policy.Range.RoleIds.Where(role => memberships.TryGetValue(role, out var users) && users.Contains(user)).ToArray();
                    if (matching.Length == 0) return Invalid();
                    facts.AddRange(matching.Select(role => new WorkflowAssigneeMembershipBasis(role, user)));
                }
            }
            basis.Add(policy.SelectionKey, facts);
        }
        var snapshot = new WorkflowStarterSelectionSnapshot(1, selected);
        var json = JsonSerializer.Serialize(snapshot, WorkflowJsonSerializerContext.Default.WorkflowStarterSelectionSnapshot);
        if (Encoding.UTF8.GetByteCount(json) > 32768) return Invalid();
        return Result<WorkflowStarterSelectionResolution>.Success(new(snapshot, basis, json,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)))));
    }

    private static Result<WorkflowStarterSelectionResolution> Invalid() => Result<WorkflowStarterSelectionResolution>.Failure(
        new Error(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid, "Starter selections do not satisfy the published range and current identity scope.", ErrorType.Validation));
}

internal sealed record WorkflowStarterSelectionResolution(WorkflowStarterSelectionSnapshot Snapshot,
    IReadOnlyDictionary<string, IReadOnlyList<WorkflowAssigneeMembershipBasis>> Memberships, string Json, string Hash);
