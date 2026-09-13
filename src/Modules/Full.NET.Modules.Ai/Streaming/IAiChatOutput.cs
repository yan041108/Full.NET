namespace Full.NET.Modules.Ai.Streaming;

/// <summary>聊天执行只发送语义事件，不持有 HTTP 上下文或管理响应状态码。</summary>
internal interface IAiChatOutput
{
    /// <summary>传输是否已经启动，决定失败应返回应用结果还是流内事件。</summary>
    bool HasStarted { get; }
    /// <summary>预检和配额通过后启动传输。</summary>
    Task StartAsync(CancellationToken cancellationToken);
    /// <summary>发送一段增量文本。</summary>
    Task WriteDeltaAsync(string delta, CancellationToken cancellationToken);
    /// <summary>持久化与清理完成后通知终态。</summary>
    Task WriteDoneAsync(Guid messageId, int? promptTokens, int? completionTokens, CancellationToken cancellationToken);
    /// <summary>传输已启动后发送安全错误。</summary>
    Task WriteErrorAsync(string message, CancellationToken cancellationToken);
}
