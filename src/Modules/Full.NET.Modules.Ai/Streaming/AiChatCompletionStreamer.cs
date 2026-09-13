using Full.NET.Modules.Ai.Security;
using System.Text;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.AI;
namespace Full.NET.Modules.Ai.Streaming;
/// <summary>保留聊天接口的正文与用量结果，供应商细节由中立客户端处理。</summary>
internal sealed record AiChatCompletionResult(string Content, int? PromptTokens, int? CompletionTokens, bool Cancelled);
/// <summary>将中立模型增量送给现有聊天流程；工厂必须显式注册，未知提供程序失败关闭。</summary>
internal sealed class AiChatCompletionStreamer(IEnumerable<IAiModelClientFactory> factories, AiModelBindingScope bindings)
{
    /// <summary>消费已由调用方验证所有权及可用范围的配置；客户端按调用释放。</summary>
    public async Task<AiChatCompletionResult> StreamAsync(AiModelConfigRecord modelConfig,
        IReadOnlyList<(string RoleKey, string Content)> messages, Func<string, Task> onDelta,
        CancellationToken cancellationToken = default, StringBuilder? contentBuffer = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!modelConfig.IsEnabled) throw new InvalidOperationException("AI model is disabled.");
        var factory = factories.SingleOrDefault(item => item.ProviderKey == modelConfig.ProviderKey)
            ?? throw new InvalidOperationException("Unsupported AI provider.");
        var binding = bindings.Create(modelConfig);
        using IChatClient client = await factory.CreateChatClientAsync(binding, cancellationToken).ConfigureAwait(false);
        contentBuffer ??= new StringBuilder();
        int? inputTokens = null, outputTokens = null;
        var history = messages.Select(item => new ChatMessage(new ChatRole(item.RoleKey), item.Content));
        await foreach (var update in client.GetStreamingResponseAsync(history, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
            {
                if (contentBuffer.Length + (long)text.Length > 1024 * 1024)
                    throw new InvalidDataException("AI generated content limit exceeded.");
                contentBuffer.Append(text);
                await onDelta(text).ConfigureAwait(false);
            }
            foreach (var usage in update.Contents.OfType<UsageContent>())
            {
                inputTokens = Count(usage.Details.InputTokenCount) ?? inputTokens;
                outputTokens = Count(usage.Details.OutputTokenCount) ?? outputTokens;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(contentBuffer.ToString(), inputTokens, outputTokens, false);
    }
    private static int? Count(long? value) => value is >= 0 and <= int.MaxValue ? (int)value.Value : null;
}
