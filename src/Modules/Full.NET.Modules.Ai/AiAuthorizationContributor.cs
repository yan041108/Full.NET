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
    ];
}
