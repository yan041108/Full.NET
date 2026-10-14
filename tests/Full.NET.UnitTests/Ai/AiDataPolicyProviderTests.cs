using Full.NET.AI.Abstractions.Credentials;
using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Providers.AzureOpenAI;
using Full.NET.AI.Providers.Http;
using Full.NET.AI.Providers.Ollama;
using Full.NET.AI.Providers.OpenAI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>三个真实适配器必须先执行分类准入，拒绝时不能读取凭据或选用默认公网客户端。</summary>
[TestClass]
public sealed class AiDataPolicyProviderTests
{
    [TestMethod]
    [DataRow("openai_compatible", "chat")] [DataRow("openai_compatible", "embedding")] [DataRow("openai_compatible", "connectivity")]
    [DataRow("ollama", "chat")] [DataRow("ollama", "embedding")] [DataRow("ollama", "connectivity")]
    [DataRow("azure_openai", "chat")] [DataRow("azure_openai", "embedding")] [DataRow("azure_openai", "connectivity")]
    public async Task Disallowed_stage_rejects_before_credential_read_and_http_client(string provider, string stage)
    {
        var http = Substitute.For<IHttpClientFactory>(); var credentials = Substitute.For<IProtectedModelCredentialStore>();
        var policy = new AiDataProcessingPolicy(new Dictionary<string, IReadOnlyList<string>> { [stage] = ["public"] });
        var factory = Factory(provider, http, credentials, policy);
        var binding = new ModelBinding(Guid.CreateVersion7(), 1, provider, "model", new Uri("https://provider.test"), Guid.CreateVersion7());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            if (stage == "chat") using (await factory.CreateChatClientAsync(binding, default)) { }
            else if (stage == "embedding") using (await factory.CreateEmbeddingGeneratorAsync(binding, default)) { }
            else await ((Full.NET.AI.Abstractions.Connectivity.IAiModelConnectivityProbe)factory).TestConnectivityAsync(binding);
        });
        Assert.HasCount(0, credentials.ReceivedCalls().ToArray()); Assert.HasCount(0, http.ReceivedCalls().ToArray());
    }

    [TestMethod]
    [DataRow("openai_compatible", "chat")] [DataRow("openai_compatible", "embedding")]
    [DataRow("ollama", "chat")] [DataRow("ollama", "embedding")]
    [DataRow("azure_openai", "chat")] [DataRow("azure_openai", "embedding")]
    public async Task Allowed_restricted_data_selects_private_silent_client(string provider, string stage)
    {
        using var client = new HttpClient(); var http = Substitute.For<IHttpClientFactory>(); http.CreateClient(Arg.Any<string>()).Returns(client);
        var credentials = Substitute.For<IProtectedModelCredentialStore>();
        var protection = new EphemeralDataProtectionProvider();
        credentials.ReadAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>()).Returns(protection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Protect("key"));
        var policy = new AiDataProcessingPolicy(new Dictionary<string, IReadOnlyList<string>> { ["telemetry"] = [] }, ["restricted"]);
        var factory = Factory(provider, http, credentials, policy, protection);
        var binding = new ModelBinding(Guid.CreateVersion7(), 1, provider, "model", new Uri("https://provider.test"), Guid.CreateVersion7());
        if (stage == "chat") using (await factory.CreateChatClientAsync(binding, default)) { }
        else using (await factory.CreateEmbeddingGeneratorAsync(binding, default)) { }
        http.Received(1).CreateClient(AiProviderHttpClients.Name(provider, false, stage == "embedding", true, false));
    }

    private static IAiModelClientFactory Factory(string provider, IHttpClientFactory http, IProtectedModelCredentialStore credentials,
        AiDataProcessingPolicy policy, IDataProtectionProvider? protection = null) => provider switch
    {
        "openai_compatible" => new OpenAiModelClientFactory(http, protection ?? new EphemeralDataProtectionProvider(), credentials,
            new OpenAiGatewayPolicy(new ConfigurationBuilder().Build()), policy),
        "ollama" => new OllamaModelClientFactory(http, protection ?? new EphemeralDataProtectionProvider(), credentials, policy),
        "azure_openai" => new AzureOpenAiModelClientFactory(http, protection ?? new EphemeralDataProtectionProvider(), credentials, policy),
        _ => throw new InvalidOperationException(),
    };
}
