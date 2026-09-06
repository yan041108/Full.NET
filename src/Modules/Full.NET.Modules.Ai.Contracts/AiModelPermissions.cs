namespace Full.NET.Modules.Ai.Contracts;

/// <summary>AI 模型配置权限码。</summary>
public static class AiModelPermissions
{
    /// <summary>读取模型配置。</summary>
    public const string Read = "ai.models.read";

    /// <summary>创建模型配置。</summary>
    public const string Create = "ai.models.create";

    /// <summary>更新模型配置。</summary>
    public const string Update = "ai.models.update";

    /// <summary>测试模型连通性。</summary>
    public const string Test = "ai.models.test";
}
