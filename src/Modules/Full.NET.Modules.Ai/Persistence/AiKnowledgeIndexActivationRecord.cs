namespace Full.NET.Modules.Ai.Persistence;

/// <summary>内部活动指针及无正文完成回执；原租约只用于识别同一次完成重试。</summary>
internal sealed record AiKnowledgeIndexActivationRecord
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public Guid IndexTaskId { get; init; }
    public Guid VersionId { get; init; }
    public string PlanHash { get; init; } = "";
    public string ManifestHash { get; init; } = "";
    public string ReceiptHash { get; init; } = "";
    public string ReceiptJson { get; init; } = "";
    public Guid LeaseToken { get; init; }
    public DateTimeOffset ActivatedAtUtc { get; init; }
}
