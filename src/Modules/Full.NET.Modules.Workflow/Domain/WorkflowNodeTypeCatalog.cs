namespace Full.NET.Modules.Workflow.Domain;

internal sealed record WorkflowNodeTypeDefinition(
    string NodeTypeKey,
    int NodeSchemaVersion,
    bool Designable,
    bool Publishable,
    bool Executable,
    bool SupportsFieldPolicies);

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
        Node("notify.cc"),
        Node("gateway.exclusive"),
        Node("end"),
    ];

    private static readonly IReadOnlyDictionary<string, WorkflowNodeTypeDefinition> ByNodeType =
        Definitions.ToDictionary(definition => definition.NodeTypeKey, StringComparer.Ordinal);

    public static WorkflowNodeTypeCatalogSnapshot Current { get; } =
        new(1, 1, Array.AsReadOnly(Definitions));

    public static bool TryGet(string? nodeTypeKey, out WorkflowNodeTypeDefinition? definition)
    {
        definition = null;
        return nodeTypeKey is not null && ByNodeType.TryGetValue(nodeTypeKey, out definition);
    }

    private static WorkflowNodeTypeDefinition Node(
        string nodeTypeKey,
        bool supportsFieldPolicies = false) =>
        new(nodeTypeKey, 1, true, true, true, supportsFieldPolicies);
}
