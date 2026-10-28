using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>退回从原激活快照恢复席位；只复核人员资格，不按当前组织目录重新展开来源或继承旧票。</summary>
internal static class WorkflowReturnAssigneeSnapshot
{
    public static bool TryRestore(string nodeType, JsonElement configuration, Guid initiator, string? json, string? hash,
        out WorkflowHumanActivityAssignees? assignees)
    {
        assignees = null;
        if (initiator == Guid.Empty || json is null || hash is null || Encoding.UTF8.GetByteCount(json) > 128 * 1024 ||
            hash != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)))) return false;
        WorkflowAssigneeResolutionSnapshot? snapshot;
        try { snapshot = JsonSerializer.Deserialize(json, WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot); }
        catch (JsonException) { return false; }
        if (!Valid(snapshot, initiator) || snapshot!.MemberUserIds.Count is < 1 or > 20 || snapshot.Handling is not null) return false;
        var members = snapshot.MemberUserIds.ToArray();
        if (nodeType == "human.task")
        {
            if (snapshot.SchemaVersion != 1 || snapshot.Groups is not null || !WorkflowTaskPolicy.TryRead(configuration, out var task)) return false;
            assignees = new(null, Guid.Empty, json, hash, Task: new(task!, members));
            return true;
        }
        if (nodeType is not ("human.approval" or "human.group_approval") ||
            !WorkflowApprovalPolicy.TryRead(configuration, out var policy)) return false;
        if (policy?.GroupConfiguration is { } groups)
        {
            if (nodeType != "human.group_approval" || snapshot.SchemaVersion != 2 || snapshot.Groups is null ||
                snapshot.Groups.Count != groups.Groups.Count || snapshot.Groups.Count is < 2 or > 8) return false;
            var restored = new List<WorkflowApprovalGroupAssignees>();
            for (var index = 0; index < groups.Groups.Count; index++)
            {
                var definition = groups.Groups[index]; var original = snapshot.Groups[index];
                if (original is null || original.GroupKey != definition.GroupKey || original.GroupName != definition.GroupName ||
                    !Valid(original.Resolution, initiator) || original.Resolution.SchemaVersion != 1 || original.Resolution.Groups is not null) return false;
                var resolution = original.Resolution;
                var handling = resolution.Handling;
                if (handling is not null)
                {
                    if (handling.ActionKey is not ("auto_pass" or "skip") || resolution.MemberUserIds.Count != 0) return false;
                    restored.Add(new(definition.GroupKey, definition.GroupName, new("system", [], 0), handling));
                    continue;
                }
                var groupMembers = resolution.MemberUserIds.ToArray();
                var groupPolicy = definition.ApprovalPolicy ?? new WorkflowApprovalPolicy("single", [], 1);
                var required = groupPolicy.ResolveRequiredCount(groupMembers.Length);
                if (required == 0) return false;
                restored.Add(new(definition.GroupKey, definition.GroupName, groupPolicy with { ApproverUserIds = groupMembers, RequiredApprovals = required }));
            }
            if (!restored.SelectMany(group => group.Policy.ApproverUserIds).SequenceEqual(members) ||
                restored.Count(group => group.Handling is not null) >= groups.RequiredGroups) return false;
            assignees = new(policy with { ApproverUserIds = members, RequiredApprovals = groups.RequiredGroups, Groups = restored }, Guid.Empty, json, hash);
            return true;
        }
        if (nodeType != "human.approval" || snapshot.SchemaVersion != 1 || snapshot.Groups is not null) return false;
        if (policy is null)
        {
            if (members.Length != 1) return false;
            assignees = new(null, members[0], json, hash);
            return true;
        }
        var requiredCount = policy.ResolveRequiredCount(members.Length);
        if (requiredCount == 0) return false;
        assignees = new(policy with { ApproverUserIds = members, RequiredApprovals = requiredCount }, Guid.Empty, json, hash);
        return true;
    }

    private static bool Valid(WorkflowAssigneeResolutionSnapshot? snapshot, Guid initiator) => snapshot is not null &&
        snapshot.SchemaVersion is 1 or 2 && snapshot.InitiatorUserId == initiator && snapshot.MemberUserIds is { Count: <= 20 } members &&
        members.All(id => id != Guid.Empty) && members.Distinct().Count() == members.Count && snapshot.Sources is { Count: <= 64 };
}
