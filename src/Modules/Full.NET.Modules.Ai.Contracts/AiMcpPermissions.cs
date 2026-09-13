namespace Full.NET.Modules.Ai.Contracts;

/// <summary>MCP 远端能力调用权限。</summary>
public static class AiMcpPermissions
{
    /// <summary>查看 MCP 远端连接与批准状态。</summary>
    public const string Read = "ai.mcp_remote.read";

    /// <summary>管理 MCP 远端连接与工具批准。</summary>
    public const string Manage = "ai.mcp_remote.manage";

    /// <summary>调用已批准的远端 MCP 工具。</summary>
    public const string RemoteInvoke = "ai.mcp_remote.invoke";
}
