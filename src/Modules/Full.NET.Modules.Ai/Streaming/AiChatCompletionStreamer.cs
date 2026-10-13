using Full.NET.Modules.Ai.Security;
using System.Text;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.AI;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Domain;
namespace Full.NET.Modules.Ai.Streaming;
/// <summary>保留聊天接口的正文与用量结果，供应商细节由中立客户端处理。</summary>
internal sealed record AiChatCompletionResult(string Content, int? PromptTokens, int? CompletionTokens, bool Cancelled);
/// <summary>将中立模型增量送给现有聊天流程；工厂必须显式注册，未知提供程序失败关闭。</summary>
internal sealed class AiChatCompletionStreamer(IEnumerable<IAiModelClientFactory> factories, AiModelBindingScope bindings)
{
    /// <summary>消费已由调用方验证所有权及可用范围的配置；客户端按调用释放。</summary>
    public async Task<AiChatCompletionResult> StreamAsync(AiModelConfigRecord modelConfig,
        IReadOnlyList<(string RoleKey, string Content)> messages, Func<string, Task> onDelta,
        CancellationToken cancellationToken = default, StringBuilder? contentBuffer = null, AiChatStreamingReceipt? receipt = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(onDelta);
        if (!modelConfig.IsEnabled) throw new InvalidOperationException("AI model is disabled.");
        var factory = factories.SingleOrDefault(item => item.ProviderKey == modelConfig.ProviderKey)
            ?? throw new InvalidOperationException("Unsupported AI provider.");
        var binding = bindings.Create(modelConfig);
        using IChatClient client = await factory.CreateChatClientAsync(binding, cancellationToken).ConfigureAwait(false);
        if (client is null) throw new InvalidDataException("AI provider returned no chat client.");
        cancellationToken.ThrowIfCancellationRequested();
        contentBuffer ??= new StringBuilder();
        int? inputTokens = null, outputTokens = null;
        var history = messages.Select(item => new ChatMessage(new ChatRole(item.RoleKey), item.Content));
        receipt ??= new AiChatStreamingReceipt();
        receipt.Begin();
        var source = client.GetStreamingResponseAsync(history, new ChatOptions { MaxOutputTokens = AiChatContentPolicy.MaxCompletionTokens }, cancellationToken);
        if (source is null) throw new InvalidDataException("AI provider returned no chat stream.");
        var enumerator = source.GetAsyncEnumerator(cancellationToken)
            ?? throw new InvalidDataException("AI provider returned no chat enumerator.");
        try
        {
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                var update = enumerator.Current;
                // Current 与工厂也可能忽略取消；同帧用量先于正文验证，非法回执不能被忽略或交付。
                cancellationToken.ThrowIfCancellationRequested();
                if (update is null) throw new InvalidDataException("AI provider returned no chat update.");
                foreach (var usage in update.Contents.OfType<UsageContent>())
                {
                    AiOperationUsage reported;
                    try { reported = AiModelUsagePolicy.Read(usage.Details); }
                    catch (AiBudgetException) { throw new InvalidDataException("AI provider returned invalid chat usage."); }
                    if (reported.OutputTokens > AiChatContentPolicy.MaxCompletionTokens)
                        throw new InvalidDataException("AI provider exceeded the chat output reservation.");
                    receipt.Report(reported);
                    inputTokens = (int?)reported.InputTokens ?? inputTokens;
                    outputTokens = (int?)reported.OutputTokens ?? outputTokens;
                }
                var text = update.Text;
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(text))
                {
                    if (contentBuffer.Length + (long)text.Length > 1024 * 1024)
                        throw new InvalidDataException("AI generated content limit exceeded.");
                    contentBuffer.Append(text);
                    await onDelta(text).ConfigureAwait(false);
                }
            }
            // EOF先确认计量，随后枚举器/客户端释放、取消或落库失败都不能抹掉完整报告。
            receipt.Complete();
            cancellationToken.ThrowIfCancellationRequested();
            return new(contentBuffer.ToString(), inputTokens, outputTokens, false);
        }
        finally { await enumerator.DisposeAsync().ConfigureAwait(false); }
    }
}
