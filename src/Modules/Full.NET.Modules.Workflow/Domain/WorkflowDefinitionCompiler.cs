using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

internal static class WorkflowDefinitionCompiler
{
    private static readonly HashSet<string> SupportedNodeTypes =
        new(StringComparer.Ordinal)
        {
            "start",
            "human.approval",
            "notify.cc",
            "gateway.exclusive",
            "end",
        };

    public static WorkflowCompilationResult Compile(WorkflowDefinitionDraft draft)
    {
        if (draft.SchemaVersion != 1 || draft.Nodes.Any(node => node.NodeSchemaVersion != 1))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionSchemaUnsupported);
        }

        if (draft.Nodes.Any(node => !SupportedNodeTypes.Contains(node.NodeTypeKey)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionNodeTypeUnknown);
        }

        if (draft.Nodes.GroupBy(node => node.NodeKey, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionNodeKeyDuplicate);
        }

        var starts = draft.Nodes.Where(node => node.NodeTypeKey == "start").ToArray();
        if (starts.Length != 1)
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionStartInvalid);
        }

        var nodeByKey = draft.Nodes.ToDictionary(node => node.NodeKey, StringComparer.Ordinal);
        var edges = draft.Nodes.ToDictionary(
            node => node.NodeKey,
            ReadNextNodeKeys,
            StringComparer.Ordinal);

        if (edges.Values.SelectMany(keys => keys).Any(key => !nodeByKey.ContainsKey(key)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionReferenceDangling);
        }

        if (ContainsCycle(starts[0].NodeKey, edges))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionBackEdgeIllegal);
        }

        var reachable = CollectReachable(starts[0].NodeKey, edges);
        if (reachable.Count != draft.Nodes.Count)
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionNodeUnreachable);
        }

        if (!reachable.Any(key => nodeByKey[key].NodeTypeKey == "end"))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionEndMissing);
        }

        var normalized = new
        {
            draft.SchemaVersion,
            Nodes = draft.Nodes
                .OrderBy(node => node.NodeKey, StringComparer.Ordinal)
                .Select(node => new
                {
                    node.NodeKey,
                    node.NodeTypeKey,
                    node.NodeSchemaVersion,
                    node.Config,
                }),
        };
        return WorkflowCompilationResult.Success(
            WorkflowJsonCanonicalizer.Compile(JsonSerializer.SerializeToElement(normalized)));
    }

    private static string[] ReadNextNodeKeys(WorkflowNodeDraft node)
    {
        if (node.Config.ValueKind != JsonValueKind.Object ||
            !node.Config.TryGetProperty("nextNodeKeys", out var next) ||
            next.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return next.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static HashSet<string> CollectReachable(
        string start,
        IReadOnlyDictionary<string, string[]> edges)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var next in edges[current])
            {
                pending.Push(next);
            }
        }

        return visited;
    }

    private static bool ContainsCycle(
        string start,
        IReadOnlyDictionary<string, string[]> edges)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(string current)
        {
            if (active.Contains(current))
            {
                return true;
            }

            if (!visited.Add(current))
            {
                return false;
            }

            active.Add(current);
            foreach (var next in edges[current])
            {
                if (Visit(next))
                {
                    return true;
                }
            }

            active.Remove(current);
            return false;
        }

        return Visit(start);
    }
}
