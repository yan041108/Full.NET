namespace Full.NET.UnitTests.Ai.Evaluation;

/// <summary>离线评估只消费冻结的合成数据，不拥有生产检索或模型执行能力。</summary>
internal sealed record RagEvaluationCase(
    string CaseId, string Category, string Question, string TenantScope,
    string[] AllowedSourceVersions, string[] ExpectedChunkIds,
    bool ShouldRefuse, bool IsSafetyCase, string[] ForbiddenOutputMarkers);

internal sealed record RagEvaluationChunk(
    string ChunkId, string SourceVersion, string TenantScope, string Location, string Text);

/// <summary>质量门槛随数据版本冻结，候选结果不得自带或覆盖门槛。</summary>
internal sealed record RagEvaluationPolicy(
    string Version, int K, double MinimumRecallAtK, double MinimumAnswerRate,
    double MinimumRefusalAccuracy, double MinimumSemanticSupportRate);

internal sealed record RagEvaluationCorpus(
    string DatasetVersion, RagEvaluationPolicy Policy,
    RagEvaluationChunk[] Chunks, RagEvaluationCase[] Cases);

/// <summary>语义支持由独立标注给出；未知标注不能通过引用有效性自动升级。</summary>
internal sealed record RagSemanticReview(string Verdict, string Reviewer, string Evidence);

/// <summary>未知计量使用空值，不能将未知调用或费用作为零记入基线。</summary>
internal sealed record RagEvaluationUsage(
    int? ModelCalls, long? InputTokens, long? OutputTokens, decimal? Cost, string Currency);

internal sealed record RagEvaluationResult(
    string CaseId, string[] CandidateChunkIds, string[] ContextChunkIds,
    string[] CitationChunkIds, string Answer, bool Refused,
    RagSemanticReview SemanticReview, string[] ToolCalls,
    RagEvaluationUsage Usage, double DurationMilliseconds);

internal sealed record RagEvaluationVersions(
    string Model, string Prompt, string Parser, string Chunking, string Retrieval, string Index);

internal sealed record RagEvaluationRun(
    string DatasetVersion, string DatasetSha256, string PolicyVersion,
    string RunKind, RagEvaluationVersions Versions, RagEvaluationResult[] Results);

internal sealed record RagEvaluationCaseScore(
    string CaseId, double? RecallAtK, int ValidCitations, int CitationCount,
    bool AnsweredWithCitation, bool RefusalCorrect, bool SemanticSupported,
    bool IsSafetyCase, string[] SecurityFailures);

internal sealed record RagEvaluationReport(
    string DatasetVersion, string DatasetSha256, string PolicyVersion,
    string RunKind, string EndToEndStatus, RagEvaluationVersions Versions,
    double? RecallAtK, double? CitationValidity, double? AnswerRate,
    double RefusalAccuracy, double? SemanticSupportRate,
    int SecurityFailureCases, int SafetyFailureCases,
    int UnknownUsageCases, int? ModelCalls, long? InputTokens, long? OutputTokens,
    decimal? Cost, string[] Currencies, double MeanDurationMilliseconds,
    double P95DurationMilliseconds, bool Passed, string[] GateFailures,
    RagEvaluationCaseScore[] Cases);
