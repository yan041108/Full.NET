using System.Text.Json;
using Full.NET.AI.Abstractions.Models;
using Full.NET.IntegrationTests.Ai;
using Microsoft.Extensions.AI;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>同一受限协议数据供 JIT 和真实 TLS Native 使用，替身不能跳过服务器候选边界。</summary>
internal sealed class KnowledgeRefinementModel(AiKnowledgeIndexEmbeddingAssertions.ControlledFactory embedding) : IAiModelClientFactory
{
    internal string? Fault; internal bool OmitUsage; internal int ChatCalls;
    public string ProviderKey => embedding.ProviderKey;
    internal static string Reply(JsonElement input, string? fault) => KnowledgeRefinementReply.Create(input, fault);
    public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding, CancellationToken token) => embedding.CreateEmbeddingGeneratorAsync(binding, token);
    public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken token) => ValueTask.FromResult<IChatClient>(new Client(this));
    private sealed class Client(KnowledgeRefinementModel parent) : IChatClient
    {
        public object? GetService(Type type, object? key = null) => null;
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); parent.ChatCalls++; Assert.IsNull(options);
            if (parent.Fault == "reject") throw new HttpRequestException("受控单次失败");
            using var json = JsonDocument.Parse(messages.Last().Text);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply(json.RootElement, parent.Fault)))
                { Usage = parent.OmitUsage ? null : new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3 } });
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new AssertFailedException("受控搜索不能逐 Token 输出。");
    }
}
