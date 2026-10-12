using Full.NET.Agents.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;

namespace Full.NET.AgenticWeb.Mcp.Client;

/// <summary>注册 MCP 客户端出站组件。</summary>
public static class McpClientRegistration
{
    /// <summary>注册远端 MCP 客户端、策略与工具适配器。</summary>
    /// <param name="services">宿主服务集合。</param>
    /// <param name="configuration">可信宿主网络配置，装配时冻结，修改后须重启。</param>
    public static IServiceCollection AddFullNetMcpClient(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection(McpClientOptions.SectionName + ":ApprovedOrigins").GetChildren()
            .Select(child => child.Value ?? throw new InvalidOperationException("MCP allowed origin cannot be null.")).ToArray();
        var network = configuration.GetSection("FullNet:Ai:Network");
        var configured = network.GetChildren().Any(child => string.Equals(child.Key, "PrivateOnly", StringComparison.OrdinalIgnoreCase));
        var section = network.GetSection("PrivateOnly");
        var privateOnly = false;
        // 只有键缺失可以使用默认值；空值、对象或无效布尔值必须阻止宿主装配。
        if (configured && (section.GetChildren().Any() || !bool.TryParse(section.Value, out privateOnly)))
            throw new InvalidOperationException("AI private-only network policy requires a boolean.");
        var policy = new McpEndpointPolicy(origins, privateOnly);
        services.AddOptions<McpClientOptions>().Configure(options =>
        {
            options.ApprovedOrigins = origins.ToArray();
            options.PrivateOnly = privateOnly;
        });
        services.AddHttpClient(nameof(McpClientConnectionManager), client =>
        {
            client.DefaultRequestVersion = HttpVersion.Version11;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        }).ConfigurePrimaryHttpMessageHandler(() => CreateHandler(policy));
        services.TryAddScoped<McpClientConnectionManager>();
        services.TryAddScoped<McpRemoteToolAdapter>();
        services.TryAddScoped<IRemoteAgentToolDefinitionSource>(provider => provider.GetRequiredService<McpRemoteToolAdapter>());
        return services;
    }

    internal static HttpMessageHandler CreateHandler(McpEndpointPolicy policy)
    {
        var connector = new McpNetworkConnector(policy,
            (host, cancellation) => Dns.GetHostAddressesAsync(host, cancellation), McpNetworkConnector.ConnectSocketAsync);
        return new PolicyHandler(policy)
        {
            InnerHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectCallback = (context, cancellation) => connector.ConnectAsync(
                    context.InitialRequestMessage.RequestUri!, context.DnsEndPoint, cancellation),
            },
        };
    }

    /// <summary>池中连接复用也不能跳过 URI 校验；HTTP/1.1 固定经过受控 TCP 连接回调。</summary>
    private sealed class PolicyHandler(McpEndpointPolicy policy) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            policy.ValidateEndpoint(request.RequestUri!);
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
