using Full.NET.AgenticWeb.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace Full.NET.AgenticWeb.Mcp.Server;

/// <summary>装配标准 Streamable HTTP MCP 服务端点。</summary>
public static class McpServerRegistration
{
    /// <summary>注册 MCP 服务、适配器与受保护资源元数据。</summary>
    public static IServiceCollection AddFullNetMcpServer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<McpAuthorizationOptions>().BindConfiguration(McpAuthorizationOptions.SectionName);
        services.AddHttpContextAccessor();
        services.AddScoped<McpToolAdapter>();
        services.AddScoped<McpResourceAdapter>();
        services.AddScoped<McpPromptAdapter>();
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new()
                {
                    Name = "Full.NET AI MCP",
                    Version = "1.0.0",
                };
            })
            .WithHttpTransport(transport =>
            {
                transport.Stateless = true;
            })
            .WithListToolsHandler((context, cancellationToken) =>
            {
                var adapter = ResolveScoped<McpToolAdapter>(context.Services);
                return adapter.ListToolsAsync(context, cancellationToken);
            })
            .WithCallToolHandler((context, cancellationToken) =>
            {
                var adapter = ResolveScoped<McpToolAdapter>(context.Services);
                return adapter.CallToolAsync(context, cancellationToken);
            })
            .WithListResourcesHandler((context, cancellationToken) =>
            {
                var adapter = ResolveScoped<McpResourceAdapter>(context.Services);
                return adapter.ListResourcesAsync(context, cancellationToken);
            })
            .WithReadResourceHandler((context, cancellationToken) =>
            {
                var adapter = ResolveScoped<McpResourceAdapter>(context.Services);
                return adapter.ReadResourceAsync(context, cancellationToken);
            })
            .WithListPromptsHandler((context, cancellationToken) =>
            {
                var adapter = ResolveScoped<McpPromptAdapter>(context.Services);
                return adapter.ListPromptsAsync(context, cancellationToken);
            })
            .WithGetPromptHandler((context, cancellationToken) =>
            {
                var adapter = ResolveScoped<McpPromptAdapter>(context.Services);
                return adapter.GetPromptAsync(context, cancellationToken);
            });
        return services;
    }

    /// <summary>映射 MCP HTTP 端点与授权元数据。</summary>
    public static void MapFullNetMcp(this IEndpointRouteBuilder endpoints)
    {
        McpProtectedResourceMetadataEndpoint.Map(endpoints);
        endpoints.MapMcp("/api/v1/ai/mcp")
            .RequireAuthorization();
    }

    private static T ResolveScoped<T>(IServiceProvider? services) where T : notnull
    {
        if (services is null)
        {
            throw new InvalidOperationException("MCP request services are unavailable.");
        }

        return services.GetRequiredService<T>();
    }
}
