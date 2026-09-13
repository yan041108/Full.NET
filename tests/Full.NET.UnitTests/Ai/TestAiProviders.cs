using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Abstractions.Connectivity;
using Full.NET.AI.Providers.OpenAI;
using Full.NET.AI.Providers.Ollama;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Streaming;
using Full.NET.Modules.Ai.Connectivity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
namespace Full.NET.UnitTests.Ai;
/// <summary>协议测试使用真实 Provider 与独立凭据作用域，不连接外部服务。</summary>
internal static class TestAiProviders
{
    private static readonly EphemeralDataProtectionProvider Protection = new();
    private static readonly OpenAiGatewayPolicy DefaultGatewayPolicy = new(new ConfigurationBuilder().Build());
    internal static string Protect(string key) => Protection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Protect(key);
    internal static IEnumerable<IAiModelClientFactory> Create(IHttpClientFactory http, AiModelBindingScope scope, OpenAiGatewayPolicy? policy = null) =>
        [new OpenAiModelClientFactory(http, Protection, scope, policy ?? DefaultGatewayPolicy), new OllamaModelClientFactory(http, Protection, scope)];
    internal static IEnumerable<IAiModelConnectivityProbe> CreateProbes(IHttpClientFactory http, AiModelBindingScope scope) =>
        [new OpenAiModelClientFactory(http, Protection, scope, DefaultGatewayPolicy), new OllamaModelClientFactory(http, Protection, scope)];
    internal static AiChatCompletionStreamer Streamer(IHttpClientFactory http, OpenAiGatewayPolicy? policy = null)
    {
        var scope = new AiModelBindingScope();
        return new(Create(http, scope, policy), scope);
    }
    internal static AiModelConnectivityTester Tester(IHttpClientFactory http)
    {
        var scope = new AiModelBindingScope();
        return new(CreateProbes(http, scope), scope);
    }
}
