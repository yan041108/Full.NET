namespace Full.NET.Modules.Ai.Persistence;

/// <summary>评估集根与准确不可变修订；来源 JSON 仅在服务端核证，不直接映射到公开 DTO。</summary>
internal sealed record AiKnowledgeEvaluationRow
{
    public Guid Id { get; init; }
    public Guid KnowledgeBaseId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid CreatedById { get; init; }
    public Guid CreateRequestKey { get; init; }
    public string CreateHash { get; init; } = "";
    public Guid HeadRevisionId { get; init; }
    public int HeadRevisionNo { get; init; }
    public int Version { get; init; }
    public string FootprintJson { get; init; } = "";
    public string FootprintHash { get; init; } = "";
    public Guid? ApprovalId { get; init; }
    public string? ApprovalState { get; init; }
    public Guid RevisionId { get; init; }
    public int RevisionNo { get; init; }
    public Guid RequestKey { get; init; }
    public string RequestHash { get; init; } = "";
    public string ContentJson { get; init; } = "";
    public string ContentHash { get; init; } = "";
    public string SourceJson { get; init; } = "";
    public string SourceHash { get; init; } = "";
    public Guid EditorUserId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public Guid? RestoredFromRevisionId { get; init; }
}

/// <summary>批准动作幂等记录；追加决定不修改已有冻结修订。</summary>
internal sealed record AiKnowledgeEvaluationApprovalRow(Guid Id, Guid RevisionId, string RequestHash, string DecisionKey);

/// <summary>同一有界页最后一次联合核证，只接受服务器已校验的根与修订摘要。</summary>
internal sealed record AiKnowledgeEvaluationBinding(Guid Id, int Version, Guid HeadRevisionId, string FootprintHash,
    Guid RevisionId, string ContentHash, string SourceHash, string FootprintJson);
