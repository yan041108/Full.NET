namespace Full.NET.Modules.Ai.Contracts;

/// <summary>AI Agent 工具目录与调用审计权限码。</summary>
public static class AiAgentToolPermissions
{
    /// <summary>读取静态 Agent Tool 目录。</summary>
    public const string CatalogRead = "ai.tools.catalog.read";

    /// <summary>读取 Agent Tool 调用审计。</summary>
    public const string CallsRead = "ai.tools.calls.read";
}
