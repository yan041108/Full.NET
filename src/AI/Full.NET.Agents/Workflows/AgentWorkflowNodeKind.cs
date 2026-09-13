namespace Full.NET.Agents.Workflows;

/// <summary>工作流节点类型；静态白名单，不支持任意代码节点。</summary>
public enum AgentWorkflowNodeKind
{
    /// <summary>调用只读工具。</summary>
    ToolRead = 1,

    /// <summary>单轮文本模型调用。</summary>
    ModelText = 2,

    /// <summary>调用写工具；可能触发审批。</summary>
    ToolWrite = 3,
}
