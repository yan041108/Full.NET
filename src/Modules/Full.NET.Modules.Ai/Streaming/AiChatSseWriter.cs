using System.Text.Json;
using System.Text;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Streaming;

/// <summary>向客户端写入标准 SSE 事件。</summary>
internal static class AiChatSseWriter
{
    private static readonly AiJsonSerializerContext SerializerContext = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>写入增量文本事件。</summary>
    /// <param name="responseBody">保持开启的 HTTP 响应流。</param>
    /// <param name="delta">本次生成的文本增量。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    public static async Task WriteDeltaAsync(
        Stream responseBody,
        string delta,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new AiChatStreamDeltaEvent(delta), SerializerContext.AiChatStreamDeltaEvent);
        await WriteEventAsync(responseBody, "delta", payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>写入完成事件。</summary>
    /// <param name="responseBody">保持开启的 HTTP 响应流。</param>
    /// <param name="assistantMessageId">已持久化的助手消息标识。</param>
    /// <param name="promptTokens">提供程序报告的输入令牌数。</param>
    /// <param name="completionTokens">提供程序报告的输出令牌数。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    public static async Task WriteDoneAsync(
        Stream responseBody,
        Guid assistantMessageId,
        int? promptTokens,
        int? completionTokens,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(
            new AiChatStreamDoneEvent(assistantMessageId, promptTokens, completionTokens),
            SerializerContext.AiChatStreamDoneEvent);
        await WriteEventAsync(responseBody, "done", payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>写入错误事件。</summary>
    /// <param name="responseBody">保持开启的 HTTP 响应流。</param>
    /// <param name="message">可向客户端公开的错误说明。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    public static async Task WriteErrorAsync(
        Stream responseBody,
        string message,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new AiChatStreamErrorEvent(message), SerializerContext.AiChatStreamErrorEvent);
        await WriteEventAsync(responseBody, "error", payload, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteEventAsync(
        Stream responseBody,
        string eventName,
        string payload,
        CancellationToken cancellationToken)
    {
        // 直接将取消传递至实际写入，避免 StreamWriter 在失败释放时补刷已取消的事件。
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Encoding.UTF8.GetBytes($"event: {eventName}\ndata: {payload}\n\n");
        await responseBody.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await responseBody.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
