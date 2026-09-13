using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Ai.Streaming;

/// <summary>保留原生管理端 SSE 契约，将 HTTP 生命周期封装在传输边界。</summary>
/// <param name="context">当前 HTTP 请求上下文。</param>
internal sealed class AiChatHttpOutput(HttpContext context) : IAiChatOutput
{
    private bool started;
    /// <inheritdoc />
    public bool HasStarted => started || context.Response.HasStarted;
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.ContentType = "text/event-stream";
        await context.Response.StartAsync(cancellationToken).ConfigureAwait(false);
        started = true;
    }
    /// <inheritdoc />
    public Task WriteDeltaAsync(string delta, CancellationToken cancellationToken) =>
        AiChatSseWriter.WriteDeltaAsync(context.Response.Body, delta, cancellationToken);
    /// <inheritdoc />
    public Task WriteDoneAsync(Guid messageId, int? promptTokens, int? completionTokens, CancellationToken cancellationToken) =>
        AiChatSseWriter.WriteDoneAsync(context.Response.Body, messageId, promptTokens, completionTokens, cancellationToken);
    /// <inheritdoc />
    public Task WriteErrorAsync(string message, CancellationToken cancellationToken) =>
        AiChatSseWriter.WriteErrorAsync(context.Response.Body, message, cancellationToken);
}
