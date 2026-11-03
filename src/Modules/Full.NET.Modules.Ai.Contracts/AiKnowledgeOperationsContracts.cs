namespace Full.NET.Modules.Ai.Contracts;

/// <summary>知识维护元数据的独立权限，不授权读取文档或私人答案正文。</summary>
public static class AiKnowledgeOperationsPermissions
{
    /// <summary>查询本人管理范围的任务及发布待办。</summary>
    public const string Read = "ai.knowledge_operations.read";
}

/// <summary>任务与发布记录并列展示；激活/发布记录不是当前来源可服务资格证明，不包含正文或异常。</summary>
public sealed record AiKnowledgeOperationItem(
    Guid KnowledgeBaseId, Guid DocumentId, Guid VersionId, int VersionNumber, string SourceKind, Guid? FaqId,
    string PreparationState, Guid? TaskId, string TaskState, int AttemptCount, string? FailureCode,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? TaskUpdatedAtUtc, bool HasActiveGeneration,
    string ReviewState, Guid? PublicationId, Guid? PublicationVersionId, string PublicationState,
    DateTimeOffset? EffectiveFromUtc, DateTimeOffset? EffectiveToUtc);

/// <summary>最多二十五项；下页复用相同时间窗口。元数据按读取时权威变化，不是历史内容快照。</summary>
public sealed record AiKnowledgeOperationsPage(IReadOnlyList<AiKnowledgeOperationItem> Items, Guid? NextAfterId,
    DateTimeOffset FromUtc, DateTimeOffset ToUtc, string View);
