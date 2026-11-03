using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>独立搜索与诊断各有精确权限；不授予收费问答或模型派发资格。</summary>
public static class AiKnowledgeSearchPermissions
{
    /// <summary>读取当前有权访问的已发布知识命中。</summary>
    public const string Read = "ai.knowledge_search.read";
    /// <summary>读取本次已授权候选的有界诊断。</summary>
    public const string Diagnostics = "ai.knowledge_search.diagnostics";
    /// <summary>明确派发一次已批准查询向量并比较策略，不授予聊天或回答生成资格。</summary>
    public const string Compare = "ai.knowledge_search.compare";
    /// <summary>在批准知识范围中明确执行收费改写/重排，不授予会话或自由聊天资格。</summary>
    public const string Refine = "ai.knowledge_search.refine";
}

/// <summary>绑定当前发布与文件版本，不能仅用块序号追随下一份发布。</summary>
/// <param name="KnowledgeBaseId">知识库身份。</param>
/// <param name="KnowledgeBaseVersion">目录并发版本。</param>
/// <param name="DocumentId">文档身份。</param>
/// <param name="DocumentVersion">发布冻结的文档版本，不随新草稿或当前标题变化。</param>
/// <param name="VersionId">不可变文件版本。</param>
/// <param name="PublicationId">当前发布身份。</param>
/// <param name="GenerationId">完整索引代次。</param>
/// <param name="ManifestHash">完整清单摘要。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeSearchSource(Guid KnowledgeBaseId, int KnowledgeBaseVersion, Guid DocumentId,
    int DocumentVersion, Guid VersionId, Guid PublicationId, Guid GenerationId, string ManifestHash)
{
    /// <summary>静态来源协议；FAQ 修订不能降级为普通文件或使用文件下载入口。</summary>
    public string SourceKind { get; init; } = "document-v1";
    /// <summary>FAQ 数据库的准确不可变修订，仅 faq-revision-v1 协议允许。</summary>
    public Guid? FaqRevisionId { get; init; }
    /// <summary>FAQ 根身份；与准确修订、发布及完整索引一起核证，普通文件必须为空。</summary>
    public Guid? FaqId { get; init; }
}

/// <summary>不调用模型的关键词搜索；全部词项按不依赖语言区域的字面规则匹配。</summary>
/// <param name="KnowledgeBaseIds">最多八个独立知识库，同一已批准索引空间。</param>
/// <param name="DocumentIds">可选的最多八个独立文档；空数组表示所选知识库范围。</param>
/// <param name="EmbeddingModelConfigId">已发布清单绑定的模型配置，不授予调用模型资格。</param>
/// <param name="EmbeddingModelVersion">读取时批准的精确模型版本。</param>
/// <param name="Query">最多五百一十二个 UTF-16 单元、十六个字面词项。</param>
/// <param name="Limit">返回排名前一至二十项，同时返回真实总命中数。</param>
/// <param name="IncludeDiagnostics">需要额外诊断权限，不能静默降级。</param>
/// <param name="ExpectedSources">重复查询时可绑定此前完整来源集合，漂移须显式重新采用。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SearchAiKnowledgeRequest(IReadOnlyList<Guid> KnowledgeBaseIds, IReadOnlyList<Guid> DocumentIds,
    Guid EmbeddingModelConfigId, int EmbeddingModelVersion, string Query, int Limit, bool IncludeDiagnostics,
    IReadOnlyList<AiKnowledgeSearchSource>? ExpectedSources);

/// <summary>命中正文与位置只来自已证明的完整发布代次；人工更正不伪造原文坐标。</summary>
/// <param name="Source">被冻结并重新核验的发布身份。</param>
/// <param name="DocumentTitle">当前有权访问的标题，与元数据版本配对，不冒充发布时标题。</param>
/// <param name="DocumentMetadataVersion">当前标题所属的元数据版本，与发布冻结版本明确区分。</param>
/// <param name="Sequence">代次中的块序号。</param>
/// <param name="Text">完整有界命中块。</param>
/// <param name="Score">全部词项的有界字面出现次数，不是正确率。</param>
/// <param name="PageNumber">真实页码。</param>
/// <param name="Offset">原始文本位置；人工修订为空。</param>
/// <param name="Length">原始文本长度；人工修订为空。</param>
/// <param name="SourceLocation">真实结构位置。</param>
/// <param name="DocxLocation">既有 DOCX 位置。</param>
/// <param name="Correction">人工修订的完整来源集合及补写性质。</param>
public sealed record AiKnowledgeSearchHit(AiKnowledgeSearchSource Source, string DocumentTitle, int DocumentMetadataVersion, int Sequence, string Text,
    double Score, int? PageNumber, int? Offset, int? Length, AiKnowledgeSourceLocationResponse? SourceLocation,
    AiKnowledgeDocxLocationResponse? DocxLocation, AiKnowledgeCorrectionCitationResponse? Correction);

/// <summary>仅含本次全部已授权候选计数，不含秘密、提示词或未授权来源。</summary>
/// <param name="SourceCount">已证明的来源数量。</param>
/// <param name="CandidateCount">完整候选块数量。</param>
/// <param name="ScoreKind">稳定的分数语义。</param>
public sealed record AiKnowledgeSearchDiagnostics(int SourceCount, int CandidateCount, string ScoreKind);

/// <summary>候选全集已验证，结果排名前缀与超出返回数量明确区分。</summary>
/// <param name="Strategy">固定策略身份。</param>
/// <param name="Sources">参与本次读取的完整来源集合。</param>
/// <param name="TotalMatches">全部授权候选中的真实命中总数。</param>
/// <param name="Hits">排名前缀。</param>
/// <param name="Diagnostics">仅在请求且有权时返回。</param>
public sealed record AiKnowledgeSearchResponse(string Strategy, IReadOnlyList<AiKnowledgeSearchSource> Sources,
    int TotalMatches, IReadOnlyList<AiKnowledgeSearchHit> Hits, AiKnowledgeSearchDiagnostics? Diagnostics);
