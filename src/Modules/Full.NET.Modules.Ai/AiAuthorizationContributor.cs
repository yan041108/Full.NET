using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai;

/// <summary>向 Identity 授权目录注册 AI 权限、导航与页面操作。</summary>
internal sealed class AiAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("ai", "AI", 118);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(AiModelPermissions.Read, "读取 AI 模型配置", AuthorizationScope.Host),
        new(AiModelPermissions.Create, "创建 AI 模型配置", AuthorizationScope.Host),
        new(AiModelPermissions.Update, "更新 AI 模型配置", AuthorizationScope.Host),
        new(AiModelPermissions.Test, "测试 AI 模型连通性", AuthorizationScope.Host),
        new(AiTenantQuotaPermissions.Read, "读取 AI 租户配额", AuthorizationScope.Host),
        new(AiTenantQuotaPermissions.Update, "更新 AI 租户配额", AuthorizationScope.Host),
        new(AiChatPermissions.Read, "读取 AI 聊天会话", AuthorizationScope.Host),
        new(AiChatPermissions.Create, "创建 AI 聊天会话", AuthorizationScope.Host),
        new(AiChatPermissions.Update, "更新 AI 聊天会话", AuthorizationScope.Host),
        new(AiChatPermissions.Delete, "删除 AI 聊天会话", AuthorizationScope.Host),
        new(AiChatPermissions.Send, "发送 AI 聊天消息", AuthorizationScope.Host),
        new(AiChatPermissions.Cancel, "取消 AI 聊天生成", AuthorizationScope.Host),
        new(AiAgentToolPermissions.CatalogRead, "读取 Agent Tool 静态目录", AuthorizationScope.Host | AuthorizationScope.Tenant),
        new(AiAgentToolPermissions.CallsRead, "读取 Agent Tool 调用审计", AuthorizationScope.Host),
        new(AiAgentRunPermissions.Read, "读取 Agent 运行", AuthorizationScope.Host | AuthorizationScope.Tenant),
        new(AiAgentRunPermissions.Create, "创建 Agent 运行", AuthorizationScope.Host | AuthorizationScope.Tenant),
        new(AiAgentRunPermissions.Cancel, "取消 Agent 运行", AuthorizationScope.Host | AuthorizationScope.Tenant),
        new(AiAgentRunPermissions.Resume, "恢复 Agent 运行", AuthorizationScope.Host | AuthorizationScope.Tenant),
        new(AiAgentApprovalPermissions.Read, "读取 Agent 审批", AuthorizationScope.Host),
        new(AiAgentApprovalPermissions.Request, "请求 Agent 审批", AuthorizationScope.Host),
        new(AiAgentApprovalPermissions.Decide, "决定 Agent 审批", AuthorizationScope.Host),
        new(AiAgentApprovalPermissions.Delegate, "管理 Agent 委托", AuthorizationScope.Host),
        new(AiMcpPermissions.Read, "读取 MCP 远端连接", AuthorizationScope.Host),
        new(AiMcpPermissions.Manage, "管理 MCP 远端连接", AuthorizationScope.Host),
        new(AiMcpPermissions.RemoteInvoke, "调用已批准 MCP 远端工具", AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "ai-model-configs",
            null,
            "ai-model-configs",
            "/ai/model-configs",
            "ai-model-configs",
            "AI 模型配置",
            "AI Model Configs",
            "cpu",
            10,
            AiModelPermissions.Read),
        new NavigationDefinition(
            "ai-chat",
            null,
            "ai-chat",
            "/ai/chat",
            "ai-chat",
            "AI 对话",
            "AI Chat",
            "chat-dot-round",
            20,
            AiChatPermissions.Read),
        new NavigationDefinition(
            "ai-agent-tools",
            null,
            "ai-agent-tools",
            "/ai/agent-tools",
            "ai-agent-tools",
            "Agent 工具",
            "Agent Tools",
            "operation",
            30,
            AiAgentToolPermissions.CatalogRead),
        new NavigationDefinition(
            "ai-mcp-remote-connections",
            null,
            "ai-mcp-remote-connections",
            "/ai/mcp-remote-connections",
            "ai-mcp-remote-connections",
            "MCP 远端连接",
            "MCP Remote Connections",
            "link",
            35,
            AiMcpPermissions.Read),
        new NavigationDefinition(
            "ai-agent-runs",
            null,
            "ai-agent-runs",
            "/ai/agent-runs",
            "ai-agent-runs",
            "Agent 运行",
            "Agent Runs",
            "monitor",
            40,
            AiAgentRunPermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "ai.models.create",
            "ai-model-configs",
            AiModelPermissions.Create,
            "新建模型",
            "create",
            10),
        new AuthorizationActionDefinition(
            "ai.models.update",
            "ai-model-configs",
            AiModelPermissions.Update,
            "编辑模型",
            "update",
            20),
        new AuthorizationActionDefinition(
            "ai.models.test",
            "ai-model-configs",
            AiModelPermissions.Test,
            "测试连通性",
            "test",
            30),
        new AuthorizationActionDefinition(
            "ai.mcp.remote.manage",
            "ai-mcp-remote-connections",
            AiMcpPermissions.Manage,
            "管理远端连接",
            "manage",
            10),
        new AuthorizationActionDefinition(
            "ai.quotas.update",
            "ai-model-configs",
            AiTenantQuotaPermissions.Update,
            "编辑租户配额",
            "quota",
            40),
        new AuthorizationActionDefinition(
            "ai.chat.sessions.create",
            "ai-chat",
            AiChatPermissions.Create,
            "新建会话",
            "create",
            10),
        new AuthorizationActionDefinition(
            "ai.chat.messages.send",
            "ai-chat",
            AiChatPermissions.Send,
            "发送消息",
            "send",
            20),
        new AuthorizationActionDefinition(
            "ai.chat.messages.cancel",
            "ai-chat",
            AiChatPermissions.Cancel,
            "停止生成",
            "cancel",
            30),
        new AuthorizationActionDefinition(
            "ai.tools.calls.read",
            "ai-agent-tools",
            AiAgentToolPermissions.CallsRead,
            "查看调用审计",
            "audit",
            10),
        new AuthorizationActionDefinition(
            "ai.agent_runs.create",
            "ai-agent-runs",
            AiAgentRunPermissions.Create,
            "创建运行",
            "create",
            10),
        new AuthorizationActionDefinition(
            "ai.agent_runs.cancel",
            "ai-agent-runs",
            AiAgentRunPermissions.Cancel,
            "取消运行",
            "cancel",
            20),
        new AuthorizationActionDefinition(
            "ai.agent_runs.resume",
            "ai-agent-runs",
            AiAgentRunPermissions.Resume,
            "恢复运行",
            "resume",
            30),
    ];
}
