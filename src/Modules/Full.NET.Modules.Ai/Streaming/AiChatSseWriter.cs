using System.Text.Json;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Streaming;

/// <summary>向客户端写入标准 SSE 事件。</summary>
internal static class AiChatSseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>写入增量文本事件。</summary>
    public static async Task WriteDeltaAsync(
        Stream responseBody,
        string delta,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new AiChatStreamDeltaEvent(delta), SerializerOptions);
        await WriteEventAsync(responseBody, "delta", payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>写入完成事件。</summary>
    public static async Task WriteDoneAsync(
        Stream responseBody,
        Guid assistantMessageId,
        int? promptTokens,
        int? completionTokens,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(
            new AiChatStreamDoneEvent(assistantMessageId, promptTokens, completionTokens),
            SerializerOptions);
        await WriteEventAsync(responseBody, "done", payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>写入错误事件。</summary>
    public static async Task WriteErrorAsync(
        Stream responseBody,
        string message,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new AiChatStreamErrorEvent(message), SerializerOptions);
        await WriteEventAsync(responseBody, "error", payload, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteEventAsync(
        Stream responseBody,
        string eventName,
        string payload,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(responseBody, leaveOpen: true);
        await writer.WriteAsync($"event: {eventName}\n").ConfigureAwait(false);
        await writer.WriteAsync($"data: {payload}\n\n").ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
