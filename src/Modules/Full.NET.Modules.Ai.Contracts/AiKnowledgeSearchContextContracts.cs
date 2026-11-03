using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>只读取同一完整发布内的相邻块；不接受客户端正文、向量或位置。</summary>
/// <param name="Source">此前服务器交付的完整发布身份，漂移后必须重新搜索。</param>
/// <param name="EmbeddingModelConfigId">发布绑定的批准索引空间，不授予模型调用资格。</param>
/// <param name="EmbeddingModelVersion">精确模型版本。</param>
/// <param name="AnchorSequence">完整代次中从零开始的锚块序号。</param>
/// <param name="Radius">前后各零至两块；总正文最多三万二千七百六十八个 UTF-16 单元，超限整次拒绝。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReadAiKnowledgeSearchContextRequest(AiKnowledgeSearchSource Source, Guid EmbeddingModelConfigId,
    int EmbeddingModelVersion, int AnchorSequence, int Radius);

/// <summary>保持独立来源位置的上下文块，不能将相邻正文拼接成伪造的原文区间。</summary>
/// <param name="Sequence">完整代次中的真实块序号。</param>
/// <param name="Text">完整有界正文。</param>
/// <param name="PageNumber">真实页码。</param>
/// <param name="Offset">原文位置；人工修订为空。</param>
/// <param name="Length">原文长度；人工修订为空。</param>
/// <param name="SourceLocation">原生结构位置。</param>
/// <param name="DocxLocation">既有 DOCX 位置。</param>
/// <param name="Correction">人工更正/补写的真实来源集合。</param>
public sealed record AiKnowledgeSearchContextBlock(int Sequence, string Text, int? PageNumber, int? Offset, int? Length,
    AiKnowledgeSourceLocationResponse? SourceLocation, AiKnowledgeDocxLocationResponse? DocxLocation,
    AiKnowledgeCorrectionCitationResponse? Correction);

/// <summary>同一发布的连续邻接窗口，不含搜索分数或隐藏候选诊断，不调用模型。</summary>
/// <param name="Strategy">固定邻接策略身份。</param>
/// <param name="Source">重新核验后的完整发布身份。</param>
/// <param name="DocumentTitle">当前有权标题。</param>
/// <param name="DocumentMetadataVersion">当前标题版本，与发布冻结版本区分。</param>
/// <param name="AnchorSequence">请求锚块。</param>
/// <param name="Radius">实际采用的前后半径。</param>
/// <param name="HasPreviousBlocks">窗口之前是否还有发布内容。</param>
/// <param name="HasNextBlocks">窗口之后是否还有发布内容。</param>
/// <param name="Blocks">包含锚块的连续窗口；正文或位置不做推算和截断。</param>
public sealed record AiKnowledgeSearchContextResponse(string Strategy, AiKnowledgeSearchSource Source, string DocumentTitle,
    int DocumentMetadataVersion, int AnchorSequence, int Radius, bool HasPreviousBlocks, bool HasNextBlocks,
    IReadOnlyList<AiKnowledgeSearchContextBlock> Blocks);
