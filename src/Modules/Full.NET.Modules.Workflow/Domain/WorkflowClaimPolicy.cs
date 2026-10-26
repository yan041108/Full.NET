using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>抢单先保存候选池，是否允许释放由发布策略固定；循环预算不能由请求放宽。</summary>
internal sealed record WorkflowClaimPolicy(bool AllowRelease)
{
    internal const int MaximumClaimCycles = 32;
    internal const int MaximumCandidates = 20;

    public static bool TryRead(JsonElement config, out WorkflowClaimPolicy? policy)
    {
        policy = null;
        if (config.ValueKind != JsonValueKind.Object || config.EnumerateObject().GroupBy(item => item.Name, StringComparer.Ordinal).Any(group => group.Count() > 1) ||
            !config.TryGetProperty("claimPolicy", out var element) ||
            element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Count() != 1 ||
            !element.TryGetProperty("allowRelease", out var release) || release.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        policy = new(release.GetBoolean());
        return true;
    }
}

/// <summary>到达时冻结的候选与来源快照，领取时仍需检查当前来源成员资格。</summary>
internal sealed record WorkflowClaimActivationPolicy(WorkflowClaimPolicy Policy, IReadOnlyList<Guid> MemberUserIds);

/// <summary>本次实际授权来源必须与到达时固定候选取交集，旧角色或已离职成员不能仅凭旧快照领取。</summary>
internal static class WorkflowClaimRules
{
    public static bool CanClaim(Guid actor, IReadOnlyCollection<Guid> frozen, IReadOnlyCollection<Guid> current,
        string status, long revision, long expectedRevision, int cycle) =>
        actor != Guid.Empty && status == "available" && revision >= 1 && revision == expectedRevision &&
        cycle is >= 0 and < WorkflowClaimPolicy.MaximumClaimCycles &&
        frozen.Count is >= 1 and <= WorkflowClaimPolicy.MaximumCandidates && frozen.Distinct().Count() == frozen.Count &&
        !frozen.Contains(Guid.Empty) && frozen.Contains(actor) && current.Contains(actor);

    public static bool CanRelease(WorkflowClaimPolicy policy, string status, Guid actor, Guid? assignee,
        int cycle, long revision, long expectedRevision) => policy.AllowRelease && status == "claimed" &&
        actor != Guid.Empty && actor == assignee && cycle is >= 1 and < WorkflowClaimPolicy.MaximumClaimCycles &&
        revision >= 1 && revision == expectedRevision;
}
