using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>读取完整发布中的真实嵌入图片父子关系，不接受客户端正文或关系标记。</summary>
/// <param name="Source">此前服务器交付的完整发布身份。</param>
/// <param name="EmbeddingModelConfigId">发布绑定的批准索引模型，不授予模型调用资格。</param>
/// <param name="EmbeddingModelVersion">精确索引模型版本。</param>
/// <param name="AnchorSequence">完整代次中的锚块序号。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReadAiKnowledgeSearchRelationsRequest(AiKnowledgeSearchSource Source, Guid EmbeddingModelConfigId,
    int EmbeddingModelVersion, int AnchorSequence);

/// <summary>由不可变原文位置证明的父子边；合并块可同时包含两端，但不代表原文位置存在环。</summary>
/// <param name="ParentSequence">父正文所在索引块；原文父锚点未进入当前发布索引时为空。</param>
/// <param name="ChildSequence">图片正文所在索引块。</param>
/// <param name="ParentLocation">真实非图片父锚点，不能用相邻序号推断。</param>
/// <param name="ChildLocation">嵌入图片位置，其 Parent 必须精确等于父锚点。</param>
public sealed record AiKnowledgeSearchRelation(int? ParentSequence, int ChildSequence,
    AiKnowledgeSourceLocationResponse ParentLocation, AiKnowledgeSourceLocationResponse ChildLocation);

/// <summary>同一发布内的一层真实嵌入关系；最多五块、三万二千七百六十八个 UTF-16 单元和一百二十八条边，超限整次拒绝。</summary>
/// <param name="Strategy">固定真实嵌入关系策略身份。</param>
/// <param name="Source">重新核验的完整发布身份。</param>
/// <param name="DocumentTitle">当前有权标题。</param>
/// <param name="DocumentMetadataVersion">当前标题版本。</param>
/// <param name="AnchorSequence">请求锚块，返回正文始终包含该块。</param>
/// <param name="RelationState">none、complete 或 parent_not_indexed；缺少父正文不伪造补全。</param>
/// <param name="Relations">精确来源关系，不能按块序号邻接解释。</param>
/// <param name="Blocks">完整有界正文及各自原文位置，没有模型调用或费用结算。</param>
public sealed record AiKnowledgeSearchRelationsResponse(string Strategy, AiKnowledgeSearchSource Source, string DocumentTitle,
    int DocumentMetadataVersion, int AnchorSequence, string RelationState, IReadOnlyList<AiKnowledgeSearchRelation> Relations,
    IReadOnlyList<AiKnowledgeSearchContextBlock> Blocks);
