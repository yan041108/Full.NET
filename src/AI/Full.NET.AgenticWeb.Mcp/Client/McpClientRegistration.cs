using Full.NET.Agents.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.AgenticWeb.Mcp.Client;

/// <summary>注册 MCP 客户端出站组件。</summary>
public static class McpClientRegistration
{
    /// <summary>注册远端 MCP 客户端、策略与工具适配器。</summary>
    public static IServiceCollection AddFullNetMcpClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<McpClientOptions>().BindConfiguration(McpClientOptions.SectionName);
        services.AddHttpClient(nameof(McpClientConnectionManager));
        services.TryAddScoped<McpClientConnectionManager>();
        services.TryAddScoped<McpRemoteToolAdapter>();
        services.TryAddScoped<IRemoteAgentToolDefinitionSource>(provider => provider.GetRequiredService<McpRemoteToolAdapter>());
        return services;
    }
}
