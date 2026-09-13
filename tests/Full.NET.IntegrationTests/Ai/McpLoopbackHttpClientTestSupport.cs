using Full.NET.AgenticWeb.Mcp.Client;
using Full.NET.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>将 MCP 出站客户端绑定到 WebApplicationFactory 测试宿主，避免环回地址落到真实 80 端口。</summary>
internal static class McpLoopbackHttpClientTestSupport
{
    private sealed class HandlerHolder
    {
        internal HttpMessageHandler? Handler { get; set; }
    }

    internal static void ConfigureServices(IServiceCollection services)
    {
        var holder = new HandlerHolder();
        services.AddSingleton(holder);
        services.AddHttpClient(nameof(McpClientConnectionManager))
            .ConfigurePrimaryHttpMessageHandler(() =>
                holder.Handler
                ?? throw new InvalidOperationException("MCP loopback handler is not bound yet."));
    }

    internal static void Bind(FullNetApiFactory factory)
    {
        var holder = factory.Services.GetRequiredService<HandlerHolder>();
        holder.Handler = factory.Server.CreateHandler();
    }
}
