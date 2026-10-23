using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>原生 DOCX 工具适配统一完整分块；失败不提供部分正文。</summary>
internal sealed record AiKnowledgeDocxModelChunkResult(AiKnowledgeDocumentModelChunks? Output,
    AiKnowledgeModelChunkFailure Failure, AiKnowledgeChunkFailure ChunkFailure = AiKnowledgeChunkFailure.None)
{
    internal bool IsSuccess => Output is not null;
}

/// <summary>保留轻量 DOCX 隔离工具的真实消费者，完整规划和持久化统一使用结构来源。</summary>
internal static class AiKnowledgeDocxModelChunker
{
    internal static AiKnowledgeDocxModelChunkResult Chunk(AiKnowledgeBaseRecord knowledge, AiModelConfigRecord model,
        AiKnowledgeParsedDocx document, AiKnowledgeChunkOptions options, CancellationToken token)
        => AiKnowledgeTelemetry.Run("knowledge_chunk_docx", () => ChunkCore(knowledge, model, document, options, token), token);

    private static AiKnowledgeDocxModelChunkResult ChunkCore(AiKnowledgeBaseRecord knowledge, AiModelConfigRecord model,
        AiKnowledgeParsedDocx document, AiKnowledgeChunkOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(knowledge); ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        var parsed = AiKnowledgeParsedDocument.FromDocx(document, token);
        if (parsed is null) return new(null, AiKnowledgeModelChunkFailure.ChunkingFailed, AiKnowledgeChunkFailure.InvalidDocument);
        var result = AiKnowledgeDocumentModelChunker.Chunk(knowledge, model, parsed, options, token);
        return new(result.Output, result.Failure, result.ChunkFailure);
    }
}