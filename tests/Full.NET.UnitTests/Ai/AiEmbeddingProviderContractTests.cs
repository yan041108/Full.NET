using System.Net;
using System.Text;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Security;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>中立 Embedding 合同：维度、批量、规模限制、取消与不支持模型拒绝。</summary>
[TestClass]
public sealed class AiEmbeddingProviderContractTests
{
    [TestMethod]
    [DataRow("openai_compatible")]
    [DataRow("ollama")]
    public async Task Single_input_returns_dimensions_and_usage(string provider)
    {
        using var http = Client(EmbeddingBody(provider, [[0.1f, 0.2f, 0.3f]], 7));
        var generator = await CreateGenerator(http, provider);
        using (generator)
        {
            var result = await generator.GenerateAsync(["hello"], cancellationToken: default);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3, result[0].Vector.Length);
            Assert.AreEqual(7, result.Usage?.InputTokenCount);
        }
    }

    [TestMethod]
    public async Task Batch_input_preserves_dimension_consistency()
    {
        using var http = Client(EmbeddingBody("openai_compatible",
            [[0.1f, 0.2f], [0.3f, 0.4f]], 11));
        var generator = await CreateGenerator(http, "openai_compatible");
        using (generator)
        {
            var result = await generator.GenerateAsync(["a", "b"], cancellationToken: default);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(2, result[0].Vector.Length);
            Assert.AreEqual(2, result[1].Vector.Length);
        }
    }

    [TestMethod]
    [DataRow("openai_compatible")]
    [DataRow("ollama")]
    public async Task Oversized_input_is_rejected_before_network(string provider)
    {
        using var http = Client("{}", HttpStatusCode.OK);
        var generator = await CreateGenerator(http, provider);
        using (generator)
        {
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
                generator.GenerateAsync([new string('x', 8001)], cancellationToken: default));
        }
    }

    [TestMethod]
    public async Task Unsupported_model_does_not_return_fake_vectors()
    {
        using var http = Client("{\"error\":{\"message\":\"model not found\"}}", HttpStatusCode.BadRequest);
        var generator = await CreateGenerator(http, "openai_compatible");
        using (generator)
        {
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
                generator.GenerateAsync(["hello"], cancellationToken: default));
        }
    }

    [TestMethod]
    public async Task Cancellation_is_observed()
    {
        using var http = Client(EmbeddingBody("openai_compatible", [[0.1f]], 1));
        var generator = await CreateGenerator(http, "openai_compatible");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using (generator)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                generator.GenerateAsync(["hello"], cancellationToken: cancellation.Token));
        }
    }

    private static async Task<IEmbeddingGenerator<string, Embedding<float>>> CreateGenerator(
        HttpClient http, string provider)
    {
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(http);
        using var scope = new AiModelBindingScope();
        var factory = TestAiProviders.Create(httpFactory, scope).Single(item => item.ProviderKey == provider);
        return await factory.CreateEmbeddingGeneratorAsync(scope.Create(new()
        {
            Id = Guid.NewGuid(),
            Version = 1,
            ProviderKey = provider,
            ModelId = "embedding-model",
            EndpointBaseUrl = "https://provider.test",
            ApiKeyProtected = TestAiProviders.Protect("test-key"),
        }), default);
    }

    private static HttpClient Client(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new StaticHandler(body, status)) { BaseAddress = new Uri("https://provider.test") };

    private static string EmbeddingBody(string provider, float[][] vectors, int promptTokens)
    {
        if (provider == "ollama")
        {
            return "{\"embedding\":[" + string.Join(',', vectors[0]) + "],\"prompt_eval_count\":" + promptTokens + "}";
        }

        var data = string.Join(',', vectors.Select((vector, index) =>
            "{\"index\":" + index + ",\"embedding\":[" + string.Join(',', vector) + "]}"));
        return "{\"data\":[" + data + "],\"usage\":{\"prompt_tokens\":" + promptTokens + "}}";
    }

    private sealed class StaticHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
