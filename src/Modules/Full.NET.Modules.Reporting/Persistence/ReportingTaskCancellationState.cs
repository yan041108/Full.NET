namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>取消只先读取本人状态及版本，不读取原参数或会话正文。</summary>
internal sealed class ReportingTaskCancellationState
{
    public Guid Id { get; init; }
    public string StatusKey { get; init; } = string.Empty;
    public long Version { get; init; }
}
