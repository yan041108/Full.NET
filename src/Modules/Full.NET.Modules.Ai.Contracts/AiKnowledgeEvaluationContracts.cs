using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>持久评估集使用独立权限；任何权限均不授予生产会话正文读取权或模型派发权。</summary>
public static class AiKnowledgeEvaluationPermissions
{
    /// <summary>读取当前可读知识库及完整来源交集中的评估修订。</summary>
    public const string Read = "ai.knowledge_evaluation_sets.read";
    /// <summary>在本人管理的库内新建人工脱敏评估集。</summary>
    public const string Create = "ai.knowledge_evaluation_sets.create";
    /// <summary>在本人管理的库内追加人工脱敏修订。</summary>
    public const string Append = "ai.knowledge_evaluation_sets.append";
    /// <summary>显式冻结修订；不自动批准或执行模型。</summary>
    public const string Freeze = "ai.knowledge_evaluation_sets.freeze";
    /// <summary>恢复历史正文并保留当前累计来源。</summary>
    public const string Restore = "ai.knowledge_evaluation_sets.restore";
    /// <summary>在同一库内保留完整来源复制评估集。</summary>
    public const string Copy = "ai.knowledge_evaluation_sets.copy";
    /// <summary>独立批准明确冻结的人工脱敏修订。</summary>
    public const string Approve = "ai.knowledge_evaluation_sets.approve";
    /// <summary>撤回当前冻结修订的批准资格。</summary>
    public const string Withdraw = "ai.knowledge_evaluation_sets.withdraw";
    /// <summary>窄用途复核批准反馈并保留其私人来源；不交付原用户问题或原回答正文。</summary>
    public const string Feedback = "ai.knowledge_evaluation_sets.feedback";
}

/// <summary>旧评估候选只提供人工字段；持久化前必须在准确目标库重新捕获完整当前来源与原答案摘要。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrepareAiKnowledgeEvaluationFeedback(string CandidateToken);

/// <summary>专用持久化准备凭据，绑定原会话、目标准确版本、完整来源与人工字段；不构成长期授权。</summary>
public sealed record AiKnowledgeEvaluationPreparedFeedback(string PersistenceToken, string SourceHash, string SampleHash, DateTimeOffset ExpiresAtUtc);

/// <summary>客户端只能提交人工脱敏样例；已有来源由服务器保留，新增反馈必须使用准确目标的新用途持久化准备凭据。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeEvaluationCaseInput(Guid? Id, AiFeedbackEvaluationSample Sample, string? IndependentBasis,
    string? FeedbackCandidateToken);

/// <summary>保存使用原请求身份及准确头；Freeze 明确创建不可覆盖的冻结修订，不会自动批准或调用模型。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveAiKnowledgeEvaluationSet(Guid RequestKey, Guid? ExpectedRevisionId, int ExpectedVersion,
    string Name, IReadOnlyList<AiKnowledgeEvaluationCaseInput> Cases, bool ConfirmManualRedaction, bool Freeze);

/// <summary>恢复仅复制历史脱敏正文，当前累计来源和贡献者足迹必须保留。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RestoreAiKnowledgeEvaluationSet(Guid RequestKey, Guid ExpectedRevisionId, int ExpectedVersion, Guid RevisionId);

/// <summary>复制只在明确的当前库和修订内进行，来源与贡献者不能清空。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CopyAiKnowledgeEvaluationSet(Guid RequestKey, Guid ExpectedRevisionId, int ExpectedVersion);

/// <summary>批准与撤回均绑定准确修订和根版本；批准者不能是该正文的贡献者。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewAiKnowledgeEvaluationSet(Guid RequestKey, Guid ExpectedRevisionId, int ExpectedVersion,
    string Decision, bool ConfirmIndependentRedactionReview);

/// <summary>公开样例仅含人工输入字段和不具授权作用的来源摘要，不暴露原消息、会话或用户问题。</summary>
public sealed record AiKnowledgeEvaluationCase(Guid Id, AiFeedbackEvaluationSample Sample, string? IndependentBasis, string? SourceKey);

/// <summary>不可变评估修订；来源数量包含移出样例仍须保留的限制。</summary>
public sealed record AiKnowledgeEvaluationRevision(Guid Id, int Number, string Name, string State,
    IReadOnlyList<AiKnowledgeEvaluationCase> Cases, string ContentHash, string SourceHash, int SourceCount,
    DateTimeOffset CreatedAtUtc, Guid? RestoredFromRevisionId);

/// <summary>批准记录只适用于准确冻结修订；不能作为当前来源资格或已执行评估的证明。</summary>
public sealed record AiKnowledgeEvaluationWorkspace(Guid Id, Guid KnowledgeBaseId, int Version,
    AiKnowledgeEvaluationRevision Revision, string ApprovalState, Guid? ApprovalId, bool CanIndependentlyApprove);

/// <summary>有界可读集合页；游标对应已读取窗口，而非全局计数。</summary>
public sealed record AiKnowledgeEvaluationPage(IReadOnlyList<AiKnowledgeEvaluationWorkspace> Items, Guid? NextAfterId);

/// <summary>已提交操作的原身份收据；来源交付失效时正文为空，不把已提交操作误报为可用新身份重试的失败。</summary>
public sealed record AiKnowledgeEvaluationMutation(AiKnowledgeEvaluationWorkspace? Workspace, Guid SavedRevisionId, bool Replayed, string DeliveryState);
