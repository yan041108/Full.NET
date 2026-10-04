namespace Full.NET.Modules.Ai.Persistence;

/// <summary>载荷自身不可覆盖，任务/批次字段只由关联权威行投影。</summary>
internal sealed record AiKnowledgeIndexVectorRecord
{
    public Guid Id { get; init; }
    public Guid IndexTaskId { get; init; }
    public int BatchOrdinal { get; init; }
    public string RequestHash { get; init; } = "";
    public int InputCount { get; init; }
    public Guid ModelConfigId { get; init; }
    public int ModelConfigVersion { get; init; }
    public string ProviderKey { get; init; } = "";
    public string ModelId { get; init; } = "";
    public int Dimensions { get; init; }
    public string FormatKey { get; init; } = "";
    public string DistanceKey { get; init; } = "";
    public string PayloadHash { get; init; } = "";
    public byte[] Payload { get; init; } = [];
    public DateTimeOffset CreatedAtUtc { get; init; }
}
