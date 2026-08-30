using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

internal static class WorkflowDefinitionCompiler
{
    public static WorkflowCompilationResult Compile(WorkflowDefinitionDraft draft)
    {
        if (draft.SchemaVersion != WorkflowNodeTypeCatalog.Current.DefinitionSchemaVersion ||
            draft.Nodes.Any(node =>
                WorkflowNodeTypeCatalog.TryGet(node.NodeTypeKey, out var definition) &&
                node.NodeSchemaVersion != definition!.NodeSchemaVersion))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionSchemaUnsupported);
        }

        if (draft.Nodes.Any(node =>
                !WorkflowNodeTypeCatalog.TryGet(node.NodeTypeKey, out _)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionNodeTypeUnknown);
        }

        if (draft.Nodes.Any(node =>
                WorkflowNodeTypeCatalog.TryGet(node.NodeTypeKey, out var definition) &&
                (!definition!.Publishable || !definition.Executable)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionNodeTypeUnavailable);
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

        if (!IsRuntimeTopologySupported(starts[0], nodeByKey, edges))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionTopologyUnsupported);
        }

        return WorkflowCompilationResult.Success(
            WorkflowJsonCanonicalizer.Compile(writer => WriteCanonical(writer, draft)));
    }

    public static WorkflowCompilationResult Compile(
        WorkflowDefinitionDraft draft,
        WorkflowFormSchema formSchema)
    {
        var graph = Compile(draft);
        if (!graph.IsSuccess)
        {
            return graph;
        }

        if (!TryCompileFieldPolicies(draft, formSchema, out var policiesByNode))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionFieldPolicyInvalid);
        }

        return WorkflowCompilationResult.Success(
            WorkflowJsonCanonicalizer.Compile(
                writer => WriteCanonical(writer, draft, policiesByNode)));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, WorkflowDefinitionDraft draft)
        => WriteCanonical(writer, draft, null);

    private static void WriteCanonical(
        Utf8JsonWriter writer,
        WorkflowDefinitionDraft draft,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? policiesByNode)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", draft.SchemaVersion);
        writer.WritePropertyName("nodes");
        writer.WriteStartArray();
        foreach (var node in draft.Nodes.OrderBy(item => item.NodeKey, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("nodeKey", node.NodeKey);
            writer.WriteString("nodeTypeKey", node.NodeTypeKey);
            writer.WriteNumber("nodeSchemaVersion", node.NodeSchemaVersion);
            writer.WritePropertyName("config");
            if (policiesByNode is not null && policiesByNode.TryGetValue(node.NodeKey, out var policies))
            {
                WriteConfigWithFieldPolicies(writer, node.Config, policies);
            }
            else
            {
                WorkflowJsonCanonicalizer.WriteElement(writer, node.Config);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static bool TryCompileFieldPolicies(
        WorkflowDefinitionDraft draft,
        WorkflowFormSchema formSchema,
        out IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> policiesByNode)
    {
        var fields = formSchema.Sections.SelectMany(section => section.Fields)
            .ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var node in draft.Nodes)
        {
            var configuredPolicies = default(JsonElement);
            var hasPolicies = node.Config.ValueKind == JsonValueKind.Object &&
                              node.Config.TryGetProperty("fieldPolicies", out configuredPolicies);
            var supportsFieldPolicies = WorkflowNodeTypeCatalog.TryGet(
                node.NodeTypeKey,
                out var nodeType) && nodeType!.SupportsFieldPolicies;
            if (!supportsFieldPolicies)
            {
                if (hasPolicies)
                {
                    policiesByNode = result;
                    return false;
                }

                continue;
            }

            if (node.Config.ValueKind != JsonValueKind.Object ||
                hasPolicies && configuredPolicies.ValueKind != JsonValueKind.Object)
            {
                policiesByNode = result;
                return false;
            }

            var policies = fields.Values.ToDictionary(
                field => field.FieldKey,
                field => field.Required ? "required" : "editable",
                StringComparer.Ordinal);
            if (hasPolicies)
            {
                foreach (var configured in configuredPolicies.EnumerateObject())
                {
                    if (!fields.ContainsKey(configured.Name) ||
                        configured.Value.ValueKind != JsonValueKind.String ||
                        configured.Value.GetString() is not ("hidden" or "readOnly" or "editable" or "required"))
                    {
                        policiesByNode = result;
                        return false;
                    }

                    policies[configured.Name] = configured.Value.GetString()!;
                }
            }

            result.Add(node.NodeKey, policies);
        }

        policiesByNode = result;
        return true;
    }

    private static void WriteConfigWithFieldPolicies(
        Utf8JsonWriter writer,
        JsonElement config,
        IReadOnlyDictionary<string, string> policies)
    {
        writer.WriteStartObject();
        var properties = config.EnumerateObject()
            .Where(property => property.Name != "fieldPolicies")
            .Select(property => property.Name)
            .Append("fieldPolicies")
            .OrderBy(name => name, StringComparer.Ordinal);
        foreach (var propertyName in properties)
        {
            writer.WritePropertyName(propertyName);
            if (propertyName == "fieldPolicies")
            {
                writer.WriteStartObject();
                foreach (var policy in policies.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(policy.Key, policy.Value);
                }

                writer.WriteEndObject();
            }
            else
            {
                WorkflowJsonCanonicalizer.WriteElement(writer, config.GetProperty(propertyName));
            }
        }

        writer.WriteEndObject();
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

    /// <summary>
    /// 当前执行器只闭合“开始→单人审批→结束”；目录扩展必须与运行时推进能力同批交付。
    /// </summary>
    private static bool IsRuntimeTopologySupported(
        WorkflowNodeDraft start,
        IReadOnlyDictionary<string, WorkflowNodeDraft> nodeByKey,
        IReadOnlyDictionary<string, string[]> edges)
    {
        if (nodeByKey.Count != 3 || edges[start.NodeKey] is not [var approvalKey] ||
            !nodeByKey.TryGetValue(approvalKey, out var approval) ||
            approval.NodeTypeKey != "human.approval" ||
            edges[approvalKey] is not [var endKey] ||
            !nodeByKey.TryGetValue(endKey, out var end) ||
            end.NodeTypeKey != "end" || edges[endKey].Length != 0)
        {
            return false;
        }

        return true;
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
