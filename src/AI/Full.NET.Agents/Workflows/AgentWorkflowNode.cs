namespace Full.NET.Agents.Workflows;

/// <summary>静态工作流节点；边由定义顺序隐式连接。</summary>
/// <param name="Key">节点键。</param>
/// <param name="Kind">节点类型。</param>
/// <param name="ToolName">工具名；仅工具节点使用。</param>
/// <param name="PromptTemplate">提示模板；模型节点使用，可引用 <c>{{node_key}}</c> 输出。</param>
public sealed record AgentWorkflowNode(
    string Key,
    AgentWorkflowNodeKind Kind,
    string? ToolName = null,
    string? PromptTemplate = null);
