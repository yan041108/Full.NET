using System.Buffers;

namespace Full.NET.Hosting.Http;

/// <summary>在读取外部正文前落实字节和时间预算，不能用读取后的截断替代资源边界。</summary>
public static class BoundedHttpContent
{
    /// <summary>有界缓冲文本，沿用 HttpContent 的字符集解析；调用方须使用 ResponseHeadersRead。</summary>
    /// <param name="content">外部响应正文。</param>
    /// <param name="maximumBytes">最大正文原始字节数。</param>
    /// <param name="cancellationToken">调用链取消令牌。</param>
    /// <returns>预算内的完整文本，超限或超时抛出异常。</returns>
    public static async Task<string> ReadAsStringAsync(
        HttpContent content, long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await content.LoadIntoBufferAsync(maximumBytes, deadline.Token).ConfigureAwait(false);
        return await content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
    }

    /// <summary>流式复制到调用方拥有的输出；长度头不能替代实际累计字节校验。</summary>
    /// <param name="content">外部正文。</param>
    /// <param name="destination">调用方拥有的输出流。</param>
    /// <param name="maximumBytes">最大允许字节数。</param>
    /// <param name="cancellationToken">包含总截止时间的调用链令牌。</param>
    /// <returns>实际复制字节数。</returns>
    public static async Task<long> CopyToAsync(
        HttpContent content, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (content.Headers.ContentLength > maximumBytes)
            throw new HttpRequestException("HTTP response exceeds the configured byte budget.");
        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        long total = 0;
        try
        {
            while (true)
            {
                var count = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (count == 0) return total;
                total = checked(total + count);
                if (total > maximumBytes)
                    throw new HttpRequestException("HTTP response exceeds the configured byte budget.");
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }
}
