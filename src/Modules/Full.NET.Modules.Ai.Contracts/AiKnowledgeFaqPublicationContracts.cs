using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>明确确认当前修订索引，未知结果重用相同请求键，不接收客户端索引正文或来源。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IndexAiKnowledgeFaqRevision(Guid RequestKey, Guid ExpectedRevisionId, int ExpectedHeadVersion, bool ConfirmCost);

/// <summary>只绑定已经完整完成的当前修订代次；有效期不得超出修订约束。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestAiKnowledgeFaqPublication(Guid RequestKey, Guid ExpectedRevisionId, Guid VersionId, int ExpectedHeadVersion,
    int ExpectedPublicationVersion, DateTimeOffset EffectiveFromUtc, DateTimeOffset EffectiveToUtc);

/// <summary>索引受理不是发布或完成；不包含文件身份，因为输入是数据库修订。</summary>
public sealed record AiKnowledgeFaqIndexAdmission(Guid FaqId, Guid RevisionId, Guid VersionId, Guid TaskId, string State, bool Replayed);

/// <summary>当前稿、候选及发布各自表达；不会因新稿存在把旧发布标成已撤销。</summary>
public sealed record AiKnowledgeFaqLifecycle(Guid FaqId, Guid DocumentId, Guid RevisionId, int HeadVersion, Guid? VersionId,
    Guid? TaskId, string IndexState, Guid? PublicationId, int PublicationVersion, Guid? PublishedRevisionId, string PublicationState, DateTimeOffset? EffectiveFromUtc, DateTimeOffset? EffectiveToUtc, Guid? PendingPublicationId);

/// <summary>用于人工核对的冻结发布申请；完整依赖仍以当前权限逐次复核，不能把响应当授权令牌。</summary>
public sealed record AiKnowledgeFaqPublication(Guid Id, Guid FaqId, Guid RevisionId, Guid VersionId, Guid GenerationId,
    string SnapshotHash, string State, DateTimeOffset EffectiveFromUtc, DateTimeOffset EffectiveToUtc, int ExpectedPublicationVersion,
    Guid RequestedByUserId, Guid? ReviewedByUserId, int Version, AiKnowledgeFaqContent Content, string Origin,
    string? Basis, string SourceHash, IReadOnlyList<AiKnowledgeSearchSource> Sources, bool Current)
{
    /// <summary>发布申请冻结的来源图；未携带有权来源时为 <see langword="null"/>。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiKnowledgeFaqSourceGraph? Graph { get; init; }
    /// <summary>发布申请绑定的准确反馈来源；非来源反馈申请为 <see langword="null"/>。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiKnowledgeFaqFeedbackSource? Feedback { get; init; }
    /// <summary>发布申请引用的个人日程来源选择；无相关来源时为 <see langword="null"/>。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AiPersonalScheduleSourceSelection>? PersonalSchedules { get; init; }
}

/// <summary>正式 FAQ 正文，只有准确当前发布及完整来源当前可读时交付。</summary>
public sealed record AiKnowledgeFaqPublishedContent(Guid FaqId, Guid RevisionId, Guid PublicationId, string Origin, string? Basis,
    string SourceHash, AiKnowledgeFaqContent Content, IReadOnlyList<AiKnowledgeSearchSource> Sources,
    DateTimeOffset EffectiveFromUtc, DateTimeOffset EffectiveToUtc)
{
    /// <summary>正式 FAQ 交付的来源图；未携带有权来源时为 <see langword="null"/>。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiKnowledgeFaqSourceGraph? Graph { get; init; }
    /// <summary>正式 FAQ 绑定的准确反馈来源；非来源反馈发布为 <see langword="null"/>。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiKnowledgeFaqFeedbackSource? Feedback { get; init; }
    /// <summary>正式 FAQ 引用的个人日程来源选择；无相关来源时为 <see langword="null"/>。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AiPersonalScheduleSourceSelection>? PersonalSchedules { get; init; }
}
