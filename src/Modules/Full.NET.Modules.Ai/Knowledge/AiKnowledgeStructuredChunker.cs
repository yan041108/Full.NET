using Microsoft.ML.Tokenizers;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>由可信来源元数据显式选择的内部文本格式，不从正文猜测。</summary>
internal enum AiKnowledgeTextFormat { PlainText, Markdown }
/// <summary>内部识别的有界顶层结构类别，不是完整 Markdown AST。</summary>
internal enum AiKnowledgeTextBlockKind { Paragraph, Code, Table }
/// <summary>只引用来源中的标题或表头，不含模型生成上下文。</summary>
internal enum AiKnowledgeContextKind { Heading, TableHeader }
/// <summary>原文 UTF-16 区间；Level 仅表示 ATX 标题层级，表头为零。</summary>
internal sealed record AiKnowledgeSourceContext(AiKnowledgeContextKind Kind, int Offset, int Length, int Level = 0);
/// <summary>Text 是单一原文区间，EmbeddingText 含另外列出的重复来源区间；TokenCount 对完整后者计费。</summary>
internal sealed record AiKnowledgeStructuredChunk(int Sequence, int Offset, int Length, string Text, string EmbeddingText,
    int TokenCount, AiKnowledgeTextBlockKind Kind, IReadOnlyList<AiKnowledgeSourceContext> Context,
    IReadOnlyList<AiKnowledgeSourceContext> RepeatedContext);
/// <summary>绑定全部解释与计费输入，不作为持久化或公开传输协议。</summary>
internal sealed record AiKnowledgeStructuredChunks(string ContentHash, string ParserVersion, string TokenizerVersion,
    AiKnowledgeTextFormat Format, AiKnowledgeChunkOptions Options, IReadOnlyList<AiKnowledgeStructuredChunk> Chunks)
{
    internal string ChunkingVersion => "structure-window-v1";
}
/// <summary>失败不得提供部分正文或上下文。</summary>
internal sealed record AiKnowledgeStructuredChunkResult(AiKnowledgeStructuredChunks? Output, AiKnowledgeChunkFailure Failure)
{
    internal bool IsSuccess => Output is not null;
    internal static AiKnowledgeStructuredChunkResult Rejected(AiKnowledgeChunkFailure failure) => new(null, failure);
}

/// <summary>保留来源位置的有界结构分块，不渲染 Markdown、不选择模型或授予外发资格。</summary>
internal static class AiKnowledgeStructuredChunker
{
    internal const int MaxLines = 65536;
    internal const int MaxBlocks = 8192;
    internal const int MaxContextCharacters = 4096;

    /// <summary>真实词表须由后续调用边界核准；格式与来源授权也须来自可信元数据。</summary>
    /// <remarks>同步 Tokenizer 只在调用前后观察取消，不能强制终止；文本与重复来源区间分别保存。</remarks>
    /// <param name="document">已授权且摘要绑定的来源原文，位置不进行归一化或译写。</param>
    /// <param name="tokenizer">与已核准模型匹配、不带归一化或特殊控制 Token 的可信普通文本词表。</param>
    /// <param name="tokenizerVersion">词表与算法版本标识，后续索引还须与模型/代次共同复核。</param>
    /// <param name="format">来自权威版本元数据的明确格式，TXT 不解释标题或表格。</param>
    /// <param name="options">每块与总 Token 均包含重复上下文，重叠只限制原文后缀。</param>
    /// <param name="cancellationToken">在扫描、迭代和同步 Tokenizer 调用前后观察取消。</param>
    internal static AiKnowledgeStructuredChunkResult Chunk(AiKnowledgeParsedText document, TiktokenTokenizer tokenizer,
        string tokenizerVersion, AiKnowledgeTextFormat format, AiKnowledgeChunkOptions options, CancellationToken cancellationToken)
    {
        var validation = AiKnowledgeTextChunker.Validate(document, tokenizer, tokenizerVersion, options, cancellationToken);
        if (validation != AiKnowledgeChunkFailure.None) return AiKnowledgeStructuredChunkResult.Rejected(validation);
        if (format is not (AiKnowledgeTextFormat.PlainText or AiKnowledgeTextFormat.Markdown))
            return AiKnowledgeStructuredChunkResult.Rejected(AiKnowledgeChunkFailure.UnsupportedFormat);
        var blocks = AiKnowledgeTextStructure.Scan(document.Text, format, cancellationToken, out var structureFailure);
        if (blocks is null) return AiKnowledgeStructuredChunkResult.Rejected(structureFailure);
        var chunks = new List<AiKnowledgeStructuredChunk>();
        var totalTokens = 0;
        var totalCharacters = 0;
        var text = document.Text;
        foreach (var block in blocks)
        {
            var offset = block.Offset;
            var coveredEnd = block.Offset;
            while (offset < block.End)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (chunks.Count >= options.MaxChunks) return AiKnowledgeStructuredChunkResult.Rejected(AiKnowledgeChunkFailure.TooManyChunks);
                var repeated = block.Context.Where(context => context.Offset + context.Length <= offset).ToArray();
                var prefix = string.Concat(repeated.Select(context => text.Substring(context.Offset, context.Length)));
                // 首个表格块的原始表头属于正文，后续才属于重复上下文；二者都进入同一个 Token 窗口。
                var firstTableRows = block.HasRows && offset == block.Offset;
                var tokenOffset = firstTableRows ? block.RowsStart : offset;
                var tokenPrefix = firstTableRows ? prefix + text.Substring(offset, tokenOffset - offset) : prefix;
                if (tokenPrefix.Length > MaxContextCharacters) return AiKnowledgeStructuredChunkResult.Rejected(AiKnowledgeChunkFailure.ContextTooLarge);
                var end = AiKnowledgeTextChunker.FindChunkEnd(text, tokenOffset, block.End, tokenPrefix, block.Kind == AiKnowledgeTextBlockKind.Table,
                    tokenizer, options.MaxTokens, cancellationToken, out var count, out var failure);
                // 重叠不能让某块不覆盖新正文；只减少重叠，不删除上下文或静默截断来源。
                if (end <= coveredEnd || (firstTableRows && end <= tokenOffset))
                {
                    if (offset < coveredEnd) { offset = coveredEnd; continue; }
                    return AiKnowledgeStructuredChunkResult.Rejected(failure);
                }
                if (count <= 0) return AiKnowledgeStructuredChunkResult.Rejected(AiKnowledgeChunkFailure.InvalidTokenProfile);
                if (count > options.MaxTotalTokens - totalTokens) return AiKnowledgeStructuredChunkResult.Rejected(AiKnowledgeChunkFailure.TotalTokenBudgetExceeded);
                var length = end - offset;
                // 无上下文时复用同一个正文字符串；有上下文时两份实际正文分配均计入总字符预算。
                var charge = length + (prefix.Length == 0 ? 0 : prefix.Length + length);
                if (charge > AiKnowledgeTextChunker.MaxTotalCharacters - totalCharacters)
                    return AiKnowledgeStructuredChunkResult.Rejected(AiKnowledgeChunkFailure.OutputTooLarge);
                totalTokens += count;
                totalCharacters += charge;
                var body = text.Substring(offset, length);
                var embedding = prefix.Length == 0 ? body : prefix + body;
                chunks.Add(new(chunks.Count, offset, length, body, embedding, count, block.Kind, block.Context, Array.AsReadOnly(repeated)));
                coveredEnd = end;
                if (end == block.End) break;
                offset = AiKnowledgeTextChunker.OverlapStart(text, tokenOffset, end, tokenizer, options.OverlapTokens, cancellationToken);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(document.ContentHash, document.ParserVersion, tokenizerVersion, format, options,
            Array.AsReadOnly(chunks.ToArray())), AiKnowledgeChunkFailure.None);
    }
}
