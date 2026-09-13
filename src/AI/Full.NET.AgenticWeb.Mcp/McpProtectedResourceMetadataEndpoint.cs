using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Full.NET.AgenticWeb.Mcp;

/// <summary>发布 MCP 受保护资源元数据；复用现有 JWT 签发方，不宣称完整 OAuth 服务器。</summary>
internal static class McpProtectedResourceMetadataEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/.well-known/oauth-protected-resource/ai/mcp", (
            HttpContext httpContext,
            IOptions<McpAuthorizationOptions> options) =>
        {
            var configured = options.Value;
            var authority = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{configured.AuthorizationServerPath}";
            return Results.Json(
                new McpProtectedResourceMetadataResponse(
                    configured.ResourceIdentifier,
                    [authority],
                    ["header"]),
                McpJsonSerializerContext.Default.McpProtectedResourceMetadataResponse);
        })
        .WithName("aiMcpProtectedResourceMetadata")
        .AllowAnonymous();
    }
}
