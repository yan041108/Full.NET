using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>首个真实所有者动作是根审批最终提交点；不能在分支中或审批前提前应用业务快照。</summary>
internal static class WorkflowBusinessCommandBindings
{
    public static Result<WorkflowDefinitionDraft> Bind(WorkflowDefinitionDraft draft, WorkflowManagementScope scope, WorkflowBusinessActionCatalog catalog)
    {
        if (draft.Nodes is null || draft.Nodes.Count > WorkflowDefinitionCompiler.MaximumNodes || draft.Nodes.Any(node => node is null) ||
            draft.Nodes.GroupBy(node => node.NodeKey, StringComparer.Ordinal).Any(group => group.Count() > 1)) return Invalid();
        var commands = draft.Nodes.Where(node => node.NodeTypeKey == "business.command").ToArray();
        if (commands.Length == 0) return Result<WorkflowDefinitionDraft>.Success(draft);
        if (commands.Length != 1) return Invalid();
        var node = commands[0];
        if (!WorkflowBusinessCommandPolicy.TryRead(node.NodeTypeKey, node.Config, out var policy) || policy is null ||
            catalog.Find(scope.ScopeKey, policy.ActionKey, policy.ActionVersion) is not { } action ||
            !TryReadFinalizer(draft, node.NodeKey, out _)) return Invalid();
        var configuration = node.Config.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal);
        configuration["boundBusinessActionHash"] = JsonSerializer.SerializeToElement(action.Hash, Serialization.WorkflowJsonSerializerContext.Default.String);
        var updated = node with { Config = JsonSerializer.SerializeToElement(configuration, Serialization.WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement) };
        return Result<WorkflowDefinitionDraft>.Success(draft with { Nodes = draft.Nodes.Select(item => item.NodeKey == node.NodeKey ? updated : item).ToArray() });
    }
    public static bool TryReadFinalizer(WorkflowDefinitionDraft draft, string nodeKey, out IReadOnlyList<string> approvalNodes)
    {
        approvalNodes = [];
        var node = draft.Nodes.SingleOrDefault(item => item.NodeKey == nodeKey);
        if (node?.NodeTypeKey != "business.command" || !Successors(node.Config, out var next) || next is not [var end] ||
            draft.Nodes.Count(item => item.NodeTypeKey == "end") != 1 || !draft.Nodes.Any(item => item.NodeKey == end && item.NodeTypeKey == "end")) return false;
        var incoming = new List<string>();
        foreach (var candidate in draft.Nodes)
        {
            if (candidate.Config.ValueKind != JsonValueKind.Object) return false;
            if (Successors(candidate.Config, out var endTargets) && endTargets.Contains(end, StringComparer.Ordinal) && candidate.NodeKey != nodeKey) return false;
            if (Successors(candidate.Config, out var targets) && targets.Contains(nodeKey, StringComparer.Ordinal))
            {
                if (!WorkflowHumanActivityKinds.IsApproval(candidate.NodeTypeKey)) return false;
                incoming.Add(candidate.NodeKey);
            }
            if (candidate.Config.TryGetProperty("branches", out var branches) && branches.ValueKind == JsonValueKind.Array &&
                branches.EnumerateArray().Any(branch => branch.ValueKind == JsonValueKind.Object && branch.TryGetProperty("nextNodeKey", out var target) &&
                    target.ValueKind == JsonValueKind.String && (target.GetString() == nodeKey || target.GetString() == end))) return false;
        }
        approvalNodes = incoming; return incoming.Count > 0;
    }
    private static bool Successors(JsonElement config, out string[] values)
    {
        values = [];
        if (config.ValueKind != JsonValueKind.Object || !config.TryGetProperty("nextNodeKeys", out var array) || array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() > WorkflowDefinitionCompiler.MaximumEdges ||
            array.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String)) return false;
        values = array.EnumerateArray().Select(item => item.GetString()!).ToArray(); return true;
    }
    private static Result<WorkflowDefinitionDraft> Invalid() => Result<WorkflowDefinitionDraft>.Failure(new(WorkflowErrorCodes.DefinitionBusinessCommandInvalid,
        "Choose an available business action immediately after a root approval and before the end.", ErrorType.Validation));
}
