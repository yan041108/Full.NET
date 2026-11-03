namespace Full.NET.Agents.Workflows;

/// <summary>版本化显式工作流定义；节点与边静态白名单。</summary>
public sealed class AgentWorkflowDefinition
{
    /// <summary>创建静态有界工作流；组外顺序执行，连续模型组有界并行。</summary>
    public AgentWorkflowDefinition(string key, int version, IReadOnlyList<AgentWorkflowNode> nodes)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
            throw new ArgumentException("Workflow key is invalid.", nameof(key));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Count is < 1 or > 64)
            throw new ArgumentException("Workflow must contain 1 to 64 nodes.", nameof(nodes));

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Key) || node.Key.Length > 64
                || node.Key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
                throw new ArgumentException("Workflow node identity is invalid.", nameof(nodes));
            if (!keys.Add(node.Key))
                throw new ArgumentException($"Duplicate workflow node key '{node.Key}'.", nameof(nodes));
            ValidateNode(node);
        }

        Key = key;
        Version = version;
        // 调用方传入的列表不可在目录冻结后改变拓扑和节点摘要。
        Nodes = Array.AsReadOnly(nodes.ToArray());
        var groups = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Nodes.Count; index++)
        {
            var start = InputPrefixIndex(index);
            if (Nodes[index].ParallelGroup is { } group)
            {
                if (group.Length is < 1 or > 64 || group.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_'))
                    || Nodes[index].Kind != AgentWorkflowNodeKind.ModelText)
                    throw new ArgumentException("Parallel groups only contain bounded model nodes.", nameof(nodes));
                if (start == index)
                {
                    if (!groups.Add(group) || GroupEndIndex(index) - index is < 2 or > 4)
                        throw new ArgumentException("Parallel groups must be contiguous and contain 2 to 4 nodes.", nameof(nodes));
                }
            }
            // 只识别原始模板中的占位符；组内结果不构成彼此输入，不能用完成顺序改变 Prompt。
            var template = Nodes[index].PromptTemplate ?? string.Empty;
            for (var offset = template.IndexOf("{{", StringComparison.Ordinal); offset >= 0;
                offset = template.IndexOf("{{", offset + 2, StringComparison.Ordinal))
            {
                var end = template.IndexOf("}}", offset + 2, StringComparison.Ordinal);
                if (end < 0 || !Nodes.Take(start).Any(node => node.Key == template[(offset + 2)..end]))
                    throw new ArgumentException("Workflow prompts must reference prior completed stages.", nameof(nodes));
            }
        }
    }

    /// <summary>工作流键。</summary>
    public string Key { get; }

    /// <summary>工作流版本。</summary>
    public int Version { get; }

    /// <summary>有序节点列表。</summary>
    public IReadOnlyList<AgentWorkflowNode> Nodes { get; }

    /// <summary>当前节点的输入只来自此前完整阶段；同组分支共享相同前缀。</summary>
    /// <param name="index">节点序号。</param>
    public int InputPrefixIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Nodes.Count);
        var group = Nodes[index].ParallelGroup;
        while (group is not null && index > 0 && Nodes[index - 1].ParallelGroup == group) index--;
        return index;
    }

    /// <summary>当前静态阶段的结束序号，不含该序号。</summary>
    /// <param name="index">阶段内节点序号。</param>
    public int GroupEndIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Nodes.Count);
        var group = Nodes[index].ParallelGroup;
        var end = index + 1;
        while (group is not null && end < Nodes.Count && Nodes[end].ParallelGroup == group) end++;
        return end;
    }

    private static void ValidateNode(AgentWorkflowNode node)
    {
        switch (node.Kind)
        {
            case AgentWorkflowNodeKind.ToolRead or AgentWorkflowNodeKind.ToolWrite:
                if (string.IsNullOrWhiteSpace(node.ToolName))
                    throw new ArgumentException($"Tool node '{node.Key}' requires a tool name.");
                break;
            case AgentWorkflowNodeKind.ModelText:
                if (string.IsNullOrWhiteSpace(node.PromptTemplate) || node.PromptTemplate.Length > 16384)
                    throw new ArgumentException($"Model node '{node.Key}' requires a prompt template.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(node), node.Kind, "Unsupported workflow node kind.");
        }
    }
}
