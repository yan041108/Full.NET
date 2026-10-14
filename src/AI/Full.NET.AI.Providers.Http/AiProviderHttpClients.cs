using System.Net;
using Full.NET.AI.Abstractions.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.AI.Providers.Http;

/// <summary>两个现有 Provider 的受控 HTTP 装配入口；只由宿主组合根配置。</summary>
public static class AiProviderHttpClients
{
    /// <summary>正式 Provider HTTP 指标与追踪源的稳定名称，供宿主静态订阅。</summary>
    public const string TelemetryName = AiProviderTelemetry.Name;
    /// <summary>获取隔离 Provider 的聊天或目录命名客户端。</summary>
    /// <param name="providerKey">支持的静态提供程序键。</param>
    /// <param name="connectivity">是否为目录探测。</param>
    /// <param name="embedding">是否为向量处理。</param>
    /// <param name="privateData">是否强制私网连接。</param>
    /// <param name="emitTelemetry">是否允许 AI 传输诊断。</param>
    /// <returns>宿主预先注册的有限客户端名称。</returns>
    public static string Name(string providerKey, bool connectivity, bool embedding, bool privateData, bool emitTelemetry) => providerKey switch
    {
        "openai_compatible" or "ollama" or "azure_openai" =>
            $"Full.NET.Ai.{providerKey}.{(connectivity ? "Connectivity" : embedding ? "Embedding" : "Chat")}{(privateData ? ".Private" : "")}{(emitTelemetry ? "" : ".Silent")}",
        _ => throw new InvalidOperationException("Unsupported AI provider."),
    };

    /// <summary>保留原命名方法 ABI，兼容通道继续沿用既有名称。</summary>
    /// <param name="providerKey">静态供应商键。</param><param name="connectivity">是否为目录探测。</param>
    /// <param name="embedding">是否为向量处理。</param><returns>原客户端名称。</returns>
    public static string Name(string providerKey, bool connectivity, bool embedding = false) => Name(providerKey, connectivity, embedding, false, true);

    /// <summary>先执行分类去向准入，再选择冻结的网络/遥测客户端；不读取或分配模型凭据。</summary>
    /// <param name="providerKey">静态提供程序键。</param>
    /// <param name="stage">聊天、向量或连通性阶段。</param>
    /// <param name="classification">服务端确定的分类。</param>
    /// <param name="policy">宿主冻结策略；缺省沿用兼容策略。</param>
    /// <returns>通过分类策略的客户端名称。</returns>
    public static string NameForData(string providerKey, string stage, string classification, AiDataProcessingPolicy? policy = null)
    {
        if (stage is not ("chat" or "embedding" or "connectivity")) throw new InvalidOperationException("Unsupported AI processing stage.");
        var current = policy ?? AiDataProcessingPolicy.Default;
        current.Require(stage, classification);
        return Name(providerKey, stage == "connectivity", stage == "embedding", current.RequiresPrivateNetwork(classification), current.Allows("telemetry", classification));
    }

    /// <summary>注册静态网络闭包；内网批准列表读取一次，修改配置需要重启宿主。</summary>
    /// <param name="services">宿主服务集合。</param>
    /// <param name="configuration">可信宿主配置；不能来自模型记录或请求参数。</param>
    public static void AddAiProviderHttpClients(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("FullNet:Ai:Network:OllamaAllowedOrigins").GetChildren()
            .Select(child => child.Value ?? throw new InvalidOperationException("AI allowed origin cannot be null.")).ToArray();
        var dataPolicy = AiDataPolicyConfiguration.Read(configuration);
        services.AddSingleton(dataPolicy);
        services.AddSingleton(AiProviderInputAdmissionPolicy.Read(configuration));
        var networkSection = configuration.GetSection("FullNet:Ai:Network");
        var privateSection = networkSection.GetSection("PrivateOnly");
        var privateConfigured = networkSection.GetChildren().Any(child =>
            string.Equals(child.Key, "PrivateOnly", StringComparison.OrdinalIgnoreCase));
        var privateOnly = false;
        // 显式读取避免反射绑定；只有键缺失才能使用默认值，空值或对象不能静默开放公网。
        if (privateConfigured && (privateSection.GetChildren().Any()
            || !bool.TryParse(privateSection.Value, out privateOnly)))
            throw new InvalidOperationException("AI private-only network policy requires a boolean.");
        var policy = new AiEndpointPolicy(origins, privateOnly);
        // 公网 Ollama 可以保留原准入，但不能借同一个 origin 进入私网数据通道；域名仍在连接时复核。
        var privateOrigins = origins.Where(origin => !IPAddress.TryParse(new Uri(origin).DnsSafeHost, out var address)
            || AiEndpointPolicy.IsAllowedAddress(address, true) && !AiEndpointPolicy.IsAllowedAddress(address, false));
        var privatePolicy = new AiEndpointPolicy(privateOrigins, true);
        foreach (var key in new[] { "openai_compatible", "ollama", "azure_openai" })
        {
            var ollama = key == "ollama";
            foreach (var stage in new[] { "connectivity", "chat", "embedding" })
            foreach (var privateData in new[] { false, true })
            foreach (var emitTelemetry in new[] { false, true })
            {
                var selectedPolicy = privateData ? privatePolicy : policy;
                var registration = services.AddHttpClient(Name(key, stage == "connectivity", stage == "embedding", privateData, emitTelemetry), client =>
                {
                    client.Timeout = stage == "connectivity" ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(3);
                    client.DefaultRequestVersion = HttpVersion.Version11;
                    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
                }).ConfigurePrimaryHttpMessageHandler(() => emitTelemetry
                    ? new AiProviderTelemetryHandler(key, stage) { InnerHandler = CreateHandler(selectedPolicy, ollama) }
                    : new AiSilentHttpHandler { InnerHandler = CreateHandler(selectedPolicy, ollama, true) });
                if (!emitTelemetry) registration.RemoveAllLoggers();
            }
        }
    }

    internal static HttpMessageHandler CreateHandler(AiEndpointPolicy policy, bool ollama, bool silent = false)
    {
        var connector = new AiNetworkConnector(policy, ollama,
            (host, cancellation) => Dns.GetHostAddressesAsync(host, cancellation), AiNetworkConnector.ConnectSocketAsync);
        return new PolicyHandler(policy, ollama)
        {
            InnerHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
                MeterFactory = silent ? new AiSilentMeterFactory() : null,
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
