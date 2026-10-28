using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

internal static class WorkflowDefinitionCompiler
{
    // 同时约束递归栈、拓扑分析和后续运行计划的工作量，预算在任何递归之前检查。
    internal const int MaximumNodes = 128;
    internal const int MaximumEdges = 512;
    /// <summary>编译工作流图并验证节点类型、拓扑及抄送配置。</summary>
    /// <param name="draft">待编译的工作流定义草稿。</param>
    /// <returns>包含规范化定义或稳定错误码的编译结果。</returns>
    public static WorkflowCompilationResult Compile(WorkflowDefinitionDraft draft)
    {
        if (draft is null || draft.Nodes is null || draft.Nodes.Count > MaximumNodes ||
            draft.Nodes.Any(node => node is null || !WorkflowNodeKeyValidator.IsValid(node.NodeKey)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionTopologyUnsupported);
        }
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

        // 目录只承诺实际可执行的配置，不能把监听器、脚本或其他未知选项静默当作空配置发布。
        if (draft.Nodes.Any(node => !WorkflowNodeTypeCatalog.SupportsConfiguration(node)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionNodeTypeUnavailable);
        if (draft.Nodes.Any(node => node.NodeTypeKey == WorkflowSubprocessPolicy.NodeType && !WorkflowSubprocessPolicy.TryRead(node.Config, null, out _) ||
            node.NodeTypeKey != WorkflowSubprocessPolicy.NodeType && node.Config.ValueKind == JsonValueKind.Object &&
            (node.Config.TryGetProperty("subprocessPolicy", out _) || node.Config.TryGetProperty("boundSubprocess", out _) || node.Config.TryGetProperty("multiInstancePolicy", out _) || node.Config.TryGetProperty("starterPolicy", out _) || node.Config.TryGetProperty("childTimeoutPolicy", out _))))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionSubprocessInvalid);
        if (draft.Nodes.Any(node => !WorkflowBusinessCommandPolicy.TryRead(node.NodeTypeKey, node.Config, out _)) ||
            draft.Nodes.Count(node => node.NodeTypeKey == "business.command") > 1 ||
            draft.Nodes.Any(node => node.NodeTypeKey == "business.command" && !WorkflowBusinessCommandBindings.TryReadFinalizer(draft, node.NodeKey, out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionBusinessCommandInvalid);
        var withdrawalStarts = draft.Nodes.Where(node => node.NodeTypeKey == "start").ToArray();
        if (withdrawalStarts is [var withdrawalStart] && (!WorkflowWithdrawalPolicy.TryRead(withdrawalStart.Config, out var withdrawal) ||
            withdrawal is not null && withdrawal.NodeKeys.Any(key => !draft.Nodes.Any(node => node.NodeKey == key && WorkflowHumanActivityKinds.IsFormActivity(node.NodeTypeKey)))))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionWithdrawalPolicyInvalid);
        if (draft.Nodes.Any(node => WorkflowHumanActivityKinds.IsFormActivity(node.NodeTypeKey) && !WorkflowHumanActionPolicy.TryRead(node.Config, out _, node.NodeTypeKey)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionActionPolicyInvalid);

        // 分组必须显式声明组内来源及组间门槛，不能退化为默认发起人审批。
        if (draft.Nodes.Any(node => node.NodeTypeKey == WorkflowHumanActivityKinds.GroupApproval &&
            !WorkflowGroupApprovalConfiguration.TryRead(node.Config, out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionApprovalPolicyInvalid);

        if (draft.Nodes.Any(node => node.NodeTypeKey == WorkflowHumanActivityKinds.Task &&
            !WorkflowTaskPolicy.TryRead(node.Config, out _)) || draft.Nodes.Any(node => node.NodeTypeKey != WorkflowHumanActivityKinds.Task &&
                node.Config.ValueKind == JsonValueKind.Object && node.Config.TryGetProperty("taskPolicy", out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionTaskPolicyInvalid);

        if (draft.Nodes.Any(node => node.NodeTypeKey == WorkflowHumanActivityKinds.Vote &&
                !WorkflowVotePolicy.TryRead(node.Config, out _)) || draft.Nodes.Any(node => node.NodeTypeKey != WorkflowHumanActivityKinds.Vote &&
                node.Config.ValueKind == JsonValueKind.Object && node.Config.TryGetProperty("votePolicy", out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionVotePolicyInvalid);

        if (draft.Nodes.Any(node => node.NodeTypeKey == WorkflowHumanActivityKinds.Claim && !WorkflowClaimPolicy.TryRead(node.Config, out _)) ||
            draft.Nodes.Any(node => node.NodeTypeKey != WorkflowHumanActivityKinds.Claim && node.Config.ValueKind == JsonValueKind.Object && node.Config.TryGetProperty("claimPolicy", out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionClaimPolicyInvalid);

        // 抄送配置会进入运行时身份边界，只接受稳定用户标识的闭合集合，禁止任意副作用参数透传。
        if (draft.Nodes.Any(node =>
                node.NodeTypeKey == "notify.cc" &&
                !WorkflowCcNodeConfiguration.TryReadPolicy(node.Config, out _)))
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

        if (draft.Nodes.Any(node =>
                node.NodeTypeKey == "gateway.parallel" &&
                !WorkflowParallelGatewayConfiguration.TryRead(node.Config, out _)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionGatewayInvalid);
        }

        if (draft.Nodes.Any(node =>
                node.NodeTypeKey == "gateway.inclusive" &&
                !WorkflowInclusiveGatewayConfiguration.TryRead(node.Config, null, out _)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionGatewayInvalid);
        }

        // 超时策略会直接驱动后台可靠事件，只允许审批节点携带闭合且可执行的时间与接收人配置。
        if (draft.Nodes.Any(node => node.NodeTypeKey == "timer.delay" && !WorkflowDelayPolicy.TryRead(node.Config, null, out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionDelayPolicyInvalid);
        if (draft.Nodes.Any(node => node.NodeTypeKey is "data.modify" or "data.clear" &&
            !WorkflowFieldMutationPolicy.TryRead(node.NodeTypeKey, node.Config, null, out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionFieldPolicyInvalid);
        if (draft.Nodes.Any(node => node.NodeTypeKey is "integration.trigger" or "integration.async_trigger" && !WorkflowConnectorPolicy.TryRead(node.Config, null, out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionFieldPolicyInvalid);

        if (draft.Nodes.Any(node => !WorkflowSlaPolicy.TryRead(node.Config, node.NodeTypeKey, out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionTimeoutPolicyInvalid);
        if (draft.Nodes.Any(node => !WorkflowActivityListenerPolicy.TryRead(node.NodeTypeKey, node.Config, out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionNodeTypeUnavailable);
        if (draft.Nodes.Any(node => !WorkflowResultCallbackPolicy.TryRead(node.NodeTypeKey, node.Config, null, out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionFieldPolicyInvalid);

        if (draft.Nodes.Any(node =>
                WorkflowHumanActivityKinds.IsFormActivity(node.NodeTypeKey) &&
                !WorkflowTodoTimeoutPolicy.TryRead(node.Config, out _)) ||
            draft.Nodes.Any(node =>
                !WorkflowHumanActivityKinds.IsFormActivity(node.NodeTypeKey) &&
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

        // 办理人解析策略决定节点激活时的可信办理人集合，发布前必须闭合结构与来源键。
        if (draft.Nodes.Any(node =>
                WorkflowHumanActivityKinds.IsFormActivity(node.NodeTypeKey) &&
                !WorkflowAssigneePolicy.TryRead(node.Config, out _)) ||
            draft.Nodes.Any(node =>
                !WorkflowHumanActivityKinds.IsFormActivity(node.NodeTypeKey) &&
                node.Config.ValueKind == JsonValueKind.Object &&
                node.Config.TryGetProperty("assigneePolicy", out _)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid);
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

        if (edges.Values.Sum(keys => keys.Length) > MaximumEdges)
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionTopologyUnsupported);
        }

        if (edges.Values.SelectMany(keys => keys).Any(key => !nodeByKey.ContainsKey(key)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionReferenceDangling);
        }

        if (!WorkflowSubprocessTimeoutPolicy.ValidateTargets(nodeByKey, edges))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionSubprocessInvalid);
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

        if (!WorkflowDynamicAssigneeDefinitionRules.ValidateGraph(draft, edges, starts[0].NodeKey))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid);

        if (!WorkflowAsyncDefinitionPlan.TryCompile(draft, edges, out _))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionAsyncDependencyInvalid);

        if (!WorkflowRuntimePlan.TryCreate(draft, out _))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionTopologyUnsupported);
        }

        if (!WorkflowParallelWritePolicy.Validate(draft, edges))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionParallelWriteConflict);

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
        if (draft.Nodes.Any(node => !WorkflowResultCallbackPolicy.TryRead(node.NodeTypeKey, node.Config, formSchema, out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionFieldPolicyInvalid);

        if (!WorkflowDynamicAssigneeDefinitionRules.ValidateFields(draft, formSchema))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid);
        if (draft.Nodes.Any(node => node.NodeTypeKey is "data.modify" or "data.clear" &&
            !WorkflowFieldMutationPolicy.TryRead(node.NodeTypeKey, node.Config, formSchema, out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionFieldPolicyInvalid);

        if (draft.Nodes.Any(node => node.NodeTypeKey == "timer.delay" && !WorkflowDelayPolicy.TryRead(node.Config, formSchema, out _)))
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionDelayPolicyInvalid);

        foreach (var node in draft.Nodes.Where(node => node.NodeTypeKey == WorkflowHumanActivityKinds.Vote))
        {
            if (!WorkflowVotePolicy.TryRead(node.Config, out var vote))
                return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionVotePolicyInvalid);
            if (vote!.ResultFieldKey is not { } field) continue;
            if (!formSchema.Sections.SelectMany(section => section.Fields).Any(item => item.FieldKey == field && item.FieldTypeKey == "switch") ||
                !policiesByNode.TryGetValue(node.NodeKey, out var fields) || !fields.TryGetValue(field, out var access) || access is not ("editable" or "required"))
                return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionVotePolicyInvalid);
        }

        // 发布时将每个条件绑定到不可变表单架构，运行时不再接受临时字段或类型推断。
        if (draft.Nodes.Any(node =>
                node.NodeTypeKey == "gateway.exclusive" &&
                !WorkflowExclusiveGatewayConfiguration.TryRead(node.Config, formSchema, out _)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.DefinitionGatewayInvalid);
        }

        if (draft.Nodes.Any(node =>
                node.NodeTypeKey == "gateway.inclusive" &&
                !WorkflowInclusiveGatewayConfiguration.TryRead(node.Config, formSchema, out _)))
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
        writer.WriteString("workflowKindKey", draft.WorkflowKindKey);
        writer.WritePropertyName("nodes");
        writer.WriteStartArray();
        foreach (var node in draft.Nodes.OrderBy(item => item.NodeKey, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("nodeKey", node.NodeKey);
            writer.WriteString("nodeTypeKey", node.NodeTypeKey);
            writer.WriteNumber("nodeSchemaVersion", node.NodeSchemaVersion);
            if (node.AwaitActivityNodeKeys is { Count: > 0 } dependencies)
            {
                writer.WritePropertyName("awaitActivityNodeKeys"); writer.WriteStartArray();
                foreach (var source in dependencies.Order(StringComparer.Ordinal)) writer.WriteStringValue(source);
                writer.WriteEndArray();
            }
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
                field => node.NodeTypeKey is "data.modify" or "data.clear" or "integration.trigger" or "integration.async_trigger" ? "hidden" : field.Required ? "required" : "editable",
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
        if (node.NodeTypeKey == "gateway.parallel" &&
            WorkflowParallelGatewayConfiguration.TryRead(node.Config, out var parallel))
        {
            return parallel!.Role == WorkflowParallelGatewayRole.Fork
                ? parallel.Branches.Select(branch => branch.NextNodeKey).ToArray()
                : parallel.NextNodeKey is { } nextNodeKey ? [nextNodeKey] : [];
        }

        if (node.NodeTypeKey == "gateway.inclusive" &&
            WorkflowInclusiveGatewayConfiguration.TryRead(node.Config, null, out var inclusive))
        {
            return inclusive!.Role == WorkflowInclusiveGatewayRole.Fork
                ? inclusive.Branches.Select(branch => branch.NextNodeKey)
                    .Append(inclusive.DefaultNextNodeKey)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
                : [inclusive.DefaultNextNodeKey];
        }

        if (node.Config.ValueKind != JsonValueKind.Object ||
            !node.Config.TryGetProperty("nextNodeKeys", out var next) ||
            next.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var successors = next.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return node.NodeTypeKey == WorkflowSubprocessPolicy.NodeType && WorkflowSubprocessTimeoutPolicy.TryRead(node.Config, out var timeout) && timeout?.NextNodeKey is { } target
            ? successors.Append(target).Distinct(StringComparer.Ordinal).ToArray() : successors;
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
