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
        // 模型来自可信绑定；只开放输出上限，其它能力必须在网络请求前失败关闭。
        var maxOutputTokens = ResolveOutputLimit(options);
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
                    new StringBuilder(), lifetime.Token, requestStreamingUsage, maxOutputTokens).ConfigureAwait(false);
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
    /// <summary>静态白名单避免工具、模型覆盖或原始请求工厂绕过已批准的文本协议。</summary>
    private static int ResolveOutputLimit(ChatOptions? options)
    {
        if (options is null) return 4096;
        if (options.ConversationId is not null || options.Instructions is not null || options.Temperature is not null
            || options.TopP is not null || options.TopK is not null || options.FrequencyPenalty is not null
            || options.PresencePenalty is not null || options.Seed is not null || options.Reasoning is not null
            || options.ResponseFormat is not null || options.ModelId is not null || options.StopSequences is not null
            || options.AllowMultipleToolCalls is not null || options.ToolMode is not null || options.Tools is not null
            || options.AllowBackgroundResponses is not null || options.ContinuationToken is not null
            || options.RawRepresentationFactory is not null || options.AdditionalProperties is not null)
            throw new NotSupportedException("AI provider capability is not enabled.");
        var limit = options.MaxOutputTokens ?? 4096;
        if (limit is <= 0 or > 4096) throw new ArgumentOutOfRangeException(nameof(options), "AI output token limit is out of range.");
        return limit;
    }

}
