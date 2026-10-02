namespace Full.NET.Agents.Mcp;

/// <summary>读取当前主体可访问的聊天会话摘要。</summary>
public interface IMcpSessionSummaryReader
{
    /// <summary>按受限 URI 读取会话摘要 JSON。</summary>
    /// <param name="resourceUri">受限的会话资源 URI。</param>
    /// <param name="cancellationToken">取消读取的令牌。</param>
    /// <returns>会话摘要的 JSON 字符串；无权访问或资源不存在时为 <see langword="null"/>。</returns>
    ValueTask<string?> TryReadSummaryJsonAsync(string resourceUri, CancellationToken cancellationToken = default);
}
