namespace Full.NET.Modules.Identity.Contracts;

/// <summary>MCP 资源专用认证方案；不能用于普通 API。</summary>
public static class McpResourceAuthentication
{
    /// <summary>MCP 资源专用认证方案名；普通 API 不得复用该方案。</summary>
    public const string Scheme = "FullNetMcpResource";
}
