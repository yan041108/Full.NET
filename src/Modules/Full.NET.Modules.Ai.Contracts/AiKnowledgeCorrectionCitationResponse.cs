namespace Full.NET.Modules.Ai.Contracts;

/// <summary>人工修订中命中片段的完整来源集合；补写没有原文位置，更正仅指向原始上下文。</summary>
/// <param name="RevisionId">索引冻结的人工修订标识。</param>
/// <param name="TextLength">命中片段的 UTF-16 长度，不是原文件区间。</param>
/// <param name="Spans">按片段内偏移排序的全部来源交集，不截取第一个位置。</param>
public sealed record AiKnowledgeCorrectionCitationResponse(Guid RevisionId, int TextLength, IReadOnlyList<AiKnowledgeCorrectionSpanResponse> Spans);

/// <summary>片段中的一段文字及其真实来源性质；来源坐标属于原文，不能用补写或更正文长伪造。</summary>
/// <param name="ContentKind">稳定机器码：original、corrected 或 supplement。</param>
/// <param name="TextOffset">该交集在命中片段内的 UTF-16 偏移。</param>
/// <param name="TextLength">该交集在命中片段内的 UTF-16 长度。</param>
/// <param name="SourceLocation">原始逻辑位置；supplement 必须为空。</param>
/// <param name="SourceOffset">原文或原始上下文的偏移；supplement 必须为空。</param>
/// <param name="SourceLength">原文或原始上下文的长度；更正不声称与改写逐字对应。</param>
public sealed record AiKnowledgeCorrectionSpanResponse(string ContentKind, int TextOffset, int TextLength,
    AiKnowledgeSourceLocationResponse? SourceLocation, int? SourceOffset, int? SourceLength);
