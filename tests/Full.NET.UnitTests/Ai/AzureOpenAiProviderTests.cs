using System.Net;
using System.Text;
using Full.NET.AI.Providers.AzureOpenAI;
using Full.NET.Modules.Ai.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>Azure OpenAI 合同：deployment 路径、api-key 认证与 api-version 选项。</summary>
[TestClass]
public sealed class AzureOpenAiProviderTests
{
    [TestMethod]
    public async Task Embedding_uses_deployment_path_and_api_key_header()
    {
        string? path = null;
        string? apiKey = null;
        using var http = new HttpClient(new CaptureHandler((request, _) =>
        {
            path = request.RequestUri?.AbsolutePath;
            apiKey = request.Headers.TryGetValues("api-key", out var values) ? values.Single() : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"embedding":[0.1,0.2]}],"usage":{"prompt_tokens":3}}""", Encoding.UTF8, "application/json"),
            };
        }))
        { BaseAddress = new Uri("https://example.openai.azure.com") };

        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(http);
        using var scope = new AiModelBindingScope();
        var protection = new EphemeralDataProtectionProvider();
        var factory = new AzureOpenAiModelClientFactory(httpFactory, protection, scope);
        using var generator = await factory.CreateEmbeddingGeneratorAsync(scope.Create(new()
        {
            Id = Guid.NewGuid(),
            Version = 1,
            ProviderKey = "azure_openai",
            ModelId = "my-deployment",
            EndpointBaseUrl = "https://example.openai.azure.com",
            OrganizationId = "2024-10-21",
            ApiKeyProtected = protection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Protect("azure-key"),
        }), default);

        var result = await generator.GenerateAsync(["hello"], cancellationToken: default);
        Assert.AreEqual(2, result[0].Vector.Length);
        StringAssert.Contains(path!, "/openai/deployments/my-deployment/embeddings");
        Assert.AreEqual("azure-key", apiKey);
    }

    [TestMethod]
    public async Task Connectivity_lists_models_with_configured_api_version()
    {
        string? query = null;
        using var http = new HttpClient(new CaptureHandler((request, _) =>
        {
            query = request.RequestUri?.Query;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"id":"my-deployment"}]}""", Encoding.UTF8, "application/json"),
            };
        }))
        { BaseAddress = new Uri("https://example.openai.azure.com") };

        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(http);
        using var scope = new AiModelBindingScope();
        var protection = new EphemeralDataProtectionProvider();
        var factory = new AzureOpenAiModelClientFactory(httpFactory, protection, scope);
        var result = await factory.TestConnectivityAsync(scope.Create(new()
        {
            Id = Guid.NewGuid(),
            Version = 1,
            ProviderKey = "azure_openai",
            ModelId = "my-deployment",
            EndpointBaseUrl = "https://example.openai.azure.com",
            OrganizationId = "2024-06-01",
            ApiKeyProtected = protection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Protect("azure-key"),
        }), default);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("?api-version=2024-06-01", query);
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request, cancellationToken));
    }
}
