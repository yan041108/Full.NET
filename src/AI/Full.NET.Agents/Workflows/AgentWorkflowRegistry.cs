using System.Collections.Frozen;

namespace Full.NET.Agents.Workflows;

/// <summary>可信工作流目录；示例图仅供测试/环境 Overlay 显式引用。</summary>
public static class AgentWorkflowRegistry
{
    /// <summary>会话读取 → 摘要 → 校验 → 人工确认重命名示例。</summary>
    public const string ChatRenameWorkflowKey = "fullnet-chat-rename-workflow-v1";

    private static readonly FrozenDictionary<(string Key, int Version), AgentWorkflowDefinition> Definitions =
        new Dictionary<(string, int), AgentWorkflowDefinition>
        {
            [(ChatRenameWorkflowKey, 1)] = new(
                ChatRenameWorkflowKey,
                1,
                [
                    new("read_sessions", AgentWorkflowNodeKind.ToolRead, ToolName: "ai.chat.sessions.list"),
                    new(
                        "summarize",
                        AgentWorkflowNodeKind.ModelText,
                        PromptTemplate: "Propose a concise chat session title using this session list JSON: {{read_sessions}}"),
                    new(
                        "validate",
                        AgentWorkflowNodeKind.ModelText,
                        PromptTemplate: "Reply with VALID if this title is appropriate, otherwise INVALID and a short reason. Title: {{summarize}}"),
                    new("rename", AgentWorkflowNodeKind.ToolWrite, ToolName: "ai.chat.sessions.rename"),
                ]),
        }.ToFrozenDictionary();

    /// <summary>解析静态工作流定义。</summary>
    public static AgentWorkflowDefinition? Resolve(string key, int version) =>
        Definitions.GetValueOrDefault((key, version));
}
