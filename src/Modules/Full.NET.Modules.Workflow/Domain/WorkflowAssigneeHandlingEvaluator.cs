using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>先区分正常空来源与错误，再按固定顺序处理本人和替补；不在此伪造人工票或推进实例。</summary>
internal static class WorkflowAssigneeHandlingEvaluator
{
    public static async Task<Result<WorkflowHandledAssignees>> EvaluateAsync(WorkflowAssigneeResolver resolver,
        WorkflowAssigneePolicy policy, IReadOnlyList<Guid> explicitUsers, WorkflowManagementScope scope, Guid initiator,
        WorkflowAssigneeResolutionCapture capture, CancellationToken token, WorkflowAssigneeRuntimeContext? context = null,
        bool validateIdentities = true)
    {
        var handling = policy.Handling ?? WorkflowAssigneeHandlingPolicy.Default;
        var observed = new HashSet<Guid>(); var attempts = 0;
        var containsInitiator = policy.Sources.Any(source => source.ResolverKindKey == WorkflowAssigneePolicy.Initiator);
        var main = await resolver.CollectAsync(policy, explicitUsers, scope, initiator, token, capture, context,
            allowEmpty: policy.Handling is not null).ConfigureAwait(false);
        if (!main.IsSuccess) return Result<WorkflowHandledAssignees>.Failure(main.Error!);
        var original = main.Value!; observed.UnionWith(original);
        IReadOnlyList<Guid> members = original;
        WorkflowAssigneeHandlingDisposition? disposition = null;
        if (members.Count == 0)
        {
            switch (handling.WhenEmpty.ActionKey)
            {
                case "error": return Invalid();
                case "suspend": case "auto_pass": case "auto_reject":
                    disposition = new(handling.WhenEmpty.ActionKey, "empty"); break;
                case "assign":
                    var fallback = await FallbackAsync(handling.WhenEmpty, "empty", false).ConfigureAwait(false);
                    if (!fallback.IsSuccess) return Result<WorkflowHandledAssignees>.Failure(fallback.Error!);
                    members = fallback.Value!;
                    if (members.Count == 0) disposition = new("suspend", "fallback_exhausted");
                    break;
                default: return Invalid();
            }
        }
        if (disposition is null && members.Contains(initiator))
        {
            switch (handling.WhenInitiator.ActionKey)
            {
                case "keep": break;
                case "skip":
                    members = members.Where(user => user != initiator).ToArray();
                    if (members.Count == 0) disposition = new("skip", "initiator");
                    break;
                case "assign":
                    var fallback = await FallbackAsync(handling.WhenInitiator, "initiator", true).ConfigureAwait(false);
                    if (!fallback.IsSuccess) return Result<WorkflowHandledAssignees>.Failure(fallback.Error!);
                    if (fallback.Value!.Count == 0) disposition = new("suspend", "fallback_exhausted");
                    else members = MergeOrdered(members.Where(user => user != initiator), fallback.Value);
                    break;
                default: return Invalid();
            }
        }
        if (members.Count > 20 || disposition is null && members.Count == 0) return Invalid();
        var observations = observed.ToArray();
        if (validateIdentities && !await resolver.ValidateObservedAsync(observations, scope, initiator, true, containsInitiator, token).ConfigureAwait(false)) return Invalid();
        return Result<WorkflowHandledAssignees>.Success(new(members, original, observations, containsInitiator, disposition));

        async Task<Result<IReadOnlyList<Guid>>> FallbackAsync(WorkflowAssigneeHandlingAction action, string phase, bool excludeInitiator)
        {
            for (var index = 0; index < action.FallbackPolicies.Count && attempts < WorkflowAssigneeHandlingPolicy.MaximumFallbackAttempts; index++)
            {
                attempts++;
                var candidatePolicy = action.FallbackPolicies[index]; var candidateCapture = new WorkflowAssigneeResolutionCapture();
                var candidate = await resolver.CollectAsync(candidatePolicy, [], scope, initiator, token, candidateCapture, context, allowEmpty: true).ConfigureAwait(false);
                if (!candidate.IsSuccess) return candidate;
                containsInitiator |= candidatePolicy.Sources.Any(source => source.ResolverKindKey == WorkflowAssigneePolicy.Initiator);
                observed.UnionWith(candidate.Value!); capture.AddFallback(candidateCapture, phase, index);
                var selected = excludeInitiator ? candidate.Value!.Where(user => user != initiator).ToArray() : candidate.Value!;
                if (selected.Count > 0) return Result<IReadOnlyList<Guid>>.Success(selected);
            }
            return Result<IReadOnlyList<Guid>>.Success([]);
        }
    }

    private static IReadOnlyList<Guid> MergeOrdered(IEnumerable<Guid> first, IEnumerable<Guid> second)
    {
        var result = new List<Guid>(); var seen = new HashSet<Guid>();
        foreach (var user in first.Concat(second)) if (seen.Add(user)) result.Add(user);
        return result;
    }
    private static Result<WorkflowHandledAssignees> Invalid() => Result<WorkflowHandledAssignees>.Failure(new(
        WorkflowErrorCodes.DefinitionAssigneePolicyInvalid, "The declared assignee handling policy could not be resolved.", ErrorType.Validation));
}

/// <summary>处置结果保留原成员和全部观察身份；组组合先合并观察集合再一次校验，不能跳过被排除人员的身份错误。</summary>
internal sealed record WorkflowHandledAssignees(IReadOnlyList<Guid> Members, IReadOnlyList<Guid> OriginalMembers,
    IReadOnlyList<Guid> ObservedMembers, bool ContainsInitiatorSource, WorkflowAssigneeHandlingDisposition? Disposition);
/// <summary>自动处置是独立的系统裁定，不代表任何真实人员提交了意见或票据。</summary>
internal sealed record WorkflowAssigneeHandlingDisposition(string ActionKey, string TriggerKey);
