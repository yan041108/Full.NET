using System.Buffers;

namespace Full.NET.Modules.Document.Providers.OfficePreview;

/// <summary>对实际输入流逐块计量；不可定位的存储流也必须遵守输入与临时磁盘预算。</summary>
internal static class OfficePreviewInputBudget
{
    public static async Task CopyAsync(Stream source, Stream destination, long maximumBytes, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        long total = 0;
        try
        {
            while (true)
            {
                var count = await source.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                if (count == 0) return;
                total = checked(total + count);
                if (total > maximumBytes) throw new InvalidDataException("Office input exceeds its byte budget.");
                await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }
}
