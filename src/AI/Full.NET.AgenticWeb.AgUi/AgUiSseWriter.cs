using System.Text;

namespace Full.NET.AgenticWeb.AgUi;

/// <summary>按 AG-UI 约定写入 SSE：event 名与 JSON type 字段一致。</summary>
internal static class AgUiSseWriter
{
    internal const int MaximumFrameBytes = 1024 * 1024;
    public static async Task WriteRawAsync(
        Stream responseBody,
        string eventType,
        string payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frame = $"event: {eventType}\ndata: {payload}\n\n";
        if (Encoding.UTF8.GetByteCount(frame) > MaximumFrameBytes) throw new InvalidDataException("AG-UI frame exceeds its bounded budget.");
        var bytes = Encoding.UTF8.GetBytes(frame);
        await responseBody.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await responseBody.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
