namespace Full.NET.Modules.Ai.Persistence;

/// <summary>一个任务一个不可替换清单；任务仍拥有代次、租户及模型关联。</summary>
internal sealed record AiKnowledgeIndexTextManifestRecord
{
    public Guid Id { get; init; }
    public string FormatKey { get; init; } = "";
    public string PayloadHash { get; init; } = "";
    public byte[] Payload { get; init; } = [];
    public DateTimeOffset CreatedAtUtc { get; init; }
}
