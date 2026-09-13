namespace Full.NET.AgenticWeb.Mcp;

/// <summary>MCP 受保护资源与授权发现配置。</summary>
public sealed class McpAuthorizationOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:Ai:Mcp";

    /// <summary>受保护资源标识；与 MCP 客户端请求的资源 URL 对齐。</summary>
    public string ResourceIdentifier { get; set; } = "fullnet://ai/mcp";

    /// <summary>OAuth 受保护资源元数据相对路径。</summary>
    public string ProtectedResourceMetadataPath { get; set; } = "/.well-known/oauth-protected-resource/ai/mcp";

    /// <summary>授权服务器根路径；指向现有 Identity 会话端点，不宣称完整 OIDC。</summary>
    public string AuthorizationServerPath { get; set; } = "/api/v1/auth";
}
