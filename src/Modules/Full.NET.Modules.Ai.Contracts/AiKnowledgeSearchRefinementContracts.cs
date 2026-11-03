using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>显式执行受控改写/重排，模型与版本必须符合全部冻结来源的知识库批准策略。</summary>
/// <param name="KnowledgeBaseIds">一至八个知识库。</param>
/// <param name="EmbeddingModelConfigId">原搜索的批准向量模型。</param>
/// <param name="ProcessingModelConfigId">知识库已批准的生成模型，不接受临时目的地。</param>
/// <param name="ProcessingModelVersion">精确生成模型版本。</param>
/// <param name="Query">原查询，遵守原有有界关键词规则。</param>
/// <param name="Mode">rewrite、rerank 或 rewrite_rerank。</param>
/// <param name="Limit">各排名交付前一至八项；模型重排只处理有界向量前缀。</param>
/// <param name="IncludeDiagnostics">独立诊断权限。</param>
/// <param name="SearchScope">原搜索文档范围、模型版本及完整发布集合。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RefineAiKnowledgeSearchRequest(IReadOnlyList<Guid> KnowledgeBaseIds, Guid EmbeddingModelConfigId,
    Guid ProcessingModelConfigId, int ProcessingModelVersion, string Query, string Mode, int Limit, bool IncludeDiagnostics,
    AiKnowledgeSearchAnswerScope SearchScope);

/// <summary>每个实际收费阶段使用原账本操作身份；未知用量不解释为零费用。</summary>
/// <param name="Stage">original_embedding、rewrite、rewritten_embedding 或 rerank。</param>
/// <param name="OperationId">本次服务器生成的 UUID v7 操作。</param>
/// <param name="Status">reported 或 unknown，不认证价格或真实账单。</param>
public sealed record AiKnowledgeSearchRefinementOperation(string Stage, Guid OperationId, string Status);

/// <summary>原查询与实验策略分别展示；拒绝是显式状态，不隐式回退或替换生产策略，不生成答案和历史。</summary>
/// <param name="Strategy">固定实验协议版本。</param>
/// <param name="Mode">实际请求的受控模式。</param>
/// <param name="Query">不变的原查询。</param>
/// <param name="EffectiveQuery">实际采用的查询；改写拒绝时保持原值。</param>
/// <param name="ProcessingModelConfigId">批准处理模型身份。</param>
/// <param name="ProcessingModelVersion">批准处理模型版本。</param>
/// <param name="RefusedStage">rewrite、rerank 或空；不是传输失败的替代状态。</param>
/// <param name="Sources">全部冻结发布来源。</param>
/// <param name="OriginalKeyword">原查询词频排名。</param>
/// <param name="OriginalVector">原查询余弦排名。</param>
/// <param name="RewrittenKeyword">成功改写后的独立词频排名。</param>
/// <param name="RewrittenVector">成功改写后的独立余弦排名。</param>
/// <param name="Reranked">模型排列的有界向量前缀，分数是序位优先级而非置信度。</param>
/// <param name="RerankCandidateCount">实际外发给重排模型的前缀数量，最多八项；未执行时为空。</param>
/// <param name="Operations">仅列出实际执行且完成结算的阶段，不伪造未执行或零费用记录。</param>
/// <param name="Diagnostics">独立授权的完整候选诊断。</param>
public sealed record AiKnowledgeSearchRefinementResponse(string Strategy, string Mode, string Query, string EffectiveQuery,
    Guid ProcessingModelConfigId, int ProcessingModelVersion, string? RefusedStage, IReadOnlyList<AiKnowledgeSearchSource> Sources,
    AiKnowledgeSearchComparisonRanking OriginalKeyword, AiKnowledgeSearchComparisonRanking OriginalVector,
    AiKnowledgeSearchComparisonRanking? RewrittenKeyword, AiKnowledgeSearchComparisonRanking? RewrittenVector,
    AiKnowledgeSearchComparisonRanking? Reranked, int? RerankCandidateCount,
    IReadOnlyList<AiKnowledgeSearchRefinementOperation> Operations, AiKnowledgeSearchDiagnostics? Diagnostics);
