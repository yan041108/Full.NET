using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>完整 DOCX 计划摘要，不表示向量已保存或文档已就绪。</summary>
internal sealed record AiKnowledgeIndexDocxPlanSummary(string FormatKey, string PlanHash, int BatchCount, int ChunkCount, long TokenCount);
/// <summary>冻结正文及位置的稳定批次，不含向量或费用回执。</summary>
internal sealed record AiKnowledgeIndexDocxBatch(int Ordinal, string SourceHash, IReadOnlyList<AiKnowledgeDocxChunk> Chunks);
/// <summary>仅消费完整本地分块，不授予来源、收费、持久化或激活资格。</summary>
internal sealed class AiKnowledgeIndexDocxPlan
{
    internal const string FormatKey = "knowledge-docx-plan-v1";
    internal AiKnowledgeDocxModelChunks Source { get; }
    internal AiKnowledgeIndexDocxPlanSummary Summary { get; }
    internal IReadOnlyList<AiKnowledgeIndexDocxBatch> Batches { get; }
    private AiKnowledgeIndexDocxPlan(AiKnowledgeDocxModelChunks source, AiKnowledgeIndexDocxPlanSummary summary, IReadOnlyList<AiKnowledgeIndexDocxBatch> batches)
    { Source = source; Summary = summary; Batches = batches; }

    /// <summary>冻结全量输入并重算真实 Token 和连续覆盖；不得从摘要列表或部分块恢复完整计划。</summary>
    internal static AiKnowledgeIndexDocxPlan? Create(AiKnowledgeDocxModelChunks source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(source);
        if (source.KnowledgeBaseId == Guid.Empty || source.KnowledgeBaseVersion <= 0 || source.TenantId == Guid.Empty || source.ModelConfigId == Guid.Empty || source.ModelConfigVersion <= 0
            || source.ProviderKey != "openai_compatible" || source.ModelId is not ("text-embedding-3-small" or "text-embedding-3-large")
            || source.TokenizerVersion != AiKnowledgeCl100kTokenizer.Version || source.Options != new AiKnowledgeChunkOptions()) return null;
        var count = source.Chunks?.Count ?? 0; if (count is < 1 or > 2048) return null;
        var document = AiKnowledgeDocxStructure.Freeze(source.Document, token); if (document is null) return null;
        var chunks = new AiKnowledgeDocxChunk[count];
        for (var index = 0; index < count; index++) { token.ThrowIfCancellationRequested(); chunks[index] = source.Chunks![index]; }
        var frozen = source with { Document = document, Chunks = Array.AsReadOnly(chunks) };
        var tokenizer = AiKnowledgeCl100kTokenizer.Get(token); long tokens = 0; var characters = 0; var next = 0;
        foreach (var block in document.Blocks)
        {
            token.ThrowIfCancellationRequested(); var text = document.Text.AsSpan(block.Offset, block.Length);
            if (text.IsWhiteSpace()) continue;
            var end = block.Offset; var offset = block.Offset - 1;
            while (next < chunks.Length && chunks[next]?.BlockSequence == block.Sequence)
            {
                var chunk = chunks[next]; token.ThrowIfCancellationRequested();
                if (chunk.Sequence != next || chunk.Offset <= offset || chunk.Offset > end || chunk.Length <= 0 || chunk.Length > block.Offset + block.Length - chunk.Offset
                    || chunk.Offset + chunk.Length <= end || char.IsLowSurrogate(document.Text[chunk.Offset])
                    || chunk.Offset + chunk.Length < document.Text.Length && char.IsLowSurrogate(document.Text[chunk.Offset + chunk.Length])
                    || chunk.Text is null || chunk.Text.Length != chunk.Length
                    || !document.Text.AsSpan(chunk.Offset, chunk.Length).SequenceEqual(chunk.Text)
                    || chunk.TokenCount is < 1 or > 512 || tokenizer.CountTokens(chunk.Text) != chunk.TokenCount
                    || AiEmbeddingContentPolicy.ValidateInputs([chunk.Text]) is not null) return null;
                end = chunk.Offset + chunk.Length; offset = chunk.Offset; tokens += chunk.TokenCount; characters += chunk.Length; next++;
                if (tokens > source.Options.MaxTotalTokens || characters > AiKnowledgeTextChunker.MaxTotalCharacters) return null;
            }
            if (end != block.Offset + block.Length) return null;
        }
        if (next != chunks.Length) return null;
        var documentHash = AiKnowledgeDocxPlanHash.Document(document, token); var batches = new List<AiKnowledgeIndexDocxBatch>(); var start = 0;
        while (start < chunks.Length)
        {
            token.ThrowIfCancellationRequested(); var finish = start; var chars = 0;
            while (finish < chunks.Length && finish - start < AiEmbeddingContentPolicy.MaxBatchSize && chunks[finish].Length <= AiEmbeddingContentPolicy.MaxTotalCharacters - chars)
            { chars += chunks[finish].Length; finish++; }
            if (finish == start) return null;
            var selected = Array.AsReadOnly(chunks[start..finish]); var ordinal = batches.Count;
            batches.Add(new(ordinal, AiKnowledgeDocxPlanHash.Batch(frozen, documentHash, ordinal, selected, token), selected)); start = finish;
        }
        var all = Array.AsReadOnly(batches.ToArray()); var hash = AiKnowledgeDocxPlanHash.Plan(frozen, documentHash, all, tokens, token);
        token.ThrowIfCancellationRequested(); return new(frozen, new(FormatKey, hash, all.Count, chunks.Length, tokens), all);
    }
}
