using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>描述执行器可闭合处理的审批、抄送、排他网关与并行网关有向无环运行计划。</summary>
internal sealed class WorkflowRuntimePlan
{
    /// <summary>处置轨迹沿用固定版本的真实节点类型，不能把人工节点伪装为空节点。</summary>
    public string? FindNodeTypeKey(string nodeKey) => nodes.TryGetValue(nodeKey, out var node) ? node.NodeTypeKey : null;
    public WorkflowSlaPolicy? FindSlaPolicy(string nodeKey) => nodes.TryGetValue(nodeKey, out var node) &&
        WorkflowSlaPolicy.TryRead(node.Config, node.NodeTypeKey, out var policy) ? policy : null;
    public bool TryGetResultCallbackPolicy(string nodeKey, out WorkflowResultCallbackPolicy? policy)
    {
        policy = null;
        return nodes.TryGetValue(nodeKey, out var node) && WorkflowResultCallbackPolicy.TryRead(node.NodeTypeKey, node.Config, formSchema, out policy);
    }
    public bool TryGetActivityListeners(string nodeKey, out IReadOnlyList<WorkflowActivityListenerPolicy> listeners)
    {
        listeners = [];
        return nodes.TryGetValue(nodeKey, out var node) && WorkflowActivityListenerPolicy.TryRead(node.NodeTypeKey, node.Config, out listeners);
    }

    /// <summary>退回的历史目标仍须属于本根固定版本，不能用快照声明任意活动种类。</summary>
    public bool TryGetReturnNode(string nodeKey, out WorkflowNodeDraft? node)
    {
        node = null;
        if (!nodes.TryGetValue(nodeKey, out var candidate) || candidate.NodeTypeKey is not ("human.approval" or "human.group_approval" or "human.task")) return false;
        node = candidate;
        return true;
    }

    /// <summary>将实际退回来源绑定结构区域；越出当前分支时必须整体重开对应外层区域。</summary>
    public bool TryDescribeReturnRegion(string entry, string source, out WorkflowStructuredRegion? region, out bool closesWholeRegion)
    {
        region = null; closesWholeRegion = false;
        return WorkflowStructuredRegionPlan.TryCreate(new(1, nodes.Values.ToArray()), outgoing, out var structure) &&
            structure!.TryDescribeReturnRegion(entry, source, out region, out closesWholeRegion);
    }

    /// <summary>恢复轮次时重新验证原入口及出口，不从已变化的历史状态推测边界。</summary>
    public bool TryDescribeFixedReturnRegion(string entry, string exit, out WorkflowStructuredRegion? region)
    {
        region = null;
        return WorkflowStructuredRegionPlan.TryCreate(new(1, nodes.Values.ToArray()), outgoing, out var structure) &&
            structure!.TryDescribeRegion(entry, exit, out region);
    }

    /// <summary>退回正常退出只使用固定定义的唯一后继，不接受请求指定目的节点。</summary>
    public bool TryGetNormalSuccessor(string nodeKey, out string? successor)
    {
        successor = null;
        if (!outgoing.TryGetValue(nodeKey, out var next) || next is not [var key]) return false;
        successor = key;
        return true;
    }

    /// <summary>未执行的终点也由固定定义识别，不能用历史是否已有End步骤决定出口。</summary>
    public bool IsTerminalNode(string nodeKey) => nodes.TryGetValue(nodeKey, out var node) && node.NodeTypeKey == "end";
    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyValues =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<string, WorkflowNodeDraft> nodes;
    private readonly IReadOnlyDictionary<string, string[]> outgoing;
    private readonly IReadOnlyDictionary<string, WorkflowExclusiveGatewayDefinition> gateways;
    private readonly IReadOnlyDictionary<string, WorkflowParallelGatewayDefinition> parallelForks;
    private readonly IReadOnlyDictionary<string, WorkflowParallelGatewayDefinition> parallelJoins;
    private readonly IReadOnlyDictionary<string, WorkflowInclusiveGatewayDefinition> inclusiveForks;
    private readonly IReadOnlyDictionary<string, WorkflowInclusiveGatewayDefinition> inclusiveJoins;
    private readonly string startNextNodeKey;
    private readonly WorkflowFormSchema? formSchema;
    private readonly IReadOnlyDictionary<string, WorkflowAsyncWaitDefinition> asynchronousWaits;
    public WorkflowDynamicRoutePlan DynamicRoutes { get; }
    public IReadOnlyList<Guid> FlowAdministratorUserIds { get; }
    public bool HasBusinessCommands { get; }
    public bool HasSubprocess { get; }

    /// <summary>使用已验证节点、出口和网关条件创建不可变运行计划。</summary>
    /// <param name="nodes">按稳定节点键索引的定义节点。</param>
    /// <param name="outgoing">每个节点的有序出口。</param>
    /// <param name="gateways">按节点键索引的排他网关定义。</param>
    /// <param name="parallelForks">按节点键索引的并行分叉定义。</param>
    /// <param name="parallelJoins">按节点键索引的并行汇合定义。</param>
    /// <param name="inclusiveForks">按节点键索引的包容分叉定义。</param>
    /// <param name="inclusiveJoins">按节点键索引的包容汇合定义。</param>
    /// <param name="startNextNodeKey">开始节点的唯一后继。</param>
    private WorkflowRuntimePlan(
        IReadOnlyDictionary<string, WorkflowNodeDraft> nodes,
        IReadOnlyDictionary<string, string[]> outgoing,
        IReadOnlyDictionary<string, WorkflowExclusiveGatewayDefinition> gateways,
        IReadOnlyDictionary<string, WorkflowParallelGatewayDefinition> parallelForks,
        IReadOnlyDictionary<string, WorkflowParallelGatewayDefinition> parallelJoins,
        IReadOnlyDictionary<string, WorkflowInclusiveGatewayDefinition> inclusiveForks,
        IReadOnlyDictionary<string, WorkflowInclusiveGatewayDefinition> inclusiveJoins,
        string startNextNodeKey,
        IReadOnlyList<Guid> flowAdministratorUserIds, WorkflowFormSchema? formSchema,
        IReadOnlyDictionary<string, WorkflowAsyncWaitDefinition> asynchronousWaits, WorkflowDynamicRoutePlan dynamicRoutes)
    {
        this.nodes = nodes;
        this.outgoing = outgoing;
        this.gateways = gateways;
        this.parallelForks = parallelForks;
        this.parallelJoins = parallelJoins;
        this.inclusiveForks = inclusiveForks;
        this.inclusiveJoins = inclusiveJoins;
        this.startNextNodeKey = startNextNodeKey;
        this.formSchema = formSchema;
        this.asynchronousWaits = asynchronousWaits;
        DynamicRoutes = dynamicRoutes;
        FlowAdministratorUserIds = flowAdministratorUserIds;
        HasBusinessCommands = nodes.Values.Any(node => node.NodeTypeKey == "business.command");
        HasSubprocess = nodes.Values.Any(node => node.NodeTypeKey == WorkflowSubprocessPolicy.NodeType);
    }

    /// <summary>从定义草稿构造结构闭合的运行计划。</summary>
    /// <param name="draft">已经过结构反序列化的定义草稿。</param>
    /// <param name="plan">构造成功后的不可变运行计划。</param>
    /// <returns>拓扑可由当前执行器闭合处理时返回 <see langword="true"/>。</returns>
    public static bool TryCreate(WorkflowDefinitionDraft draft, out WorkflowRuntimePlan? plan) =>
        TryCreate(draft, null, out plan);

    /// <summary>从定义草稿构造绑定表单架构的运行计划。</summary>
    /// <param name="draft">已经过结构反序列化的定义草稿。</param>
    /// <param name="formSchema">发布版本绑定的表单架构；为空时只验证结构。</param>
    /// <param name="plan">构造成功后的不可变运行计划。</param>
    /// <returns>全部路径到达终点，且审批类定义每条路径包含人工审批时返回 <see langword="true"/>。</returns>
    public static bool TryCreate(
        WorkflowDefinitionDraft draft,
        WorkflowFormSchema? formSchema,
        out WorkflowRuntimePlan? plan)
    {
        plan = null;
        if (draft is null || draft.Nodes is null || draft.Nodes.Any(node => node is null || !WorkflowNodeKeyValidator.IsValid(node.NodeKey)) ||
            draft.SchemaVersion != WorkflowNodeTypeCatalog.Current.DefinitionSchemaVersion ||
            draft.WorkflowKindKey is not ("approval" or "automation") ||
            draft.Nodes.Any(node => !WorkflowNodeTypeCatalog.TryGet(node.NodeTypeKey, out var type) ||
                node.NodeSchemaVersion != type!.NodeSchemaVersion || !WorkflowNodeTypeCatalog.SupportsConfiguration(node) ||
                !WorkflowSlaPolicy.TryRead(node.Config, node.NodeTypeKey, out _) ||
                !WorkflowResultCallbackPolicy.TryRead(node.NodeTypeKey, node.Config, formSchema, out _) ||
                !WorkflowActivityListenerPolicy.TryRead(node.NodeTypeKey, node.Config, out _)) ||
            draft.Nodes.Any(node => !WorkflowBusinessCommandPolicy.TryRead(node.NodeTypeKey, node.Config, out _)) ||
            draft.Nodes.Count < 2 || draft.Nodes.Count > WorkflowDefinitionCompiler.MaximumNodes ||
            draft.Nodes.GroupBy(node => node.NodeKey, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            return false;
        }

        var nodes = draft.Nodes.ToDictionary(node => node.NodeKey, StringComparer.Ordinal);
        var starts = draft.Nodes.Where(node => node.NodeTypeKey == "start").ToArray();
        if (starts is not [var start] || !TryReadSingleNext(start.Config, out var startNextNodeKey) ||
            !WorkflowFlowAdministrators.TryRead(start.Config, out var flowAdministrators))
        {
            return false;
        }

        var outgoing = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var gateways = new Dictionary<string, WorkflowExclusiveGatewayDefinition>(StringComparer.Ordinal);
        var parallelForks = new Dictionary<string, WorkflowParallelGatewayDefinition>(StringComparer.Ordinal);
        var parallelJoins = new Dictionary<string, WorkflowParallelGatewayDefinition>(StringComparer.Ordinal);
        var inclusiveForks = new Dictionary<string, WorkflowInclusiveGatewayDefinition>(StringComparer.Ordinal);
        var inclusiveJoins = new Dictionary<string, WorkflowInclusiveGatewayDefinition>(StringComparer.Ordinal);
        foreach (var node in draft.Nodes)
        {
            if (!TryReadNode(node, formSchema, outgoing, gateways, parallelForks, parallelJoins, inclusiveForks, inclusiveJoins))
            {
                return false;
            }
        }

        if (outgoing.Values.SelectMany(keys => keys).Any(key => !nodes.ContainsKey(key)))
        {
            return false;
        }

        if (!WorkflowSubprocessTimeoutPolicy.ValidateTargets(nodes, outgoing) ||
            !WorkflowDynamicAssigneeDefinitionRules.ValidateGraph(draft, outgoing, start.NodeKey) ||
            formSchema is not null && !WorkflowDynamicAssigneeDefinitionRules.ValidateFields(draft, formSchema)) return false;

        if (!ValidateParallelPairs(parallelForks, parallelJoins) ||
            !ValidateInclusivePairs(inclusiveForks, inclusiveJoins))
        {
            return false;
        }

        var forkBoundaries = parallelForks.ToDictionary(pair => pair.Key, pair => pair.Value.JoinNodeKey!, StringComparer.Ordinal);
        foreach (var pair in inclusiveForks) forkBoundaries.Add(pair.Key, pair.Value.JoinNodeKey!);
        if (!ValidateExecutionScopes(start.NodeKey, nodes, outgoing, forkBoundaries,
                parallelJoins.Keys.Concat(inclusiveJoins.Keys).ToHashSet(StringComparer.Ordinal))) return false;

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        if (!ValidatePath(
                start.NodeKey,
                hasApproval: draft.WorkflowKindKey == "automation",
                nodes,
                outgoing,
                reachable,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<(string NodeKey, bool HasApproval)>()) ||
            reachable.Count != nodes.Count)
        {
            return false;
        }

        if (!WorkflowAsyncDefinitionPlan.TryCompile(draft, outgoing, out var asynchronousWaits)) return false;
        var dynamicRoutes = new WorkflowDynamicRoutePlan(new Dictionary<string, WorkflowCompiledRoute>(StringComparer.Ordinal));
        if (draft.Nodes.Any(node => node.NodeTypeKey == WorkflowDynamicRoutePolicy.NodeType) &&
            !WorkflowDynamicRoutePlan.TryCompile(draft, outgoing, formSchema, out dynamicRoutes)) return false;
        plan = new WorkflowRuntimePlan(
            nodes,
            outgoing,
            gateways,
            parallelForks,
            parallelJoins,
            inclusiveForks,
            inclusiveJoins,
            startNextNodeKey!, flowAdministrators, formSchema, asynchronousWaits, dynamicRoutes!);
        return true;
    }

    /// <summary>解析不含条件网关的兼容启动迁移。</summary>
    /// <param name="transition">启动迁移。</param>
    /// <returns>路径无需表单值即可求值时返回 <see langword="true"/>。</returns>
    public bool TryResolveStart(out WorkflowRuntimeTransition transition) =>
        TryResolveStart(EmptyValues, out transition);

    /// <summary>根据实例表单值解析启动后到首个等待点的迁移。</summary>
    /// <param name="values">实例绑定且已验证的表单值。</param>
    /// <param name="transition">启动迁移。</param>
    /// <returns>网关条件可安全求值且路径可达等待点时返回 <see langword="true"/>。</returns>
    public bool TryResolveStart(
        IReadOnlyDictionary<string, JsonElement> values,
        out WorkflowRuntimeTransition transition) =>
        TryTraverse(startNextNodeKey, values, stopAtJoinNodeKey: null, out transition);

    /// <summary>解析不含条件网关的兼容审批通过迁移。</summary>
    /// <param name="nodeKey">当前人工审批节点键。</param>
    /// <param name="transition">匹配到的闭合迁移。</param>
    /// <returns>节点属于计划且路径无需表单值即可求值时返回 <see langword="true"/>。</returns>
    public bool TryResolveAfterHuman(string nodeKey, out WorkflowRuntimeTransition transition) =>
        TryResolveAfterHuman(nodeKey, EmptyValues, null, out transition);

    /// <summary>根据审批后的实例表单值解析下一迁移。</summary>
    /// <param name="nodeKey">当前人工审批节点键。</param>
    /// <param name="values">应用本次字段补丁后的完整表单值。</param>
    /// <param name="transition">匹配到的闭合迁移。</param>
    /// <returns>当前节点与后继路径有效时返回 <see langword="true"/>。</returns>
    public bool TryResolveAfterHuman(
        string nodeKey,
        IReadOnlyDictionary<string, JsonElement> values,
        out WorkflowRuntimeTransition transition) =>
        TryResolveAfterHuman(nodeKey, values, null, out transition);

    /// <summary>根据审批后的实例表单值解析并行分支上的下一迁移。</summary>
    /// <param name="nodeKey">当前人工审批节点键。</param>
    /// <param name="values">应用本次字段补丁后的完整表单值。</param>
    /// <param name="stopAtJoinNodeKey">分支所属汇合节点键；到达汇合点时停止推进。</param>
    /// <param name="transition">匹配到的闭合迁移。</param>
    /// <returns>当前节点与后继路径有效时返回 <see langword="true"/>。</returns>
    public bool TryResolveAfterHuman(
        string nodeKey,
        IReadOnlyDictionary<string, JsonElement> values,
        string? stopAtJoinNodeKey,
        out WorkflowRuntimeTransition transition)
    {
        transition = default;
        return nodes.TryGetValue(nodeKey, out var node) &&
               WorkflowHumanActivityKinds.IsFormActivity(node.NodeTypeKey) &&
               outgoing.TryGetValue(nodeKey, out var next) &&
               next is [var nextNodeKey] &&
               TryTraverse(nextNodeKey, values, stopAtJoinNodeKey, out transition);
    }

    /// <summary>在全部并行分支到达汇合点后，从汇合节点继续解析下一迁移。</summary>
    /// <param name="joinNodeKey">汇合节点键。</param>
    /// <param name="values">实例绑定且已验证的表单值。</param>
    /// <param name="transition">汇合后继续的闭合迁移。</param>
    /// <param name="stopAtJoinNodeKey">嵌套汇合恢复后应停留的父级汇合边界。</param>
    /// <returns>汇合节点存在且后继路径可闭合时返回 <see langword="true"/>。</returns>
    public bool TryResolveAfterJoin(
        string joinNodeKey,
        IReadOnlyDictionary<string, JsonElement> values,
        out WorkflowRuntimeTransition transition,
        string? stopAtJoinNodeKey = null)
    {
        transition = default;
        string? nextNodeKey = null;
        if (parallelJoins.TryGetValue(joinNodeKey, out var parallelJoin))
        {
            nextNodeKey = parallelJoin.NextNodeKey;
        }
        else if (inclusiveJoins.TryGetValue(joinNodeKey, out var inclusiveJoin))
        {
            nextNodeKey = inclusiveJoin.DefaultNextNodeKey;
        }

        return nextNodeKey is not null &&
               TryTraverse(nextNodeKey, values, stopAtJoinNodeKey, out transition);
    }

    /// <summary>确认分叉节点属于包容网关。</summary>
    /// <param name="forkNodeKey">分叉节点键。</param>
    /// <returns>节点存在于包容分叉索引时返回 <see langword="true"/>。</returns>
    public bool IsInclusiveFork(string forkNodeKey) =>
        inclusiveForks.ContainsKey(forkNodeKey);

    /// <summary>耐久续办仅从固定版本节点和原分支汇合边界恢复。</summary>
    public bool TryResolveContinuation(string nodeKey, IReadOnlyDictionary<string, JsonElement> values,
        string? joinNodeKey, out WorkflowRuntimeTransition transition, string? acknowledgedAwaitNodeKey = null)
    {
        transition = default;
        return (acknowledgedAwaitNodeKey is null || acknowledgedAwaitNodeKey == nodeKey) &&
            TryTraverse(nodeKey, values, joinNodeKey, out transition, acknowledgedAwaitNodeKey);
    }

    public bool TryGetAsyncWait(string nodeKey, out WorkflowAsyncWaitDefinition? definition) =>
        asynchronousWaits.TryGetValue(nodeKey, out definition);

    /// <summary>自动等待结束后才求值后继，不能将等待节点冒充人工办理完成。</summary>
    public bool TryResolveAfterAutomaticWait(string nodeKey, IReadOnlyDictionary<string, JsonElement> values,
        string? joinNodeKey, out WorkflowRuntimeTransition transition)
    {
        transition = default;
        return nodes.TryGetValue(nodeKey, out var node) && node.NodeTypeKey is "timer.delay" or "data.modify" or "data.clear" &&
            outgoing.TryGetValue(nodeKey, out var next) && next is [var key] &&
            TryTraverse(key, values, joinNodeKey, out transition);
    }

    /// <summary>确认节点是否为当前计划中的人工审批等待点。</summary>
    /// <param name="nodeKey">待检查的节点键。</param>
    /// <returns>节点存在且属于人工审批时返回 <see langword="true"/>。</returns>
    public bool ContainsHumanNode(string nodeKey) =>
        nodes.TryGetValue(nodeKey, out var node) && WorkflowHumanActivityKinds.IsFormActivity(node.NodeTypeKey);

    /// <summary>每条分支必须先闭合自己的汇合范围，禁止逃逸终点、交叉汇合或跨范围共享节点。</summary>
    private static bool ValidateExecutionScopes(string start, IReadOnlyDictionary<string, WorkflowNodeDraft> nodes,
        IReadOnlyDictionary<string, string[]> outgoing, IReadOnlyDictionary<string, string> forks, ISet<string> joins)
    {
        var scopes = new Dictionary<string, string>(StringComparer.Ordinal);
        bool Visit(string key, IReadOnlyList<string> stack)
        {
            var signature = string.Join('\n', stack);
            if (scopes.TryGetValue(key, out var previous)) return previous == signature;
            scopes.Add(key, signature);
            if (nodes[key].NodeTypeKey == "end") return stack.Count == 0;
            var nextStack = stack;
            if (joins.Contains(key))
            {
                if (stack.Count == 0 || stack[^1] != key) return false;
                nextStack = stack.Take(stack.Count - 1).ToArray();
            }
            if (forks.TryGetValue(key, out var join)) nextStack = [..nextStack, join];
            return outgoing[key].All(next => Visit(next, nextStack));
        }
        return Visit(start, []);
    }

    /// <summary>读取不可变发布版本中指定人工审批节点的超时策略。</summary>
    /// <param name="nodeKey">目标人工审批节点键。</param>
    /// <param name="timeoutPolicy">节点固化的可选超时策略。</param>
    /// <returns>目标存在、类型正确且策略可解析时返回 <see langword="true"/>。</returns>
    public bool TryGetHumanTimeoutPolicy(
        string nodeKey,
        out WorkflowTodoTimeoutPolicy? timeoutPolicy)
    {
        timeoutPolicy = null;
        return nodes.TryGetValue(nodeKey, out var node) &&
               WorkflowHumanActivityKinds.IsFormActivity(node.NodeTypeKey) &&
               WorkflowTodoTimeoutPolicy.TryRead(node.Config, out timeoutPolicy);
    }

    /// <summary>沿运行时路径执行自动节点，直到人工审批、并行分叉、汇合等待或终点。</summary>
    /// <param name="initialNodeKey">遍历起点。</param>
    /// <param name="values">实例绑定且已验证的表单值。</param>
    /// <param name="stopAtJoinNodeKey">并行分支遍历时的汇合停止点。</param>
    /// <param name="transition">解析得到的运行迁移。</param>
    /// <returns>路径可安全闭合时返回 <see langword="true"/>。</returns>
    private bool TryTraverse(
        string initialNodeKey,
        IReadOnlyDictionary<string, JsonElement> values,
        string? stopAtJoinNodeKey,
        out WorkflowRuntimeTransition transition, string? acknowledgedAwaitNodeKey = null)
    {
        transition = default;
        var automaticNodes = new List<WorkflowAutomaticRuntimeNode>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = initialNodeKey;
        while (visited.Add(current) && nodes.TryGetValue(current, out var node))
        {
            // 只有服务器已验证的原屏障续办能跳过首节点等待；后继的另一等待仍须正常建立。
            if (current != acknowledgedAwaitNodeKey && asynchronousWaits.TryGetValue(current, out var asynchronousWait))
            {
                transition = new WorkflowRuntimeTransition(null, false, automaticNodes,
                    automaticWait: new WorkflowAsyncBarrierWaitPlan(current, asynchronousWait));
                return true;
            }
            acknowledgedAwaitNodeKey = null;
            switch (node.NodeTypeKey)
            {
                case WorkflowDynamicRoutePolicy.NodeType:
                    if (!DynamicRoutes.Routes.TryGetValue(node.NodeKey, out var route)) return false;
                    transition = new WorkflowRuntimeTransition(null, false, automaticNodes,
                        automaticWait: new WorkflowRouteWaitPlan(node.NodeKey, route.NormalNextNodeKey, node.Config.Clone()));
                    return true;
                case WorkflowSubprocessPolicy.NodeType:
                    if (formSchema is null || !WorkflowSubprocessPolicy.TryRead(node.Config, formSchema, out var childPolicy) ||
                        !node.Config.TryGetProperty("boundSubprocess", out var childBoundJson) || !WorkflowBoundSubprocess.TryRead(childBoundJson, childPolicy.DefinitionVersionId, out var childBound) ||
                        !TryReadSingleNext(node.Config, out var childNext)) return false;
                    transition = new WorkflowRuntimeTransition(null, false, automaticNodes,
                        automaticWait: new WorkflowSubprocessWaitPlan(node.NodeKey, childNext!, childPolicy, childBound, formSchema, node.Config.Clone()));
                    return true;
                case "business.command":
                    if (!WorkflowBusinessCommandPolicy.TryRead(node.NodeTypeKey, node.Config, out var businessPolicy) || businessPolicy is null ||
                        !node.Config.TryGetProperty("boundBusinessActionHash", out var businessHash) || !WorkflowBusinessCommandPolicy.ValidHash(businessHash) ||
                        outgoing[node.NodeKey] is not [var businessNext] || !WorkflowBusinessCommandBindings.TryReadFinalizer(
                            new WorkflowDefinitionDraft(1, nodes.Values.ToArray()), node.NodeKey, out var approvals)) return false;
                    transition = new WorkflowRuntimeTransition(null, false, automaticNodes,
                        automaticWait: new WorkflowBusinessCommandWaitPlan(node.NodeKey, businessNext, businessPolicy, businessHash.GetString()!, approvals));
                    return true;
                case "integration.trigger":
                case "integration.async_trigger":
                    if (!WorkflowConnectorPolicy.TryRead(node.Config, formSchema, out var connectorPolicy) ||
                        !node.Config.TryGetProperty("boundConnectorHash", out var boundHash) || boundHash.ValueKind != JsonValueKind.String ||
                        boundHash.GetString() is not { Length: 64 } hash || hash.Any(character => !char.IsAsciiHexDigitLower(character)) ||
                        outgoing[node.NodeKey] is not [var connectorNext]) return false;
                    transition = new WorkflowRuntimeTransition(null, false, automaticNodes,
                        automaticWait: new WorkflowConnectorWaitPlan(node.NodeKey, connectorNext, connectorPolicy!, hash, node.NodeTypeKey == "integration.async_trigger"));
                    return true;
                case "timer.delay":
                    if (!WorkflowDelayPolicy.TryRead(node.Config, null, out var delayPolicy) || outgoing[node.NodeKey] is not [var delayNext]) return false;
                    transition = new WorkflowRuntimeTransition(null, false, automaticNodes,
                        automaticWait: new WorkflowDelayWaitPlan(node.NodeKey, delayNext, delayPolicy!));
                    return true;
                case "data.modify":
                case "data.clear":
                    if (!WorkflowFieldMutationPolicy.TryRead(node.NodeTypeKey, node.Config, formSchema, out var fieldPolicy) ||
                        outgoing[node.NodeKey] is not [var fieldNext]) return false;
                    transition = new WorkflowRuntimeTransition(null, false, automaticNodes,
                        automaticWait: new WorkflowFieldWaitPlan(node.NodeKey, fieldNext, fieldPolicy!));
                    return true;
                case "human.claim":
                    if (!WorkflowClaimPolicy.TryRead(node.Config, out var claimPolicy) ||
                        !WorkflowAssigneePolicy.TryRead(node.Config, out var claimAssignees) ||
                        !WorkflowTodoTimeoutPolicy.TryRead(node.Config, out var claimTimeout)) return false;
                    transition = new WorkflowRuntimeTransition(node.NodeKey, false, automaticNodes, claimTimeout,
                        assigneePolicy: claimAssignees, claimPolicy: claimPolicy);
                    return true;
                case "human.vote":
                    if (!WorkflowVotePolicy.TryRead(node.Config, out var votePolicy) ||
                        !WorkflowAssigneePolicy.TryRead(node.Config, out var voteAssignees) ||
                        !WorkflowTodoTimeoutPolicy.TryRead(node.Config, out var voteTimeout)) return false;
                    transition = new WorkflowRuntimeTransition(node.NodeKey, false, automaticNodes, voteTimeout,
                        assigneePolicy: voteAssignees, votePolicy: votePolicy);
                    return true;
                case "human.task":
                    if (!WorkflowTaskPolicy.TryRead(node.Config, out var taskPolicy) ||
                        !WorkflowAssigneePolicy.TryRead(node.Config, out var taskAssignees) ||
                        !WorkflowTodoTimeoutPolicy.TryRead(node.Config, out var taskTimeout)) return false;
                    transition = new WorkflowRuntimeTransition(node.NodeKey, false, automaticNodes, taskTimeout,
                        assigneePolicy: taskAssignees, taskPolicy: taskPolicy);
                    return true;
                case "human.approval":
                case "human.group_approval":
                    if (!WorkflowTodoTimeoutPolicy.TryRead(node.Config, out var timeoutPolicy) ||
                        !WorkflowApprovalPolicy.TryRead(node.Config, out var approvalPolicy) ||
                        !WorkflowAssigneePolicy.TryRead(node.Config, out var assigneePolicy))
                    {
                        return false;
                    }

                    transition = new WorkflowRuntimeTransition(
                        node.NodeKey, false, automaticNodes, timeoutPolicy, approvalPolicy, assigneePolicy);
                    return true;
                case "end":
                    transition = new WorkflowRuntimeTransition(null, true, automaticNodes, null, terminalNodeKey: node.NodeKey);
                    return true;
                case "notify.cc":
                    if (!WorkflowCcNodeConfiguration.TryReadPolicy(node.Config, out var recipientPolicy) ||
                        outgoing[node.NodeKey] is not [var ccNext])
                    {
                        return false;
                    }

                    automaticNodes.Add(new WorkflowAutomaticRuntimeNode(
                        node.NodeKey,
                        node.NodeTypeKey,
                        recipientPolicy.Sources.Where(source => source.ResolverKindKey == WorkflowAssigneePolicy.SpecifiedUsers).SelectMany(source => source.UserIds).Distinct().ToArray(),
                        null, recipientPolicy));
                    current = ccNext;
                    break;
                case "control.empty":
                    automaticNodes.Add(new WorkflowAutomaticRuntimeNode(node.NodeKey, node.NodeTypeKey, [], null));
                    current = outgoing[node.NodeKey][0];
                    break;
                case "gateway.exclusive":
                    if (!gateways.TryGetValue(node.NodeKey, out var gateway) ||
                        !gateway.TrySelectBranch(values, out var selection))
                    {
                        return false;
                    }

                    automaticNodes.Add(new WorkflowAutomaticRuntimeNode(
                        node.NodeKey,
                        node.NodeTypeKey,
                        [],
                        selection.BranchKey));
                    current = selection.NextNodeKey;
                    break;
                case "gateway.parallel":
                    if (parallelForks.TryGetValue(node.NodeKey, out var fork))
                    {
                        automaticNodes.Add(new WorkflowAutomaticRuntimeNode(
                            node.NodeKey,
                            node.NodeTypeKey,
                            [],
                            fork.JoinNodeKey));
                        var branchPlans = new List<WorkflowParallelBranchPlan>(fork.Branches.Count);
                        foreach (var branch in fork.Branches)
                        {
                            if (!TryTraverseBranch(
                                    branch.BranchKey,
                                    branch.NextNodeKey,
                                    values,
                                    fork.JoinNodeKey!,
                                    out var branchPlan))
                            {
                                return false;
                            }

                            branchPlans.Add(branchPlan);
                        }

                        transition = new WorkflowRuntimeTransition(
                            null,
                            false,
                            automaticNodes,
                            parallelFork: new WorkflowParallelForkPlan(
                                node.NodeKey,
                                fork.JoinNodeKey!,
                                branchPlans));
                        return true;
                    }

                    if (!parallelJoins.TryGetValue(node.NodeKey, out var join))
                    {
                        return false;
                    }

                    // 并行分支到达汇合点时只记录到达事实，必须等待其他分支完成后才能继续。
                    if (stopAtJoinNodeKey is not null &&
                        string.Equals(node.NodeKey, stopAtJoinNodeKey, StringComparison.Ordinal))
                    {
                        transition = new WorkflowRuntimeTransition(
                            null,
                            false,
                            automaticNodes,
                            joinArrival: new WorkflowJoinArrivalPlan(
                                node.NodeKey,
                                join.ForkNodeKey!,
                                string.Empty,
                                automaticNodes.ToArray()));
                        return true;
                    }

                    if (outgoing[node.NodeKey] is not [var joinNext])
                    {
                        return false;
                    }

                    automaticNodes.Add(new WorkflowAutomaticRuntimeNode(
                        node.NodeKey,
                        node.NodeTypeKey,
                        [],
                        "joined"));
                    current = joinNext;
                    break;
                case "gateway.inclusive":
                    if (inclusiveForks.TryGetValue(node.NodeKey, out var inclusiveFork))
                    {
                        if (!inclusiveFork.TrySelectBranches(values, out var selections))
                        {
                            return false;
                        }

                        automaticNodes.Add(new WorkflowAutomaticRuntimeNode(
                            node.NodeKey,
                            node.NodeTypeKey,
                            [],
                            string.Join(',', selections.Select(selection => selection.BranchKey))));
                        var inclusiveBranchPlans = new List<WorkflowParallelBranchPlan>(selections.Count);
                        foreach (var inclusiveSelection in selections)
                        {
                            if (!TryTraverseBranch(
                                    inclusiveSelection.BranchKey,
                                    inclusiveSelection.NextNodeKey,
                                    values,
                                    inclusiveFork.JoinNodeKey!,
                                    out var branchPlan))
                            {
                                return false;
                            }

                            inclusiveBranchPlans.Add(branchPlan);
                        }

                        transition = new WorkflowRuntimeTransition(
                            null,
                            false,
                            automaticNodes,
                            parallelFork: new WorkflowParallelForkPlan(
                                node.NodeKey,
                                inclusiveFork.JoinNodeKey!,
                                inclusiveBranchPlans,
                                "inclusive"));
                        return true;
                    }

                    if (!inclusiveJoins.TryGetValue(node.NodeKey, out var inclusiveJoin))
                    {
                        return false;
                    }

                    if (stopAtJoinNodeKey is not null &&
                        string.Equals(node.NodeKey, stopAtJoinNodeKey, StringComparison.Ordinal))
                    {
                        transition = new WorkflowRuntimeTransition(
                            null,
                            false,
                            automaticNodes,
                            joinArrival: new WorkflowJoinArrivalPlan(
                                node.NodeKey,
                                inclusiveJoin.ForkNodeKey!,
                                string.Empty,
                                automaticNodes.ToArray()));
                        return true;
                    }

                    if (outgoing[node.NodeKey] is not [var inclusiveJoinNext])
                    {
                        return false;
                    }

                    automaticNodes.Add(new WorkflowAutomaticRuntimeNode(
                        node.NodeKey,
                        node.NodeTypeKey,
                        [],
                        "joined"));
                    current = inclusiveJoinNext;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>解析单个并行分支从入口到首个等待点或汇合点的计划。</summary>
    /// <param name="branchKey">稳定分支键。</param>
    /// <param name="initialNodeKey">分支入口节点键。</param>
    /// <param name="values">实例绑定且已验证的表单值。</param>
    /// <param name="joinNodeKey">所属汇合节点键。</param>
    /// <param name="branchPlan">解析后的分支计划。</param>
    /// <returns>分支路径可闭合时返回 <see langword="true"/>。</returns>
    private bool TryTraverseBranch(
        string branchKey,
        string initialNodeKey,
        IReadOnlyDictionary<string, JsonElement> values,
        string joinNodeKey,
        out WorkflowParallelBranchPlan branchPlan)
    {
        branchPlan = default!;
        if (!TryTraverse(initialNodeKey, values, joinNodeKey, out var transition))
        {
            return false;
        }

        if (transition.JoinArrival is { } joinArrival)
        {
            branchPlan = new WorkflowParallelBranchPlan(
                branchKey,
                null,
                false,
                joinArrival.TrailingAutomaticNodes,
                null,
                null,
                WorkflowAssigneePolicy.CreateDefault(),
                joinArrival with { BranchKey = branchKey });
            return true;
        }

        branchPlan = new WorkflowParallelBranchPlan(
            branchKey,
            transition.NextHumanNodeKey,
            transition.CompletesInstance,
            transition.AutomaticNodes,
            transition.TimeoutPolicy,
            transition.ApprovalPolicy,
            transition.AssigneePolicy,
            ParallelFork: transition.ParallelFork,
            TaskPolicy: transition.TaskPolicy, VotePolicy: transition.VotePolicy, ClaimPolicy: transition.ClaimPolicy,
            AutomaticWait: transition.AutomaticWait, TerminalNodeKey: transition.TerminalNodeKey);
        return true;
    }

    /// <summary>验证分叉与汇合节点成对出现且互相引用一致。</summary>
    /// <param name="parallelForks">分叉定义索引。</param>
    /// <param name="parallelJoins">汇合定义索引。</param>
    /// <returns>全部并行网关成对闭合时返回 <see langword="true"/>。</returns>
    private static bool ValidateParallelPairs(
        IReadOnlyDictionary<string, WorkflowParallelGatewayDefinition> parallelForks,
        IReadOnlyDictionary<string, WorkflowParallelGatewayDefinition> parallelJoins)
    {
        foreach (var (forkKey, fork) in parallelForks)
        {
            if (fork.JoinNodeKey is not { } joinNodeKey ||
                !parallelJoins.TryGetValue(joinNodeKey, out var join) ||
                !string.Equals(join.ForkNodeKey, forkKey, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return parallelJoins.Values.All(join =>
            join.ForkNodeKey is { } forkNodeKey && parallelForks.ContainsKey(forkNodeKey));
    }

    /// <summary>验证包容分叉与汇合节点成对出现且互相引用一致。</summary>
    /// <param name="inclusiveForks">包容分叉定义索引。</param>
    /// <param name="inclusiveJoins">包容汇合定义索引。</param>
    /// <returns>全部包容网关成对闭合时返回 <see langword="true"/>。</returns>
    private static bool ValidateInclusivePairs(
        IReadOnlyDictionary<string, WorkflowInclusiveGatewayDefinition> inclusiveForks,
        IReadOnlyDictionary<string, WorkflowInclusiveGatewayDefinition> inclusiveJoins)
    {
        foreach (var (forkKey, fork) in inclusiveForks)
        {
            if (fork.JoinNodeKey is not { } joinNodeKey ||
                !inclusiveJoins.TryGetValue(joinNodeKey, out var join) ||
                !string.Equals(join.ForkNodeKey, forkKey, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return inclusiveJoins.Values.All(join =>
            join.ForkNodeKey is { } forkNodeKey && inclusiveForks.ContainsKey(forkNodeKey));
    }

    /// <summary>解析单个节点的出口与自动节点配置。</summary>
    /// <param name="node">待解析节点。</param>
    /// <param name="formSchema">可选的表单架构。</param>
    /// <param name="outgoing">正在构建的出口索引。</param>
    /// <param name="gateways">正在构建的排他网关索引。</param>
    /// <param name="parallelForks">正在构建的并行分叉索引。</param>
    /// <param name="parallelJoins">正在构建的并行汇合索引。</param>
    /// <param name="inclusiveForks">正在构建的包容分叉索引。</param>
    /// <param name="inclusiveJoins">正在构建的包容汇合索引。</param>
    /// <returns>节点类型和配置均受当前执行器支持时返回 <see langword="true"/>。</returns>
    private static bool TryReadNode(
        WorkflowNodeDraft node,
        WorkflowFormSchema? formSchema,
        IDictionary<string, string[]> outgoing,
        IDictionary<string, WorkflowExclusiveGatewayDefinition> gateways,
        IDictionary<string, WorkflowParallelGatewayDefinition> parallelForks,
        IDictionary<string, WorkflowParallelGatewayDefinition> parallelJoins,
        IDictionary<string, WorkflowInclusiveGatewayDefinition> inclusiveForks,
        IDictionary<string, WorkflowInclusiveGatewayDefinition> inclusiveJoins)
    {
        switch (node.NodeTypeKey)
        {
            case WorkflowDynamicRoutePolicy.NodeType:
                if (!TryReadSingleNext(node.Config, out var routeNext) || !WorkflowDynamicRoutePolicy.TryRead(node.Config, formSchema, out _)) return false;
                outgoing.Add(node.NodeKey, [routeNext!]); return true;
            case WorkflowSubprocessPolicy.NodeType:
                if (!TryReadSingleNext(node.Config, out var childNext) || !WorkflowSubprocessPolicy.TryRead(node.Config, formSchema, out var childPolicy)) return false;
                outgoing.Add(node.NodeKey, childPolicy.Timeout?.NextNodeKey is { } timeoutNext ? new[] { childNext!, timeoutNext }.Distinct(StringComparer.Ordinal).ToArray() : [childNext!]); return true;
            case "business.command":
                if (!TryReadSingleNext(node.Config, out var businessNext) || !WorkflowBusinessCommandPolicy.TryRead(node.NodeTypeKey, node.Config, out _)) return false;
                outgoing.Add(node.NodeKey, [businessNext!]); return true;
            case "integration.trigger":
            case "integration.async_trigger":
                if (!TryReadSingleNext(node.Config, out var connectorNext) ||
                    !WorkflowConnectorPolicy.TryRead(node.Config, formSchema, out _)) return false;
                outgoing.Add(node.NodeKey, [connectorNext!]);
                return true;
            case "data.modify":
            case "data.clear":
                if (!TryReadSingleNext(node.Config, out var fieldNext) ||
                    !WorkflowFieldMutationPolicy.TryRead(node.NodeTypeKey, node.Config, formSchema, out _)) return false;
                outgoing.Add(node.NodeKey, [fieldNext!]);
                return true;
            case "timer.delay":
                if (!TryReadSingleNext(node.Config, out var delayNext) || !WorkflowDelayPolicy.TryRead(node.Config, formSchema, out _)) return false;
                outgoing.Add(node.NodeKey, [delayNext!]);
                return true;
            case "start":
                if (!WorkflowWithdrawalPolicy.TryRead(node.Config, out _)) return false;
                goto case "control.empty";
            case "control.empty":
            case "human.approval":
            case "human.group_approval":
                if (!TryReadSingleNext(node.Config, out var nextNodeKey) ||
                    (node.NodeTypeKey == WorkflowHumanActivityKinds.GroupApproval && !WorkflowGroupApprovalConfiguration.TryRead(node.Config, out _)) ||
                    !WorkflowHumanActionPolicy.TryRead(node.Config, out _) ||
                    !WorkflowApprovalPolicy.TryRead(node.Config, out _) ||
                    !WorkflowAssigneePolicy.TryRead(node.Config, out _))
                {
                    return false;
                }

                outgoing.Add(node.NodeKey, [nextNodeKey!]);
                return true;
            case "human.claim":
                if (!TryReadSingleNext(node.Config, out var claimNext) || !WorkflowClaimPolicy.TryRead(node.Config, out _) ||
                    !WorkflowAssigneePolicy.TryRead(node.Config, out _) ||
                    !WorkflowHumanActionPolicy.TryRead(node.Config, out _, WorkflowHumanActivityKinds.Claim) ||
                    !WorkflowTodoTimeoutPolicy.TryRead(node.Config, out _)) return false;
                outgoing.Add(node.NodeKey, [claimNext!]);
                return true;
            case "human.vote":
                if (!TryReadSingleNext(node.Config, out var voteNext) || !WorkflowVotePolicy.TryRead(node.Config, out _) ||
                    !WorkflowAssigneePolicy.TryRead(node.Config, out _) ||
                    !WorkflowHumanActionPolicy.TryRead(node.Config, out _, WorkflowHumanActivityKinds.Vote) ||
                    !WorkflowTodoTimeoutPolicy.TryRead(node.Config, out _)) return false;
                outgoing.Add(node.NodeKey, [voteNext!]);
                return true;
            case "human.task":
                if (!TryReadSingleNext(node.Config, out var taskNext) ||
                    !WorkflowTaskPolicy.TryRead(node.Config, out _) ||
                    !WorkflowAssigneePolicy.TryRead(node.Config, out _) ||
                    !WorkflowHumanActionPolicy.TryRead(node.Config, out _, WorkflowHumanActivityKinds.Task) ||
                    !WorkflowTodoTimeoutPolicy.TryRead(node.Config, out _)) return false;
                outgoing.Add(node.NodeKey, [taskNext!]);
                return true;
            case "notify.cc":
                if (!TryReadSingleNext(node.Config, out nextNodeKey) ||
                    !WorkflowCcNodeConfiguration.TryReadPolicy(node.Config, out _))
                {
                    return false;
                }

                outgoing.Add(node.NodeKey, [nextNodeKey!]);
                return true;
            case "gateway.exclusive":
                if (!WorkflowExclusiveGatewayConfiguration.TryRead(node.Config, formSchema, out var gateway))
                {
                    return false;
                }

                var parsedGateway = gateway!;
                gateways.Add(node.NodeKey, parsedGateway);
                outgoing.Add(
                    node.NodeKey,
                    parsedGateway.Branches.Select(branch => branch.NextNodeKey)
                        .Append(parsedGateway.DefaultNextNodeKey)
                        .ToArray());
                return true;
            case "gateway.parallel":
                if (!WorkflowParallelGatewayConfiguration.TryRead(node.Config, out var parallel))
                {
                    return false;
                }

                if (parallel!.Role == WorkflowParallelGatewayRole.Fork)
                {
                    parallelForks.Add(node.NodeKey, parallel);
                    outgoing.Add(
                        node.NodeKey,
                        parallel.Branches.Select(branch => branch.NextNodeKey).ToArray());
                    return true;
                }

                parallelJoins.Add(node.NodeKey, parallel);
                outgoing.Add(node.NodeKey, [parallel.NextNodeKey!]);
                return true;
            case "gateway.inclusive":
                if (!WorkflowInclusiveGatewayConfiguration.TryRead(node.Config, formSchema, out var inclusive))
                {
                    return false;
                }

                if (inclusive!.Role == WorkflowInclusiveGatewayRole.Fork)
                {
                    inclusiveForks.Add(node.NodeKey, inclusive);
                    outgoing.Add(
                        node.NodeKey,
                        inclusive.Branches.Select(branch => branch.NextNodeKey)
                            .Append(inclusive.DefaultNextNodeKey)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray());
                    return true;
                }

                inclusiveJoins.Add(node.NodeKey, inclusive);
                outgoing.Add(node.NodeKey, [inclusive.DefaultNextNodeKey]);
                return true;
            case "end":
                if (!HasNoNext(node.Config))
                {
                    return false;
                }

                outgoing.Add(node.NodeKey, []);
                return true;
            default:
                return false;
        }
    }

    /// <summary>验证每条静态路径无环、可达终点且至少经过一个人工审批。</summary>
    /// <param name="nodeKey">当前节点键。</param>
    /// <param name="hasApproval">当前路径是否已经过人工审批。</param>
    /// <param name="nodes">节点索引。</param>
    /// <param name="outgoing">出口索引。</param>
    /// <param name="reachable">所有已到达节点集合。</param>
    /// <param name="activePath">当前递归路径集合。</param>
    /// <param name="validatedStates">已经验证的节点与审批状态组合，避免汇合图重复展开。</param>
    /// <returns>当前节点下全部路径均闭合时返回 <see langword="true"/>。</returns>
    private static bool ValidatePath(
        string nodeKey,
        bool hasApproval,
        IReadOnlyDictionary<string, WorkflowNodeDraft> nodes,
        IReadOnlyDictionary<string, string[]> outgoing,
        ISet<string> reachable,
        ISet<string> activePath,
        ISet<(string NodeKey, bool HasApproval)> validatedStates)
    {
        if (!nodes.TryGetValue(nodeKey, out var node) || activePath.Contains(nodeKey))
        {
            return false;
        }

        reachable.Add(nodeKey);
        if (validatedStates.Contains((nodeKey, hasApproval)))
        {
            return true;
        }

        activePath.Add(nodeKey);
        var pathHasApproval = hasApproval || GuaranteesHumanDecision(node);
        var valid = node.NodeTypeKey == "end"
            ? pathHasApproval
            : outgoing[nodeKey].Length > 0 && outgoing[nodeKey].All(next =>
                ValidatePath(
                    next,
                    pathHasApproval,
                    nodes,
                    outgoing,
                    reachable,
                    activePath,
                    validatedStates));
        activePath.Remove(nodeKey);
        if (valid)
        {
            validatedStates.Add((nodeKey, hasApproval));
        }

        return valid;
    }

    /// <summary>成功路径可能自动通过或跳过的审批不能满足人审屏障；投票零有效成员只挂起或拒绝。</summary>
    internal static bool GuaranteesHumanDecision(WorkflowNodeDraft node)
    {
        if (node.NodeTypeKey == WorkflowSubprocessPolicy.NodeType)
            return WorkflowSubprocessPolicy.TryRead(node.Config, null, out var child) &&
                child.Timeout is null && child.Multi?.EmptyModeKey != "skip" && node.Config.TryGetProperty("boundSubprocess", out var data) && WorkflowBoundSubprocess.TryRead(data, child.DefinitionVersionId, out var binding) && binding.HasHumanApproval;
        if (!WorkflowHumanActivityKinds.IsHumanDecision(node.NodeTypeKey)) return false;
        if (node.NodeTypeKey == WorkflowHumanActivityKinds.Vote) return true;
        if (node.NodeTypeKey == WorkflowHumanActivityKinds.GroupApproval)
            return WorkflowGroupApprovalConfiguration.TryRead(node.Config, out var groups) &&
                groups!.RequiredGroups > groups.Groups.Count(group => MayPassWithoutHuman(group.AssigneePolicy));
        return WorkflowAssigneePolicy.TryRead(node.Config, out var policy) &&
            !MayPassWithoutHuman(policy);
    }

    private static bool MayPassWithoutHuman(WorkflowAssigneePolicy policy) => policy.Handling is { } handling &&
        (handling.WhenEmpty.ActionKey == "auto_pass" || handling.WhenInitiator.ActionKey == "skip");

    /// <summary>从节点配置读取唯一后继节点键。</summary>
    /// <param name="config">节点配置 JSON。</param>
    /// <param name="nextNodeKey">唯一后继节点键。</param>
    /// <returns>配置恰好包含一个非空字符串后继时返回 <see langword="true"/>。</returns>
    private static bool TryReadSingleNext(JsonElement config, out string? nextNodeKey)
    {
        nextNodeKey = null;
        if (config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty("nextNodeKeys", out var keys) ||
            keys.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var values = keys.EnumerateArray().ToArray();
        if (values is not [var value] || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        nextNodeKey = value.GetString();
        return !string.IsNullOrWhiteSpace(nextNodeKey);
    }

    /// <summary>确认终点配置没有后继节点。</summary>
    /// <param name="config">终点配置 JSON。</param>
    /// <returns>后继字段缺失或为空数组时返回 <see langword="true"/>。</returns>
    private static bool HasNoNext(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return !config.TryGetProperty("nextNodeKeys", out var keys) ||
               keys.ValueKind == JsonValueKind.Array && !keys.EnumerateArray().Any();
    }
}

/// <summary>描述一次迁移中按路径顺序执行的自动节点。</summary>
/// <param name="NodeKey">稳定节点键。</param>
/// <param name="NodeTypeKey">自动节点类型机器键。</param>
/// <param name="RecipientUserIds">抄送节点的收件人；其他节点为空。</param>
/// <param name="OutcomeKey">网关命中的分支键或汇合节点键；其他节点为空。</param>
internal sealed record WorkflowAutomaticRuntimeNode(
    string NodeKey,
    string NodeTypeKey,
    IReadOnlyList<Guid> RecipientUserIds,
    string? OutcomeKey,
    WorkflowAssigneePolicy? RecipientPolicy = null,
    string? ResolutionJson = null,
    string? ResolutionHash = null);

/// <summary>描述一次审批边界之间需要同步落库的抄送节点。</summary>
/// <param name="NodeKey">稳定抄送节点键。</param>
/// <param name="RecipientUserIds">经过编译校验的收件人用户标识。</param>
internal sealed record WorkflowCcRuntimeNode(
    string NodeKey,
    IReadOnlyList<Guid> RecipientUserIds,
    string? ResolutionJson = null,
    string? ResolutionHash = null);

/// <summary>描述启动或审批通过后到下一等待点的闭合迁移。</summary>
internal readonly record struct WorkflowRuntimeTransition
{
    /// <summary>创建一次闭合运行迁移。</summary>
    /// <param name="nextHumanNodeKey">下一人工审批节点；流程结束时为空。</param>
    /// <param name="completesInstance">迁移完成后是否结束实例。</param>
    /// <param name="automaticNodes">到达下一等待点前按顺序执行的自动节点。</param>
    /// <param name="timeoutPolicy">下一人工审批节点发布时固化的超时策略。</param>
    /// <param name="approvalPolicy">下一人工审批节点发布时固化的多人审批策略。</param>
    /// <param name="assigneePolicy">下一人工审批节点发布时固化的办理人解析策略。</param>
    /// <param name="parallelFork">并行分叉计划；存在时表示需要同时激活多个分支。</param>
    /// <param name="joinArrival">并行分支到达汇合点计划；存在时表示当前分支需要等待其他分支。</param>
    /// <param name="terminalNodeKey">本条实际路径选中的终点身份；非结束迁移为空。</param>
    public WorkflowRuntimeTransition(
        string? nextHumanNodeKey,
        bool completesInstance,
        IReadOnlyList<WorkflowAutomaticRuntimeNode> automaticNodes,
        WorkflowTodoTimeoutPolicy? timeoutPolicy = null,
        WorkflowApprovalPolicy? approvalPolicy = null,
        WorkflowAssigneePolicy? assigneePolicy = null,
        WorkflowParallelForkPlan? parallelFork = null,
        WorkflowJoinArrivalPlan? joinArrival = null,
        WorkflowTaskPolicy? taskPolicy = null,
        WorkflowVotePolicy? votePolicy = null,
        WorkflowClaimPolicy? claimPolicy = null,
        WorkflowAutomaticWaitPlan? automaticWait = null,
        string? terminalNodeKey = null)
    {
        NextHumanNodeKey = nextHumanNodeKey;
        CompletesInstance = completesInstance;
        AutomaticNodes = automaticNodes;
        TimeoutPolicy = timeoutPolicy;
        ApprovalPolicy = approvalPolicy;
        AssigneePolicy = assigneePolicy ?? WorkflowAssigneePolicy.CreateDefault();
        ParallelFork = parallelFork;
        JoinArrival = joinArrival;
        TaskPolicy = taskPolicy;
        VotePolicy = votePolicy;
        ClaimPolicy = claimPolicy;
        AutomaticWait = automaticWait;
        TerminalNodeKey = terminalNodeKey;
    }

    /// <summary>获取下一人工审批节点；流程结束时为空。</summary>
    public string? NextHumanNodeKey { get; }

    /// <summary>获取迁移完成后是否结束实例。</summary>
    public bool CompletesInstance { get; }

    /// <summary>由固定定义选中的真实终点；不能只凭布尔结束标记猜测多个终点中的身份。</summary>
    public string? TerminalNodeKey { get; }

    /// <summary>获取到达下一等待点前按顺序执行的自动节点。</summary>
    public IReadOnlyList<WorkflowAutomaticRuntimeNode> AutomaticNodes { get; }

    /// <summary>获取下一审批待办固化的超时策略；未配置时为空。</summary>
    public WorkflowTodoTimeoutPolicy? TimeoutPolicy { get; }

    /// <summary>获取下一审批等待点的多人审批策略；为空时沿用旧单人办理语义。</summary>
    public WorkflowApprovalPolicy? ApprovalPolicy { get; }

    /// <summary>办理活动独立的提交完成规则；不会转换为审批票。</summary>
    public WorkflowTaskPolicy? TaskPolicy { get; }
    /// <summary>独立投票的固定规则，与审批席位门槛和普通办理完成规则分开。</summary>
    public WorkflowVotePolicy? VotePolicy { get; }
    /// <summary>领取池的释放规则；候选尚未领取时不产生个人待办。</summary>
    public WorkflowClaimPolicy? ClaimPolicy { get; }

    /// <summary>不创建人工待办的耐久自动等待点。</summary>
    public WorkflowAutomaticWaitPlan? AutomaticWait { get; }

    /// <summary>获取下一审批等待点固化的办理人解析策略。</summary>
    public WorkflowAssigneePolicy AssigneePolicy { get; }

    /// <summary>获取并行分叉计划。</summary>
    public WorkflowParallelForkPlan? ParallelFork { get; }

    /// <summary>获取并行分支到达汇合点计划。</summary>
    public WorkflowJoinArrivalPlan? JoinArrival { get; }

    /// <summary>获取当前迁移是否在汇合点等待其他分支。</summary>
    public bool WaitsAtJoin => JoinArrival is not null;

    /// <summary>获取兼容现有抄送写入器的抄送节点投影。</summary>
    public IReadOnlyList<WorkflowCcRuntimeNode> CcNodes =>
        AutomaticNodes
            .Where(node => node.NodeTypeKey == "notify.cc")
            .Select(node => new WorkflowCcRuntimeNode(node.NodeKey, node.RecipientUserIds, node.ResolutionJson, node.ResolutionHash))
            .ToArray();
}
