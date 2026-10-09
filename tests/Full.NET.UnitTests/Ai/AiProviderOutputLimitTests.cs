using System.Net;
using System.Text.Json;
using Full.NET.AI.Providers.AzureOpenAI;
using Microsoft.AspNetCore.DataProtection;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>实际 HTTP 请求必须携带显式输出上限，非法选项不得发送请求。</summary>
[TestClass]
public sealed class AiProviderOutputLimitTests
{
    [TestMethod]
    [DataRow("openai_compatible", false)] [DataRow("ollama", false)] [DataRow("azure_openai", false)]
    [DataRow("openai_compatible", true)] [DataRow("ollama", true)] [DataRow("azure_openai", true)]
    public async Task Explicit_output_limit_reaches_wire_for_streaming_and_buffered_calls(string provider, bool streaming)
    {
        using var handler = new RecordingHandler(provider);
        using var client = await CreateAsync(provider, handler);
        var options = new ChatOptions { MaxOutputTokens = 37 };
        if (streaming) { await foreach (var update in client.GetStreamingResponseAsync([new(ChatRole.User, "hello")], options)) { } }
        else await client.GetResponseAsync([new(ChatRole.User, "hello")], options);
        Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual(37, handler.OutputLimit);
        Assert.AreEqual(37, options.MaxOutputTokens);
    }

    [TestMethod]
    [DataRow("openai_compatible", 0)] [DataRow("ollama", 0)] [DataRow("azure_openai", 0)]
    [DataRow("openai_compatible", -1)] [DataRow("ollama", -1)] [DataRow("azure_openai", -1)]
    [DataRow("openai_compatible", 4097)] [DataRow("ollama", 4097)] [DataRow("azure_openai", 4097)]
    public async Task Invalid_output_limit_is_rejected_before_dispatch(string provider, int limit)
    {
        using var handler = new RecordingHandler(provider);
        using var client = await CreateAsync(provider, handler);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => client.GetResponseAsync([new(ChatRole.User, "hello")], new() { MaxOutputTokens = limit }));
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow("openai_compatible", false)] [DataRow("ollama", false)] [DataRow("azure_openai", false)]
    [DataRow("openai_compatible", true)] [DataRow("ollama", true)] [DataRow("azure_openai", true)]
    public async Task Null_or_empty_options_preserve_legacy_output_limit(string provider, bool empty)
    {
        using var handler = new RecordingHandler(provider);
        using var client = await CreateAsync(provider, handler);
        await client.GetResponseAsync([new(ChatRole.User, "hello")], empty ? new ChatOptions() : null);
        Assert.AreEqual(4096, handler.OutputLimit);
    }

    [TestMethod]
    [DataRow("openai_compatible")] [DataRow("ollama")] [DataRow("azure_openai")]
    public async Task Allowed_limit_does_not_silently_enable_other_options(string provider)
    {
        using var handler = new RecordingHandler(provider);
        using var client = await CreateAsync(provider, handler);
        var rawCalled = false;
        ChatOptions[] options = [
            new() { MaxOutputTokens = 37, Temperature = 0 },
            new() { MaxOutputTokens = 37, ModelId = "unauthorized-model" },
            new() { MaxOutputTokens = 37, Tools = [] },
            new() { MaxOutputTokens = 37, AdditionalProperties = new() },
            new() { MaxOutputTokens = 37, RawRepresentationFactory = _ => { rawCalled = true; return new object(); } }
        ];
        foreach (var option in options)
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => client.GetResponseAsync([new(ChatRole.User, "hello")], option));
        Assert.AreEqual(0, handler.Calls);
        Assert.IsFalse(rawCalled);
    }

    [TestMethod]
    public void Sdk_option_surface_requires_explicit_review_when_properties_change()
    {
        string[] reviewed = ["ConversationId", "Instructions", "Temperature", "MaxOutputTokens", "TopP", "TopK", "FrequencyPenalty", "PresencePenalty", "Seed", "Reasoning", "ResponseFormat", "ModelId", "StopSequences", "AllowMultipleToolCalls", "ToolMode", "Tools", "AllowBackgroundResponses", "ContinuationToken", "RawRepresentationFactory", "AdditionalProperties"];
        CollectionAssert.AreEquivalent(reviewed, typeof(ChatOptions).GetProperties().Select(property => property.Name).ToArray());
    }

    private static async Task<IChatClient> CreateAsync(string provider, HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler, disposeHandler: false));
        using var scope = new AiModelBindingScope();
        var protection = new EphemeralDataProtectionProvider();
        var adapter = provider == "azure_openai" ? new AzureOpenAiModelClientFactory(factory, protection, scope) : TestAiProviders.Create(factory, scope).Single(item => item.ProviderKey == provider);
        return await adapter.CreateChatClientAsync(scope.Create(new AiModelConfigRecord
        {
            Id = Guid.CreateVersion7(), Version = 1, ProviderKey = provider, ModelId = "model",
            EndpointBaseUrl = "https://provider.test", ApiKeyProtected = provider == "azure_openai" ? protection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Protect("test-key") : TestAiProviders.Protect("test-key")
        }), default);
    }

    private sealed class RecordingHandler(string provider) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public int OutputLimit { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            OutputLimit = provider == "ollama" ? payload.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32() : payload.RootElement.GetProperty("max_tokens").GetInt32();
            var body = provider == "ollama" ? "{\"done\":true,\"prompt_eval_count\":2,\"eval_count\":1}\n" : "data: {\"usage\":{\"prompt_tokens\":2,\"completion_tokens\":1}}\n\ndata: [DONE]\n";
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
