namespace Full.NET.Modules.Ai.Contracts;

/// <summary>首个受控任务路由：调用方明确选择意图，服务器不根据模型文本自动转为工具写入。</summary>
/// <param name="IntentKey">稳定意图键，仅支持 knowledge_ask 和 authorized_tool_task。</param>
/// <param name="SessionId">知识问答所属会话；工具任务必须省略。</param>
/// <param name="Knowledge">知识问答载荷；与工具载荷互斥。</param>
/// <param name="ToolTask">已有授权工作流载荷；本版仅接受会话重命名，不扩大工具目录。</param>
public sealed record RouteAiTaskRequest(string IntentKey, Guid? SessionId = null,
    AskAiKnowledgeRequest? Knowledge = null, CreateAiAgentRunRequest? ToolTask = null);
