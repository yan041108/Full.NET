namespace Full.NET.UnitTests.Ai.Evaluation;

/// <summary>向量证据显式绑定模型空间；文件声明不认证 Embedding 或重排提供程序的真实执行。</summary>
internal sealed record RagRetrievalVector(string ChunkId, string ModelKey, float[] Values);
internal sealed record RagRerankScore(string ChunkId, double Score);
internal sealed record RagRetrievalQuery(string CaseId, string ModelKey, float[] Values, RagRerankScore[]? RerankScores);
internal sealed record RagRetrievalExecutionInput(int SchemaVersion, string DatasetVersion, string DatasetSha256,
    string EmbeddingEvidenceKind, string ModelKey, int Dimensions, string? RerankModelKey,
    RagRetrievalVector[] Vectors, RagRetrievalQuery[] Queries);
internal sealed record RagRetrievalRank(string ChunkId, double Score);
internal sealed record RagRetrievalCaseExecution(string CaseId, string Status, int EligibleCandidateCount,
    RagRetrievalRank[] TopChunks, string[] RerankWindowChunkIds, double? RecallAt5, double DurationMilliseconds);
internal sealed record RagRetrievalStrategyExecution(string StrategyKey, string Version, double? RecallAt5,
    RagRetrievalCaseExecution[] Cases);
/// <summary>仅报告检索排名与受控时延，不含正文，也不等同于回答质量、安全或生产启用建议。</summary>
internal sealed record RagRetrievalExecutionReport(int SchemaVersion, string PolicyVersion, string DatasetVersion,
    string DatasetSha256, string InputSha256, string EmbeddingEvidenceKind, string ModelKey, string? RerankModelKey,
    bool RetrievalOnly, RagRetrievalStrategyExecution[] Strategies);
internal sealed record RagLexicalDocument(string ChunkId, string Text);
