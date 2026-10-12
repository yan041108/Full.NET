using Full.NET.Modules.Ai.Contracts;
using Full.NET.Agents.Workflows;

namespace Full.NET.Modules.Ai.Features.RouteTasks;

/// <summary>封闭意图，未知值不能获得默认生成或工具权限。</summary>
internal enum AiTaskIntent { Rejected, KnowledgeAsk, AuthorizedToolTask }

/// <summary>仅解析显式意图与互斥载荷；权限、来源资格及预算仍由原执行边界权威检查。</summary>
internal static class AiTaskRoutePolicy
{
    internal static AiTaskIntent Resolve(RouteAiTaskRequest? request) => request switch
    {
        { IntentKey: "knowledge_ask", SessionId: { } id, Knowledge: not null, ToolTask: null }
            when id != Guid.Empty => AiTaskIntent.KnowledgeAsk,
        { IntentKey: "authorized_tool_task", SessionId: null, Knowledge: null,
            ToolTask: { DefinitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey, PromptVersionId: null, Prompt: not null } }
            => AiTaskIntent.AuthorizedToolTask,
        _ => AiTaskIntent.Rejected,
    };
}
