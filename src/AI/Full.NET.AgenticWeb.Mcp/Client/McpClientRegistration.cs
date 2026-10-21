using Full.NET.Agents.Tools;
using Full.NET.AI.Abstractions.Models;
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
    /// <param name="dataPolicy">宿主已冻结的分类策略；显式分类配置缺少该绑定时拒绝装配。</param>
    /// <returns>原服务集合。</returns>
    public static IServiceCollection AddFullNetMcpClient(this IServiceCollection services, IConfiguration configuration, AiDataProcessingPolicy? dataPolicy)
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
        if (dataPolicy is null && configuration.GetSection("FullNet:Ai:DataPolicy").Exists())
            throw new InvalidOperationException("MCP requires the host frozen data policy.");
        var processing = dataPolicy ?? AiDataProcessingPolicy.Default;
        // MCP 参数没有可信低敏来源证明，始终按 restricted 收紧出站网络和诊断。
        privateOnly |= processing.RequiresPrivateNetwork("restricted");
        var silent = !processing.Allows("telemetry", "restricted");
        var policy = new McpEndpointPolicy(origins, privateOnly);
        services.AddOptions<McpClientOptions>().Configure(options =>
        {
            options.ApprovedOrigins = origins.ToArray();
            options.PrivateOnly = privateOnly;
            options.DataPolicy = processing;
        });
        var registration = services.AddHttpClient(nameof(McpClientConnectionManager), client =>
        {
            client.DefaultRequestVersion = HttpVersion.Version11;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        }).ConfigurePrimaryHttpMessageHandler(() => silent
            ? new McpSilentHttpHandler { InnerHandler = CreateHandler(policy, true) } : CreateHandler(policy));
        if (silent) registration.RemoveAllLoggers();
        services.TryAddScoped<McpClientConnectionManager>();
        services.TryAddScoped<McpRemoteToolAdapter>();
        services.TryAddScoped<IRemoteAgentToolDefinitionSource>(provider => provider.GetRequiredService<McpRemoteToolAdapter>());
        return services;
    }

    /// <summary>保留旧装配签名；优先取得宿主已经冻结的策略实例，不从请求创建分类策略。</summary>
    /// <param name="services">宿主服务集合。</param><param name="configuration">可信配置。</param>
    /// <returns>原服务集合。</returns>
    public static IServiceCollection AddFullNetMcpClient(this IServiceCollection services, IConfiguration configuration) =>
        AddFullNetMcpClient(services, configuration, services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(AiDataProcessingPolicy))?.ImplementationInstance as AiDataProcessingPolicy);

    internal static HttpMessageHandler CreateHandler(McpEndpointPolicy policy, bool silent = false)
    {
        var connector = new McpNetworkConnector(policy,
            (host, cancellation) => Dns.GetHostAddressesAsync(host, cancellation), McpNetworkConnector.ConnectSocketAsync);
        return new PolicyHandler(policy)
        {
            InnerHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
                MeterFactory = silent ? new McpSilentMeterFactory() : null,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectCallback = (context, cancellation) => connector.ConnectAsync(
                    context.InitialRequestMessage.RequestUri!, context.DnsEndPoint, cancellation),
            },
        };
    }

    /// <summary>池中连接复用也不能跳过 URI 校验；HTTP/1.1 固定经过受控 TCP 连接回调。</summary>
    private sealed class PolicyHandler(McpEndpointPolicy policy) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            policy.ValidateEndpoint(request.RequestUri!);
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (request.Method != HttpMethod.Post) return response;
            try
            {
                // POST SSE 可能包含要求客户端立即回复的反向 RPC，不能等待 EOF 后再交给 SDK。
                if (string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    response.Content = new BoundedMcpSseContent(response.Content, 4 * 1024 * 1024, TimeSpan.FromSeconds(30), cancellationToken);
                    return response;
                }
                // 普通 JSON 响应仍在解析前落实字节/时间预算；GET 通知流保持流式。
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, deadline.Token).ConfigureAwait(false);
                return response;
            }
            catch { response.Dispose(); throw; }
        }
    }
}
