namespace Full.NET.UnitTests.Ai.Evaluation;

/// <summary>三个封闭策略在相同冻结语料上提交观测；这里只消费证据，不调用检索或模型。</summary>
internal sealed record RagRetrievalStrategyRun(string StrategyKey, RagEvaluationRun Run);

internal sealed record RagRetrievalComparisonInput(int SchemaVersion, RagRetrievalStrategyRun[] Strategies);

/// <summary>比较门槛由工具固定，候选文件不能改写质量、安全或资源门槛。</summary>
internal sealed record RagRetrievalComparisonPolicy(string Version, double MinimumQualityGain,
    decimal MaximumTokenRatio, decimal MaximumCostRatio, decimal MaximumDurationRatio, decimal MaximumCallRatio);

/// <summary>报告只含评分与机器原因，不复制问题、回答、来源正文或评审正文。</summary>
internal sealed record RagRetrievalStrategyDecision(string StrategyKey, string EvidenceSha256,
    RagEvaluationReport Score, double RecallGain, double AnswerRateGain,
    decimal? TokenRatio, decimal? CostRatio, double? P95DurationRatio, decimal? CallRatio,
    bool Eligible, string[] Blockers);

/// <summary>推荐仅供人工核对原始执行和评审记录；工具不修改生产策略。</summary>
internal sealed record RagRetrievalComparisonReport(int SchemaVersion, string EvidenceStatus,
    string CurrentStrategyKey, string RecommendedStrategyKey, string PromotionStatus,
    RagRetrievalComparisonPolicy Policy, string BaselineEvidenceSha256,
    RagEvaluationReport Baseline, RagRetrievalStrategyDecision[] Candidates);
