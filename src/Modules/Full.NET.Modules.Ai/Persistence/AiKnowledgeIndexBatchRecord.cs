namespace Full.NET.Modules.Ai.Persistence;

/// <summary>批次 ID 同时作为预算 OperationId；租约更换不改变已经持久化的请求身份。</summary>
internal sealed class AiKnowledgeIndexBatchRecord
{
    public Guid Id { get; init; }
    public Guid IndexTaskId { get; init; }
    public int BatchOrdinal { get; init; }
    public string RequestHash { get; init; } = string.Empty;
    public int InputCount { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
