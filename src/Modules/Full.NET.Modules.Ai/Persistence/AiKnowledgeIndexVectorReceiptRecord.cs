namespace Full.NET.Modules.Ai.Persistence;

/// <summary>完整性校验的轻量回执；不含正文或向量，不能单独证明批次内容有效。</summary>
internal sealed record AiKnowledgeIndexVectorReceiptRecord
{
    public Guid Id { get; init; }
    public int BatchOrdinal { get; init; }
    public string RequestHash { get; init; } = string.Empty;
    public int InputCount { get; init; }
    public string PayloadHash { get; init; } = string.Empty;
    public string ProviderKey { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public int Dimensions { get; init; }
    public string FormatKey { get; init; } = string.Empty;
    public string DistanceKey { get; init; } = string.Empty;
}
