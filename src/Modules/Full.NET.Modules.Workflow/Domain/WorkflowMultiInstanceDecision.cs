namespace Full.NET.Modules.Workflow.Domain;

/// <summary>完成门槛只计算成功事实；被选集合一旦固化，迟到成功不能改变输出或重新累加。</summary>
internal static class WorkflowMultiInstanceDecision
{
    public static WorkflowMultiInstanceSelection Evaluate(WorkflowMultiInstancePolicy policy, IReadOnlyList<WorkflowMultiInstanceFact> facts)
    {
        if (facts.Count > policy.MaximumCount || facts.Any(item => item.Id == Guid.Empty || item.SequenceNo < 1 || !WorkflowSubprocessPolicy.Key(item.ItemKey) ||
                item.StatusKey is not ("pending" or "running" or "succeeded" or "rejected" or "cancelled" or "unknown")) ||
            facts.Select(item => item.Id).Distinct().Count() != facts.Count || facts.Select(item => item.ItemKey).Distinct(StringComparer.Ordinal).Count() != facts.Count ||
            !facts.Select(item => item.SequenceNo).Order().SequenceEqual(Enumerable.Range(1, facts.Count))) return new("invalid", [], []);
        if (facts.Count == 0) return new(policy.EmptyModeKey == "skip" ? "select" : "invalid", [], []);
        var successful = facts.Where(item => item.StatusKey == "succeeded").OrderBy(item => item.ItemKey, StringComparer.Ordinal).ToArray();
        var outstanding = facts.Where(item => item.StatusKey is "pending" or "running" or "unknown").OrderBy(item => item.ItemKey, StringComparer.Ordinal).ToArray();
        if (successful.Length >= policy.RequiredSuccesses(facts.Count)) return new("select", successful.Select(item => item.Id).ToArray(), outstanding.Select(item => item.Id).ToArray());
        if (facts.Any(item => item.StatusKey == "unknown")) return new("unknown", [], outstanding.Select(item => item.Id).ToArray());
        return new(successful.Length + outstanding.Length < policy.RequiredSuccesses(facts.Count) ? "impossible" : "wait", [], []);
    }

    public static bool TryReadSealedSelection(WorkflowMultiInstancePolicy policy, IReadOnlyList<Guid> selected, IReadOnlyList<WorkflowMultiInstanceFact> facts,
        out IReadOnlyList<WorkflowMultiInstanceFact> result)
    {
        result = [];
        if (Evaluate(policy, facts).StateKey == "invalid" || selected.Count < policy.RequiredSuccesses(facts.Count) ||
            selected.Count > WorkflowMultiInstancePolicy.MaximumItems || selected.Distinct().Count() != selected.Count ||
            facts.Select(item => item.Id).Distinct().Count() != facts.Count) return false;
        var byId = facts.ToDictionary(item => item.Id); var found = new List<WorkflowMultiInstanceFact>();
        foreach (var id in selected) { if (!byId.TryGetValue(id, out var fact) || fact.StatusKey != "succeeded") return false; found.Add(fact); }
        result = found.OrderBy(item => item.ItemKey, StringComparer.Ordinal).ToArray(); return true;
    }
}

internal sealed record WorkflowMultiInstanceFact(Guid Id, string ItemKey, int SequenceNo, string StatusKey);
internal sealed record WorkflowMultiInstanceSelection(string StateKey, IReadOnlyList<Guid> SelectedIds, IReadOnlyList<Guid> OutstandingIds);
