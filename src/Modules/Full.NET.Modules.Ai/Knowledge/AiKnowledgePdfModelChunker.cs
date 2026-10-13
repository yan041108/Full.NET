using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>PDF 页内 UTF-16 位置；块序号为整份文件的零基序号。</summary>
internal sealed record AiKnowledgePdfChunk(int Sequence, int PageNumber, int Offset, int Length, string Text, string EmbeddingText, int TokenCount);
/// <summary>来源与精确模型批准版本共同绑定完整分页分块。</summary>
internal sealed record AiKnowledgePdfModelChunks(Guid KnowledgeBaseId, int KnowledgeBaseVersion, Guid? TenantId,
    Guid ModelConfigId, int ModelConfigVersion, string ProviderKey, string ModelId, string ContentHash,
    string ParserVersion, string TokenizerVersion, AiKnowledgeChunkOptions Options, IReadOnlyList<AiKnowledgePdfChunk> Chunks)
{
    internal string ChunkingVersion => "pdf-page-window-v1";
}
/// <summary>任何页或全局预算失败都不提供部分块。</summary>
internal sealed record AiKnowledgePdfModelChunkResult(AiKnowledgePdfModelChunks? Output,
    AiKnowledgeModelChunkFailure Failure, AiKnowledgeChunkFailure ChunkFailure = AiKnowledgeChunkFailure.None)
{
    internal bool IsSuccess => Output is not null;
}
/// <summary>消费已验收的 PDF 分页，复用获批模型词表，不选择进程或授予外发资格。</summary>
internal static class AiKnowledgePdfModelChunker
{
    /// <summary>逐页按普通文本分块；重复上下文不跨页，总预算也不因换页重置。</summary>
    /// <remarks>来源授权、解析进程身份与派发前批准仍由后续 Worker 权威复核；这里只生成本地完整结果。</remarks>
    internal static AiKnowledgePdfModelChunkResult Chunk(AiKnowledgeBaseRecord knowledge, AiModelConfigRecord model,
        AiKnowledgeParsedPdf document, AiKnowledgeChunkOptions options, CancellationToken token)
        => AiKnowledgeTelemetry.Run("knowledge_chunk_pdf", () => ChunkCore(knowledge, model, document, options, token), token);

    /// <summary>保留逐页授权绑定与整份预算，观测不读取内容或修改分块结果。</summary>
    private static AiKnowledgePdfModelChunkResult ChunkCore(AiKnowledgeBaseRecord knowledge, AiModelConfigRecord model,
        AiKnowledgeParsedPdf document, AiKnowledgeChunkOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        if (AiKnowledgePdfOutputReader.ValidateDocument(document, token) != AiKnowledgePdfReadFailure.None)
            return Rejected(AiKnowledgeChunkFailure.InvalidDocument);
        var chunks = new List<AiKnowledgePdfChunk>();
        var totalTokens = 0;
        var totalCharacters = 0;
        foreach (var page in document.Pages)
        {
            token.ThrowIfCancellationRequested();
            // 首页先由既有入口验证 options；之后只向下收紧剩余预算，不构造非法的零预算。
            if (chunks.Count > 0 && chunks.Count >= options.MaxChunks) return Rejected(AiKnowledgeChunkFailure.TooManyChunks);
            if (chunks.Count > 0 && totalTokens >= options.MaxTotalTokens) return Rejected(AiKnowledgeChunkFailure.TotalTokenBudgetExceeded);
            var remaining = chunks.Count == 0 ? options : options with { MaxChunks = options.MaxChunks - chunks.Count, MaxTotalTokens = options.MaxTotalTokens - totalTokens };
            var text = new AiKnowledgeParsedText(page.Text, document.ContentHash, document.SizeBytes, AiKnowledgeTextSource.PdfPage);
            var result = AiKnowledgeModelChunker.ChunkCore(knowledge, model, text, AiKnowledgeTextFormat.PlainText, remaining, token);
            if (!result.IsSuccess) return new(null, result.Failure, result.ChunkFailure);
            foreach (var chunk in result.Output!.Chunks.Chunks)
            {
                token.ThrowIfCancellationRequested();
                if (chunk.Text.Length > AiKnowledgeTextChunker.MaxTotalCharacters - totalCharacters) return Rejected(AiKnowledgeChunkFailure.OutputTooLarge);
                totalTokens += chunk.TokenCount;
                totalCharacters += chunk.Text.Length;
                chunks.Add(new(chunks.Count, page.PageNumber, chunk.Offset, chunk.Length, chunk.Text, chunk.EmbeddingText, chunk.TokenCount));
            }
        }
        token.ThrowIfCancellationRequested();
        return new(new(knowledge.Id, knowledge.Version, knowledge.TenantId, model.Id, model.Version, model.ProviderKey,
            model.ModelId, document.ContentHash.ToLowerInvariant(), AiKnowledgeParsedPdf.ParserVersion, AiKnowledgeCl100kTokenizer.Version,
            options, Array.AsReadOnly(chunks.ToArray())), AiKnowledgeModelChunkFailure.None);
    }

    private static AiKnowledgePdfModelChunkResult Rejected(AiKnowledgeChunkFailure failure) => new(null, AiKnowledgeModelChunkFailure.ChunkingFailed, failure);
}
