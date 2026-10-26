namespace Full.NET.Modules.Workflow.Domain;

/// <summary>动态来源在发布时绑定字段和先行活动，不把未选中的分支或任意文本当成办理身份。</summary>
internal static class WorkflowDynamicAssigneeDefinitionRules
{
    public static IEnumerable<WorkflowAssigneeSource> Sources(WorkflowNodeDraft node)
    {
        if (node.NodeTypeKey == "notify.cc") return WorkflowCcNodeConfiguration.TryReadPolicy(node.Config, out var recipients) ? recipients.Sources : [];
        if (!WorkflowHumanActivityKinds.IsFormActivity(node.NodeTypeKey)) return [];
        if (node.NodeTypeKey == WorkflowHumanActivityKinds.GroupApproval)
            return WorkflowGroupApprovalConfiguration.TryRead(node.Config, out var groups)
                ? groups!.Groups.SelectMany(group => group.AssigneePolicy.Sources) : [];
        return WorkflowAssigneePolicy.TryRead(node.Config, out var policy) ? policy.Sources : [];
    }

    public static bool ValidateGraph(WorkflowDefinitionDraft draft, IReadOnlyDictionary<string, string[]> edges, string start)
    {
        var nodes = draft.Nodes.ToDictionary(node => node.NodeKey, StringComparer.Ordinal);
        var selections = draft.Nodes.SelectMany(Sources).Where(source => source.ResolverKindKey == WorkflowAssigneePolicy.StarterSelectedUsers).ToArray();
        if (selections.Length > WorkflowStarterSelectionPolicy.MaximumSelections || selections.Any(source => source.StarterSelection is null) ||
            selections.Select(source => source.StarterSelection!.SelectionKey).Distinct(StringComparer.Ordinal).Count() != selections.Length) return false;
        foreach (var node in draft.Nodes)
        foreach (var source in Sources(node).Where(source => source.ResolverKindKey == WorkflowAssigneePolicy.PreviousActors))
        {
            if (source.PreviousNodeKey is not { } previous || previous == node.NodeKey || !nodes.TryGetValue(previous, out var ancestor) ||
                !WorkflowHumanActivityKinds.IsFormActivity(ancestor.NodeTypeKey)) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(); pending.Push(start);
            while (pending.TryPop(out var key))
            {
                if (key == previous || !seen.Add(key)) continue;
                if (key == node.NodeKey) return false;
                if (edges.TryGetValue(key, out var next)) foreach (var target in next) pending.Push(target);
            }
        }
        return true;
    }

    public static bool ValidateFields(WorkflowDefinitionDraft draft, WorkflowFormSchema schema)
    {
        if (schema.Sections is null || schema.Sections.Any(section => section?.Fields is null)) return false;
        var fields = new Dictionary<string, WorkflowFormField>(StringComparer.Ordinal);
        foreach (var field in schema.Sections.SelectMany(section => section.Fields))
            if (field is null || !fields.TryAdd(field.FieldKey, field)) return false;
        return draft.Nodes.SelectMany(Sources).Where(source => source.ResolverKindKey is WorkflowAssigneePolicy.FormUsers or WorkflowAssigneePolicy.FormUnits)
            .All(source => source.FieldKey is { } key && fields.TryGetValue(key, out var field) && WorkflowAssigneeFieldValues.Supports(field));
    }
}
