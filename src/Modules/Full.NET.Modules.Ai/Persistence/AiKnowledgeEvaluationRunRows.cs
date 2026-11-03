namespace Full.NET.Modules.Ai.Persistence;

/// <summary>策略根与所选不可变版本的静态投影，不包含模型凭据。</summary>
internal sealed record AiKnowledgeEvaluationStrategyRow
{
    public Guid Id { get;init; }
    public Guid KnowledgeBaseId { get;init; }
    public Guid? TenantId { get;init; }
    public Guid CreatedById { get;init; }
    public string Name { get;init; }=string.Empty;
    public Guid HeadRevisionId { get;init; }
    public int HeadRevisionNo { get;init; }
    public int Version { get;init; }
    public Guid RevisionId { get;init; }
    public int RevisionNo { get;init; }
    public Guid RequestKey { get;init; }
    public string RequestHash { get;init; }=string.Empty;
    public string DefinitionJson { get;init; }=string.Empty;
    public string DefinitionHash { get;init; }=string.Empty;
    public DateTimeOffset CreatedAtUtc { get;init; }
}

/// <summary>原会话、原输入与原操作计划的运行根；不以RunId推导账本归属。</summary>
internal sealed record AiKnowledgeEvaluationRunRow
{
    public Guid Id { get;init; }
    public Guid KnowledgeBaseId { get;init; }
    public Guid? TenantId { get;init; }
    public Guid StrategyId { get;init; }
    public Guid StrategyRevisionId { get;init; }
    public Guid CreatedById { get;init; }
    public Guid SessionId { get;init; }
    public Guid RequestKey { get;init; }
    public string RequestHash { get;init; }=string.Empty;
    public string PlanJson { get;init; }=string.Empty;
    public string PlanHash { get;init; }=string.Empty;
    public int Version { get;init; }
    public DateTimeOffset CreatedAtUtc { get;init; }
}

/// <summary>样例领取、原结果与人工判断均按版本核对，未知结果不回退未运行。</summary>
internal sealed record AiKnowledgeEvaluationCaseRow
{
    public Guid Id { get;init; }
    public Guid RunId { get;init; }
    public Guid SampleId { get;init; }
    public int Sequence { get;init; }
    public int Version { get;init; }
    public string StateKey { get;init; }=string.Empty;
    public string? ReasonCode { get;init; }
    public Guid? ExecutionToken { get;init; }
    public Guid? CloseRequestKey { get;init; }
    public string? CloseRequestHash { get;init; }
    public Guid? RequestKey { get;init; }
    public string? RequestHash { get;init; }
    public string? ResultJson { get;init; }
    public string? ResultHash { get;init; }
    public decimal? DurationMs { get;init; }
    public DateTimeOffset? StartedAtUtc { get;init; }
    public DateTimeOffset? FinishedAtUtc { get;init; }
    public Guid? ReviewId { get;init; }
    public bool? BaselineSupports { get;init; }
    public bool? CandidateSupports { get;init; }
    public DateTimeOffset CreatedAtUtc { get;init; }
}

/// <summary>阶段原身份与真实账本左连接；没有账本行与未知供应商用量分别表示。</summary>
internal sealed record AiKnowledgeEvaluationOperationRow
{
    public Guid Id { get;init; }
    public string StageKey { get;init; }=string.Empty;
    public Guid ModelConfigId { get;init; }
    public int ModelConfigVersion { get;init; }
    public Guid? LedgerId { get;init; }
    public string? UsageStatus { get;init; }
    public string? Outcome { get;init; }
    public string? QuotaMonthKey { get;init; }
    public Guid? PriceVersionId { get;init; }
    public string? Currency { get;init; }
    public decimal? ReservedCost { get;init; }
    public decimal? ChargedCost { get;init; }
    public DateTimeOffset? SettledAtUtc { get;init; }
}

/// <summary>原人工判断请求的追加记录，只绑定实际结果摘要。</summary>
internal sealed record AiKnowledgeEvaluationCaseReviewRow
{
    public Guid Id { get;init; }
    public Guid RunId { get;init; }
    public Guid CaseId { get;init; }
    public string ResultHash { get;init; }=string.Empty;
    public Guid ReviewerUserId { get;init; }
    public bool BaselineSupports { get;init; }
    public bool CandidateSupports { get;init; }
    public Guid RequestKey { get;init; }
    public string RequestHash { get;init; }=string.Empty;
    public DateTimeOffset CreatedAtUtc { get;init; }
}
