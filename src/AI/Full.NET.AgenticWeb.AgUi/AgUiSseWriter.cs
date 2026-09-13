using System.Text;

namespace Full.NET.AgenticWeb.AgUi;

/// <summary>按 AG-UI 约定写入 SSE：event 名与 JSON type 字段一致。</summary>
internal static class AgUiSseWriter
{
    public static async Task WriteRawAsync(
        Stream responseBody,
        string eventType,
        string payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Encoding.UTF8.GetBytes($"event: {eventType}\ndata: {payload}\n\n");
        await responseBody.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await responseBody.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
