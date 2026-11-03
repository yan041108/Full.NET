using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>显式确认收费后，在同一完整授权候选集上比较字面和向量策略；不接受客户端向量或正文。</summary>
/// <param name="KnowledgeBaseIds">搜索选择的知识库集合。</param>
/// <param name="EmbeddingModelConfigId">已批准的索引及查询向量模型。</param>
/// <param name="Query">两组策略共同使用的原查询，沿用关键词有界规则。</param>
/// <param name="Limit">每组展示一至二十块，不静默裁剪单块正文。</param>
/// <param name="IncludeDiagnostics">需要独立诊断权限。</param>
/// <param name="SearchScope">文档范围、精确模型版本和完整发布身份，服务器独立复核。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompareAiKnowledgeSearchRequest(IReadOnlyList<Guid> KnowledgeBaseIds, Guid EmbeddingModelConfigId,
    string Query, int Limit, bool IncludeDiagnostics, AiKnowledgeSearchAnswerScope SearchScope);

/// <summary>不同策略保持独立分数语义；更多结果仅表示有界完整候选中的排名前缀。</summary>
/// <param name="Strategy">实际执行的固定策略版本。</param>
/// <param name="ScoreKind">词频或余弦分数语义，不是正确率。</param>
/// <param name="HasMore">当前排名前缀后仍有结果，不暴露隐藏候选总量。</param>
/// <param name="Hits">完整片段及独立来源位置。</param>
public sealed record AiKnowledgeSearchComparisonRanking(string Strategy, string ScoreKind, bool HasMore, IReadOnlyList<AiKnowledgeSearchHit> Hits);

/// <summary>仅暴露本次服务器操作身份及真实用量报告状态；unknown 不能解释为零费用。</summary>
/// <param name="OperationId">查询向量操作身份。</param>
/// <param name="Status">reported 表示已报告输入/输出用量；unknown 表示用量未知，不认证价格或账单。</param>
public sealed record AiKnowledgeSearchComparisonUsage(Guid OperationId, string Status);

/// <summary>同一原查询、同一完整来源与同一次候选读取的已完成比较，不生成回答或替换生产默认策略。</summary>
/// <param name="Strategy">比较协议版本。</param>
/// <param name="Query">实际参与两组策略的原查询。</param>
/// <param name="Sources">冻结并复核的完整来源。</param>
/// <param name="Keyword">字面词频排名。</param>
/// <param name="Vector">余弦排名。</param>
/// <param name="Usage">唯一查询向量操作的报告状态。</param>
/// <param name="Diagnostics">只有额外授权后才返回本次完整候选计数。</param>
public sealed record AiKnowledgeSearchComparisonResponse(string Strategy, string Query, IReadOnlyList<AiKnowledgeSearchSource> Sources,
    AiKnowledgeSearchComparisonRanking Keyword, AiKnowledgeSearchComparisonRanking Vector, AiKnowledgeSearchComparisonUsage Usage,
    AiKnowledgeSearchDiagnostics? Diagnostics);
