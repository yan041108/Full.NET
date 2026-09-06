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
    ];
}
