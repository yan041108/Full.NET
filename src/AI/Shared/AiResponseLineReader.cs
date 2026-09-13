using System.Text;

namespace Full.NET.AI.Providers.Internal;

/// <summary>以固定缓冲读取模型协议，限制单帧及整个响应，避免 ReadLine 的无界分配。</summary>
/// <param name="reader">当前响应的文本读取器，生命周期由调用者管理。</param>
internal sealed class AiResponseLineReader(TextReader reader)
{
    private readonly char[] buffer = new char[4096];
    private int offset;
    private int available;
    private long totalCharacters;
    private bool skipLineFeed;

    /// <summary>读取一行；达到协议预算时停止，绝不继续累积畸形响应。</summary>
    /// <param name="cancellationToken">取消当前读取的令牌。</param>
    internal async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (offset == available)
            {
                available = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                offset = 0;
                totalCharacters += available;
                if (totalCharacters > 8 * 1024 * 1024)
                    throw new InvalidDataException("AI response protocol budget exceeded.");
                if (available == 0) return line.Length == 0 ? null : line.ToString().TrimEnd('\r');
            }
            // CR 本身结束当前行；下一次调用再消费可选 LF，包括跨缓冲区的 CRLF。
            if (skipLineFeed)
            {
                skipLineFeed = false;
                if (buffer[offset] == '\n') offset++;
                if (offset == available) continue;
            }
            var relativeNewline = buffer.AsSpan(offset, available - offset).IndexOfAny('\r', '\n');
            var newline = relativeNewline < 0 ? -1 : offset + relativeNewline;
            var end = newline < 0 ? available : newline;
            var count = end - offset;
            if (line.Length + count > 64 * 1024)
                throw new InvalidDataException("AI response frame limit exceeded.");
            line.Append(buffer, offset, count);
            offset = newline < 0 ? end : end + 1;
            if (newline >= 0)
            {
                skipLineFeed = buffer[newline] == '\r';
                return line.ToString();
            }
        }
    }
}
