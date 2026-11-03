namespace Full.NET.Modules.ImportExport.Persistence;

/// <summary>取消资格只读取本人状态和版本，不先读取有界但可能较大的任务快照正文。</summary>
internal sealed class ImportTaskCancellationState
{
    public Guid Id { get; init; }
    public string StatusKey { get; init; } = string.Empty;
    public long Version { get; init; }
}
