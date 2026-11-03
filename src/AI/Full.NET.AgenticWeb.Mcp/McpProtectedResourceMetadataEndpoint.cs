using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace Full.NET.AgenticWeb.Mcp;

/// <summary>发布 MCP 受保护资源元数据，授权服务器来自实际启用的 OIDC 发行方。</summary>
internal static class McpProtectedResourceMetadataEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/.well-known/oauth-protected-resource/ai/mcp", (
            IOptions<McpAuthorizationOptions> options,
            IConfiguration configuration) =>
        {
            var configured = options.Value;
            var issuer = configuration["Identity:Oidc:Issuer"];
            if (!configuration.GetValue<bool>("Identity:Oidc:Enable")
                || !Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri)
                || issuerUri.Scheme != Uri.UriSchemeHttps
                || !Uri.TryCreate(configured.ResourceIdentifier, UriKind.Absolute, out var resourceUri)
                || resourceUri.Scheme != Uri.UriSchemeHttps)
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            return Results.Json(
                new McpProtectedResourceMetadataResponse(
                    configured.ResourceIdentifier,
                    [issuer!],
                    ["header"]),
                McpJsonSerializerContext.Default.McpProtectedResourceMetadataResponse);
        })
        .WithName("aiMcpProtectedResourceMetadata")
        .AllowAnonymous();
    }
}
