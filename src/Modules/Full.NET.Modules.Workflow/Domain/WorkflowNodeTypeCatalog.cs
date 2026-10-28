namespace Full.NET.Modules.Workflow.Domain;

internal sealed record WorkflowNodeTypeDefinition(
    string NodeTypeKey,
    int NodeSchemaVersion,
    bool Designable,
    bool Publishable,
    bool Executable,
    bool SupportsFieldPolicies)
{
    public IReadOnlyList<string> ConfigurationProperties => NodeTypeKey switch
    {
        "start" => ["nodeName", "nextNodeKeys", "withdrawalPolicy", "administratorUserIds"],
        "human.approval" => ["nodeName", "nextNodeKeys", "fieldPolicies", "approvalPolicy", "assigneePolicy", "timeoutPolicy", "slaPolicy", "resultCallbackFieldKey", "listeners", "operationPermissions", "signRequired", "commentRequired"],
        "human.group_approval" => ["nodeName", "nextNodeKeys", "fieldPolicies", "groups", "groupApprovalPolicy", "timeoutPolicy", "slaPolicy", "resultCallbackFieldKey", "listeners", "operationPermissions", "signRequired", "commentRequired"],
        "human.task" => ["nodeName", "nextNodeKeys", "fieldPolicies", "taskPolicy", "assigneePolicy", "timeoutPolicy", "slaPolicy", "resultCallbackFieldKey", "listeners", "operationPermissions", "signRequired", "commentRequired"],
        "human.vote" => ["nodeName", "nextNodeKeys", "fieldPolicies", "votePolicy", "assigneePolicy", "timeoutPolicy", "slaPolicy", "resultCallbackFieldKey", "listeners", "operationPermissions", "signRequired", "commentRequired"],
        "human.claim" => ["nodeName", "nextNodeKeys", "fieldPolicies", "claimPolicy", "assigneePolicy", "timeoutPolicy", "slaPolicy", "resultCallbackFieldKey", "listeners", "operationPermissions", "signRequired", "commentRequired"],
        "notify.cc" => ["nodeName", "nextNodeKeys", "recipientUserIds", "recipientPolicy"],
        "timer.delay" => ["nodeName", "nextNodeKeys", "delayPolicy"],
        "business.command" => ["nodeName", "nextNodeKeys", "businessCommandPolicy", "boundBusinessActionHash"],
        "process.subprocess" => ["nodeName", "nextNodeKeys", "fieldPolicies", "subprocessPolicy", "boundSubprocess", "multiInstancePolicy", "starterPolicy", "childTimeoutPolicy"],
        "control.route" => ["nodeName", "nextNodeKeys", "routePolicy"],
        "data.modify" or "data.clear" => ["nodeName", "nextNodeKeys", "fieldPolicies", "mutationPolicy"],
        "integration.trigger" or "integration.async_trigger" => ["nodeName", "nextNodeKeys", "fieldPolicies", "connectorPolicy", "boundConnectorHash"],
        "gateway.exclusive" => ["nodeName", "nextNodeKeys", "branches", "defaultNextNodeKey"],
        "gateway.parallel" => ["nodeName", "gatewayRoleKey", "joinNodeKey", "forkNodeKey", "nextNodeKeys", "branches"],
        "gateway.inclusive" => ["nodeName", "gatewayRoleKey", "joinNodeKey", "forkNodeKey", "nextNodeKeys", "branches", "defaultNextNodeKey"],
        _ => ["nodeName", "nextNodeKeys"],
    };
}

internal sealed record WorkflowNodeTypeCatalogSnapshot(
    int CatalogVersion,
    int DefinitionSchemaVersion,
    IReadOnlyList<WorkflowNodeTypeDefinition> NodeTypes);

/// <summary>定义当前部署可设计、可发布并可执行的闭合工作流节点目录。</summary>
internal static class WorkflowNodeTypeCatalog
{
    private static readonly WorkflowNodeTypeDefinition[] Definitions =
    [
        Node("start"),
        Node("human.approval", supportsFieldPolicies: true),
        Node("human.group_approval", supportsFieldPolicies: true),
        Node("human.task", supportsFieldPolicies: true),
        Node("human.vote", supportsFieldPolicies: true),
        Node("human.claim", supportsFieldPolicies: true),
        Node("notify.cc"),
        Node("control.empty"),
        Node(WorkflowDynamicRoutePolicy.NodeType),
        Node("timer.delay"),
        Node("business.command"),
        Node(WorkflowSubprocessPolicy.NodeType, supportsFieldPolicies: true),
        Node("data.modify", supportsFieldPolicies: true),
        Node("data.clear", supportsFieldPolicies: true),
        Node("integration.trigger", supportsFieldPolicies: true),
        Node("integration.async_trigger", supportsFieldPolicies: true),
        Node("gateway.exclusive"),
        Node("gateway.parallel"),
        Node("gateway.inclusive"),
        Node("end"),
    ];

    private static readonly IReadOnlyDictionary<string, WorkflowNodeTypeDefinition> ByNodeType =
        Definitions.ToDictionary(definition => definition.NodeTypeKey, StringComparer.Ordinal);

    public static WorkflowNodeTypeCatalogSnapshot Current { get; } =
        new(29, 1, Array.AsReadOnly(Definitions));

    public static bool TryGet(string? nodeTypeKey, out WorkflowNodeTypeDefinition? definition)
    {
        definition = null;
        return nodeTypeKey is not null && ByNodeType.TryGetValue(nodeTypeKey, out definition);
    }

    public static bool SupportsConfiguration(WorkflowNodeDraft node) =>
        node.Config.ValueKind == System.Text.Json.JsonValueKind.Object &&
        (node.NodeTypeKey is not ("start" or "end" or "human.approval" or "human.group_approval" or "human.task" or "human.vote" or "human.claim" or "control.empty" or "control.route" or "timer.delay" or "business.command" or "process.subprocess" or "data.modify" or "data.clear" or "integration.trigger" or "integration.async_trigger") ||
            (TryGet(node.NodeTypeKey, out var type) && node.Config.EnumerateObject().All(property =>
                type!.ConfigurationProperties.Contains(property.Name, StringComparer.Ordinal))));

    private static WorkflowNodeTypeDefinition Node(
        string nodeTypeKey,
        bool publishable = true,
        bool executable = true,
        bool supportsFieldPolicies = false) =>
        new(nodeTypeKey, 1, true, publishable, executable, supportsFieldPolicies);
}
