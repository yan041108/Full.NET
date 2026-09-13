using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Full.NET.AI.Abstractions.Models;
using Microsoft.Extensions.AI;
namespace Full.NET.AI.Providers.Internal;
internal sealed record AiChatCompletionResult(string Content, int? PromptTokens, int? CompletionTokens, bool Cancelled);
/// <summary>供应商流转为中立增量；有界队列提供背压，提前停止枚举会取消生产者。</summary>
internal sealed class HttpChatClient(HttpClient httpClient, ModelBinding binding, string? apiKey, bool requestStreamingUsage = false) : IChatClient
{
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(IChatClient) ? this : null;
    public void Dispose() => httpClient.Dispose();
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder();
        UsageDetails? usage = null;
        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            text.Append(update.Text);
            usage = update.Contents.OfType<UsageContent>().LastOrDefault()?.Details ?? usage;
        }
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text.ToString())) { Usage = usage };
    }
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 首切片沿用配置模型和固定输出上限；不支持的选项与非文本内容必须明确拒绝。
        if (options is not null)
            throw new NotSupportedException("AI provider capability is not enabled.");
        var history = messages.Select(message =>
        {
            if (message.Contents.Any(content => content is not TextContent))
                throw new NotSupportedException("AI provider capability is not enabled.");
            return (message.Role.Value, message.Text);
        }).ToArray();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<ChatResponseUpdate>(1);
        var producer = ProduceAsync();
        try
        {
            await foreach (var update in channel.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false)) yield return update;
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            await producer.ConfigureAwait(false);
        }
        async Task ProduceAsync()
        {
            try
            {
                var result = await new ProviderChatTransport(httpClient).StreamAsync(binding, apiKey, history,
                    delta => channel.Writer.WriteAsync(new ChatResponseUpdate(ChatRole.Assistant, delta), lifetime.Token).AsTask(),
                    new StringBuilder(), lifetime.Token, requestStreamingUsage).ConfigureAwait(false);
                await channel.Writer.WriteAsync(new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new UsageContent(new UsageDetails { InputTokenCount = result.PromptTokens, OutputTokenCount = result.CompletionTokens })],
                }, lifetime.Token).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (Exception error) { channel.Writer.TryComplete(error); }
        }
    }
}
