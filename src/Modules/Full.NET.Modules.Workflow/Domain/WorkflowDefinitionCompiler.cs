using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

internal static class WorkflowDefinitionCompiler
{
    /// <summary>编译工作流图并验证节点类型、拓扑及抄送配置。</summary>
    /// <param name="draft">待编译的工作流定义草稿。</param>
    /// <returns>包含规范化定义或稳定错误码的编译结果。</returns>
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

        // 抄送配置会进入运行时身份边界，只接受稳定用户标识的闭合集合，禁止任意副作用参数透传。
        if (draft.Nodes.Any(node =>
                node.NodeTypeKey == "notify.cc" &&
                !WorkflowCcNodeConfiguration.TryReadRecipients(node.Config, out _)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionCcRecipientsInvalid);
        }

        // 网关配置必须先通过闭合结构校验，图遍历才可以信任其中声明的出口集合。
        if (draft.Nodes.Any(node =>
                node.NodeTypeKey == "gateway.exclusive" &&
                !WorkflowExclusiveGatewayConfiguration.TryRead(node.Config, null, out _)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionGatewayInvalid);
        }

        // 超时策略会直接驱动后台可靠事件，只允许审批节点携带闭合且可执行的时间与接收人配置。
        if (draft.Nodes.Any(node =>
                node.NodeTypeKey == "human.approval" &&
                !WorkflowTodoTimeoutPolicy.TryRead(node.Config, out _)) ||
            draft.Nodes.Any(node =>
                node.NodeTypeKey != "human.approval" &&
                node.Config.ValueKind == JsonValueKind.Object &&
                node.Config.TryGetProperty("timeoutPolicy", out _)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionTimeoutPolicyInvalid);
        }

        // 多人审批配置决定运行时身份和完成门槛，发布前必须收敛为闭合用户集合与确定票数。
        if (draft.Nodes.Any(node =>
                node.NodeTypeKey == "human.approval" &&
                !WorkflowApprovalPolicy.TryRead(node.Config, out _)) ||
            draft.Nodes.Any(node =>
                node.NodeTypeKey != "human.approval" &&
                node.Config.ValueKind == JsonValueKind.Object &&
                node.Config.TryGetProperty("approvalPolicy", out _)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionApprovalPolicyInvalid);
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

        if (!WorkflowRuntimePlan.TryCreate(draft, out _))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionTopologyUnsupported);
        }

        return WorkflowCompilationResult.Success(
            WorkflowJsonCanonicalizer.Compile(writer => WriteCanonical(writer, draft)));
    }

    /// <summary>结合表单架构编译工作流图与节点字段策略。</summary>
    /// <param name="draft">待编译的工作流定义草稿。</param>
    /// <param name="formSchema">发布版本绑定的表单架构。</param>
    /// <returns>包含规范化定义或稳定错误码的编译结果。</returns>
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

        // 发布时将每个条件绑定到不可变表单架构，运行时不再接受临时字段或类型推断。
        if (draft.Nodes.Any(node =>
                node.NodeTypeKey == "gateway.exclusive" &&
                !WorkflowExclusiveGatewayConfiguration.TryRead(node.Config, formSchema, out _)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionGatewayInvalid);
        }

        if (!WorkflowRuntimePlan.TryCreate(draft, formSchema, out _))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionTopologyUnsupported);
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
