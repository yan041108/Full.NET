namespace Full.NET.Agents.Mcp;

/// <summary>读取当前主体可访问的聊天会话摘要。</summary>
public interface IMcpSessionSummaryReader
{
    /// <summary>按受限 URI 读取会话摘要 JSON；无权或不存在时返回 null。</summary>
    ValueTask<string?> TryReadSummaryJsonAsync(string resourceUri, CancellationToken cancellationToken = default);
}
