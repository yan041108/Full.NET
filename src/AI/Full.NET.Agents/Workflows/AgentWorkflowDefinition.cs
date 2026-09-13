namespace Full.NET.Agents.Workflows;

/// <summary>版本化显式工作流定义；节点与边静态白名单。</summary>
public sealed class AgentWorkflowDefinition
{
    /// <summary>创建线性工作流定义。</summary>
    public AgentWorkflowDefinition(string key, int version, IReadOnlyList<AgentWorkflowNode> nodes)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
            throw new ArgumentException("Workflow key is invalid.", nameof(key));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version));
        if (nodes.Count == 0)
            throw new ArgumentException("Workflow must contain at least one node.", nameof(nodes));

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!keys.Add(node.Key))
                throw new ArgumentException($"Duplicate workflow node key '{node.Key}'.", nameof(nodes));
            ValidateNode(node);
        }

        Key = key;
        Version = version;
        Nodes = nodes;
    }

    /// <summary>工作流键。</summary>
    public string Key { get; }

    /// <summary>工作流版本。</summary>
    public int Version { get; }

    /// <summary>有序节点列表。</summary>
    public IReadOnlyList<AgentWorkflowNode> Nodes { get; }

    private static void ValidateNode(AgentWorkflowNode node)
    {
        switch (node.Kind)
        {
            case AgentWorkflowNodeKind.ToolRead or AgentWorkflowNodeKind.ToolWrite:
                if (string.IsNullOrWhiteSpace(node.ToolName))
                    throw new ArgumentException($"Tool node '{node.Key}' requires a tool name.");
                break;
            case AgentWorkflowNodeKind.ModelText:
                if (string.IsNullOrWhiteSpace(node.PromptTemplate))
                    throw new ArgumentException($"Model node '{node.Key}' requires a prompt template.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(node), node.Kind, "Unsupported workflow node kind.");
        }
    }
}
