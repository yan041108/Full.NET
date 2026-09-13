using Full.NET.Modules.Ai.Security;
using System.Runtime.CompilerServices;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiModelClientFactoryTests
{
    [TestMethod]
    [DataRow("openai_compatible")]
    [DataRow("ollama")]
    public async Task Non_text_history_is_rejected_before_network_dispatch(string provider)
    {
        using var http = new HttpClient(new RejectNetworkHandler());
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(http);
        using var scope = new AiModelBindingScope();
        var factory = TestAiProviders.Create(httpFactory, scope).Single(item => item.ProviderKey == provider);
        using var client = await factory.CreateChatClientAsync(scope.Create(new AiModelConfigRecord { Id = Guid.NewGuid(), Version = 1, ProviderKey = provider,
            ModelId = "model", EndpointBaseUrl = "https://provider.test", ApiKeyProtected = TestAiProviders.Protect("test-key") }), default);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync([
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call", "tool")])])) { }
        });
    }

    [TestMethod]
    [DataRow("openai_compatible")]
    [DataRow("ollama")]
    public async Task Unsupported_options_are_rejected_before_network_dispatch(string provider)
    {
        using var http = new HttpClient(new RejectNetworkHandler());
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(http);
        using var scope = new AiModelBindingScope();
        var factory = TestAiProviders.Create(httpFactory, scope).Single(item => item.ProviderKey == provider);
        using var client = await factory.CreateChatClientAsync(scope.Create(new AiModelConfigRecord { Id = Guid.NewGuid(), Version = 1, ProviderKey = provider,
            ModelId = "model", EndpointBaseUrl = "https://provider.test", ApiKeyProtected = TestAiProviders.Protect("test-key") }), default);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")],
                new ChatOptions { Temperature = 0.2f })) { }
        });
    }

    [TestMethod]
    public void Model_binding_diagnostic_text_does_not_include_credentials()
    {
        var binding = new ModelBinding(Guid.NewGuid(), 1, "fake", "model", new Uri("https://provider.test"), Guid.NewGuid());
        Assert.DoesNotContain(binding.CredentialReference!.Value.ToString(), binding.ToString(), StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task Neutral_updates_preserve_text_usage_and_client_ownership()
    {
        var factory = new Factory();
        var result = await new AiChatCompletionStreamer([factory], new AiModelBindingScope()).StreamAsync(Model(), [], _ => Task.CompletedTask);
        Assert.AreEqual("neutral", result.Content);
        Assert.AreEqual(12, result.PromptTokens);
        Assert.AreEqual(3, result.CompletionTokens);
        Assert.IsTrue(factory.Client.Disposed);
    }

    [TestMethod]
    [DataRow(false, "fake")]
    [DataRow(true, "missing")]
    public async Task Disabled_or_unknown_models_do_not_create_clients(bool enabled, string provider)
    {
        var factory = new Factory();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new AiChatCompletionStreamer([factory], new AiModelBindingScope()).StreamAsync(Model(enabled, provider), [], _ => Task.CompletedTask));
        Assert.AreEqual(0, factory.Calls);
    }

    [TestMethod]
    public async Task Consumer_failure_releases_client()
    {
        var factory = new Factory();
        await Assert.ThrowsExactlyAsync<IOException>(() =>
            new AiChatCompletionStreamer([factory], new AiModelBindingScope()).StreamAsync(Model(), [], _ => throw new IOException("disconnected")));
        Assert.IsTrue(factory.Client.Disposed);
    }

    [TestMethod]
    public async Task Cancelled_request_never_creates_client()
    {
        var factory = new Factory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new AiChatCompletionStreamer([factory], new AiModelBindingScope()).StreamAsync(Model(), [], _ => Task.CompletedTask, cancellation.Token));
        Assert.AreEqual(0, factory.Calls);
    }

    private static AiModelConfigRecord Model(bool enabled = true, string provider = "fake") => new()
    {
        Id = Guid.NewGuid(), Version = 1, IsEnabled = enabled, ProviderKey = provider,
        EndpointBaseUrl = "https://provider.test", ModelId = "neutral-model",
    };

    private sealed class Factory : IAiModelClientFactory
    {
        public string ProviderKey => "fake";
        internal int Calls { get; private set; }
        internal FakeClient Client { get; } = new();
        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult<IChatClient>(Client);
        }

        public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(
            ModelBinding binding, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RejectNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("不支持的选项不应派发网络请求。");
    }

    private sealed class FakeClient : IChatClient
    {
        internal bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(ChatRole.Assistant, "neutral");
            yield return new() { Contents = [new UsageContent(new UsageDetails { InputTokenCount = 12, OutputTokenCount = 3 })] };
        }
    }
}
