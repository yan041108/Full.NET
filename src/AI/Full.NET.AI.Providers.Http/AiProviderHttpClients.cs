using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.AI.Providers.Http;

/// <summary>两个现有 Provider 的受控 HTTP 装配入口；只由宿主组合根配置。</summary>
public static class AiProviderHttpClients
{
    /// <summary>获取隔离 Provider 的聊天或目录命名客户端。</summary>
    /// <param name="providerKey">支持的静态提供程序键。</param>
    /// <param name="connectivity">是否为目录探测。</param>
    public static string Name(string providerKey, bool connectivity, bool embedding = false) => providerKey switch
    {
        "openai_compatible" or "ollama" or "azure_openai" => connectivity
            ? $"Full.NET.Ai.{providerKey}.Connectivity"
            : $"Full.NET.Ai.{providerKey}.{(embedding ? "Embedding" : "Chat")}",
        _ => throw new InvalidOperationException("Unsupported AI provider."),
    };

    /// <summary>注册静态网络闭包；内网批准列表读取一次，修改配置需要重启宿主。</summary>
    /// <param name="services">宿主服务集合。</param>
    /// <param name="configuration">可信宿主配置；不能来自模型记录或请求参数。</param>
    public static void AddAiProviderHttpClients(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("FullNet:Ai:Network:OllamaAllowedOrigins").GetChildren()
            .Select(child => child.Value ?? throw new InvalidOperationException("AI allowed origin cannot be null."));
        var policy = new AiEndpointPolicy(origins);
        foreach (var key in new[] { "openai_compatible", "ollama", "azure_openai" })
        {
            var ollama = key == "ollama";
            services.AddHttpClient(Name(key, connectivity: true), client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestVersion = HttpVersion.Version11;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
            }).ConfigurePrimaryHttpMessageHandler(() => CreateHandler(policy, ollama));
            foreach (var embedding in new[] { false, true })
            {
                services.AddHttpClient(Name(key, connectivity: false, embedding), client =>
                {
                    client.Timeout = TimeSpan.FromMinutes(3);
                    client.DefaultRequestVersion = HttpVersion.Version11;
                    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
                }).ConfigurePrimaryHttpMessageHandler(() => CreateHandler(policy, ollama));
            }
        }
    }

    internal static HttpMessageHandler CreateHandler(AiEndpointPolicy policy, bool ollama)
    {
        var connector = new AiNetworkConnector(policy, ollama,
            (host, cancellation) => Dns.GetHostAddressesAsync(host, cancellation), AiNetworkConnector.ConnectSocketAsync);
        return new PolicyHandler(policy, ollama)
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

    /// <summary>连接池复用时也检查每次请求，并限制协议为必经 TCP 校验的 HTTP/1.1。</summary>
    private sealed class PolicyHandler(AiEndpointPolicy policy, bool ollama) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            policy.ValidateUri(request.RequestUri!, ollama);
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
