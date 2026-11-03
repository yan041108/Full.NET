using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>实际评估运行与集合维护分开授权，不授予生产策略替换或范围财务读取权。</summary>
public static class AiKnowledgeEvaluationRunPermissions
{
    /// <summary>读取当前库、集合和全部来源交集中的策略与运行。</summary>
    public const string Read = "ai.knowledge_evaluation_runs.read";
    /// <summary>捕获不可变策略版本及其准确输入范围。</summary>
    public const string Plan = "ai.knowledge_evaluation_runs.plan";
    /// <summary>明确确认调用上界后建立原身份运行计划。</summary>
    public const string Create = "ai.knowledge_evaluation_runs.create";
    /// <summary>执行计划内尚未执行的样例；不能重新派发未知结果。</summary>
    public const string Execute = "ai.knowledge_evaluation_runs.execute";
    /// <summary>明确跳过尚未执行的样例，仍保留在分母。</summary>
    public const string Skip = "ai.knowledge_evaluation_runs.skip";
    /// <summary>对准确运行结果追加人工证据支持判断。</summary>
    public const string Review = "ai.knowledge_evaluation_runs.review";
    /// <summary>把无法确认结束的原样例关闭为未知，不重试模型。</summary>
    public const string CloseUnknown = "ai.knowledge_evaluation_runs.close_unknown";
}

/// <summary>财务汇总独立授权，集合或运行读取不隐含账单读取权。</summary>
public static class AiKnowledgeFinancePermissions
{
    /// <summary>读取可信范围内的实际账本汇总；不包含主体或会话信息。</summary>
    public const string Read = "ai.knowledge_finance.read";
}

/// <summary>冻结准确批准集合与来源；模型和目的地由服务器核证，不接受客户端提示词或输出。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CaptureAiKnowledgeEvaluationStrategy(Guid RequestKey, int ExpectedVersion, Guid SetId,
    Guid SetRevisionId, int SetVersion, Guid SetApprovalId, string Name, string Baseline, string Candidate,
    Guid EmbeddingModelConfigId, int EmbeddingModelVersion, Guid? ProcessingModelConfigId, int? ProcessingModelVersion,
    IReadOnlyList<Guid> DocumentIds, IReadOnlyList<AiKnowledgeSearchSource> ExpectedSources);

/// <summary>精确模型版本与不含凭据的目的地摘要；不是长期调用授权。</summary>
public sealed record AiKnowledgeEvaluationModel(Guid Id, int Version, string Purpose, string DestinationHash);

/// <summary>不可覆盖的策略版本；原查询来自准确集合修订，不由运行请求替换。</summary>
public sealed record AiKnowledgeEvaluationStrategy(Guid Id, Guid RevisionId, int Version, Guid KnowledgeBaseId,
    string Name, string Protocol, string Baseline, string Candidate, Guid SetId, Guid SetRevisionId,
    int SetVersion, Guid SetApprovalId, string SetContentHash, string SetSourceHash,
    AiKnowledgeEvaluationModel EmbeddingModel, AiKnowledgeEvaluationModel? ProcessingModel,
    IReadOnlyList<AiKnowledgeSearchSource> Sources, string DefinitionHash, DateTimeOffset CreatedAtUtc);

/// <summary>逐次准入仍执行原预算服务，未知金额不以零代替；此预览不预留费用或调用模型。</summary>
public sealed record AiKnowledgeEvaluationRunAdmission(Guid StrategyId, Guid StrategyRevisionId, int StrategyVersion,
    Guid SetRevisionId, string Digest, int CaseCount, int MaximumModelCalls, string BudgetPolicy,
    string MeasurementProtocol);

/// <summary>创建运行只冻结计划；执行每个样例使用已保存的原操作身份。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAiKnowledgeEvaluationRun(Guid RequestKey, Guid StrategyRevisionId,
    string AdmissionDigest, bool ConfirmPaidModelCalls);

/// <summary>原运行及样例版本的明确操作；网络重放不得新建模型身份。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExecuteAiKnowledgeEvaluationCase(Guid RequestKey, int ExpectedVersion);

/// <summary>人工判断仅评价当前检索证据对参考答案的支持，不冒充模型生成正确性。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewAiKnowledgeEvaluationResult(Guid RequestKey, int ExpectedVersion,
    bool BaselineSupportsReference, bool CandidateSupportsReference, bool ConfirmHumanReview);

/// <summary>账本保留原月份、价格和币种；缺失或未知费用保持为空，不能将保守预留称为实际费用。</summary>
public sealed record AiKnowledgeEvaluationOperation(Guid Id, string Stage, string State, string UsageStatus,
    string Month, Guid? PriceVersionId, string? Currency, string? ReservedCost, string? RecordedCost);

/// <summary>逐样例状态包含未运行、跳过和失败；测量缺失不是零延迟。</summary>
public sealed record AiKnowledgeEvaluationCaseState(Guid Id, Guid SampleId, int Sequence, int Version,
    string State, string? ReasonCode, double? DurationMs, DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc, bool? BaselineSupportsReference, bool? CandidateSupportsReference);

/// <summary>同输入实际检索结果；未运行生成答案时不输出引用有效性或语义正确率的虚构分数。</summary>
public sealed record AiKnowledgeEvaluationCaseResult(AiKnowledgeEvaluationCaseState Case,
    AiFeedbackEvaluationSample Sample, AiKnowledgeSearchComparisonRanking? Baseline,
    AiKnowledgeSearchComparisonRanking? Candidate, bool? RefinementRefused,
    string MeasurementProtocol, string FinanceState, IReadOnlyList<AiKnowledgeEvaluationOperation>? Operations)
{
    /// <summary>实际采用的受控改写；原问题始终来自冻结计划。</summary>
    public string? EffectiveQuery {get;init;}
}

/// <summary>分母包含全部冻结样例；延迟统计只使用实际测量项并显示测量数。</summary>
public sealed record AiKnowledgeEvaluationRun(Guid Id, Guid KnowledgeBaseId, Guid StrategyId,
    Guid StrategyRevisionId, Guid SetRevisionId, int Version, string State, int Total,
    int Succeeded, int Failed, int Unknown, int Skipped, int NotRun, int Running,
    int Measured, double? MeanDurationMs, double? P95DurationMs,
    IReadOnlyList<AiKnowledgeEvaluationCaseState> Cases, DateTimeOffset CreatedAtUtc);

/// <summary>有界运行页，游标基于实际读取窗口而非全局计数。</summary>
public sealed record AiKnowledgeEvaluationRunPage(IReadOnlyList<AiKnowledgeEvaluationRun> Items, Guid? NextAfterId);

/// <summary>维护者本人库的导航元数据，不交付名称、样例、模型或结果；展开仍核对全部来源。</summary>
public sealed record AiKnowledgeEvaluationEntry(Guid Id,Guid RevisionId,int Version,DateTimeOffset CreatedAtUtc);
/// <summary>冻结窗口的导航页，最多八条；未显示的正文不因此取得读取资格。</summary>
public sealed record AiKnowledgeEvaluationEntryPage(IReadOnlyList<AiKnowledgeEvaluationEntry> Items,Guid? NextAfterId);
