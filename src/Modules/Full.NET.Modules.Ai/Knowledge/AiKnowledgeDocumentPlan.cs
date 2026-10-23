using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>不可变全量计划中的有界付费输入批次；本身不包含付费回执或持久化证明。</summary>
internal sealed record AiKnowledgeDocumentBatch(int Ordinal, string SourceHash, IReadOnlyList<AiKnowledgeDocumentChunk> Chunks);
/// <summary>完整来源计划的稳定摘要；不是向量持久化或激活证明。</summary>
internal sealed record AiKnowledgeDocumentPlanSummary(string FormatKey, string PlanHash, int BatchCount, int ChunkCount, long TokenCount);

/// <summary>统一来源、所有逻辑块及精确模型输入共同绑定的完整计划；不能从部分块摘要恢复。</summary>
internal sealed class AiKnowledgeDocumentPlan
{
    internal const string FormatKey = "knowledge-document-plan-v1";
    internal AiKnowledgeDocumentModelChunks Source { get; }
    internal string PlanHash { get; }
    internal string DocumentHash { get; }
    internal long TokenCount { get; }
    internal IReadOnlyList<AiKnowledgeDocumentBatch> Batches { get; }
    internal AiKnowledgeDocumentPlanSummary Summary { get; }

    private AiKnowledgeDocumentPlan(AiKnowledgeDocumentModelChunks source, string hash, string documentHash, long tokens, AiKnowledgeDocumentBatch[] batches)
    {
        Source = source; PlanHash = hash; DocumentHash = documentHash; TokenCount = tokens; Batches = Array.AsReadOnly(batches);
        Summary = new(FormatKey, hash, batches.Length, source.Chunks.Count, tokens);
    }

    /// <summary>重算全部 Token 并逐块验证连续覆盖及原文坐标；不允许省略尾部、低置信度或空缓存状态。</summary>
    internal static AiKnowledgeDocumentPlan? Create(AiKnowledgeDocumentModelChunks source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(source);
        if (source.KnowledgeBaseId == Guid.Empty || source.KnowledgeBaseVersion <= 0 || source.TenantId == Guid.Empty
            || source.ModelConfigId == Guid.Empty || source.ModelConfigVersion <= 0 || source.ProviderKey != "openai_compatible"
            || source.ModelId is not ("text-embedding-3-small" or "text-embedding-3-large")
            || source.ParserRuntimeId is not { Length: 71 } || !source.ParserRuntimeId.StartsWith("sha256:", StringComparison.Ordinal)
            || source.ParserRuntimeId.Skip(7).Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || !AiKnowledgePolicy.IsKnownClassification(source.DataClassification) || source.DestinationHash is not { Length: 64 }
            || source.DestinationHash.Any(c => !char.IsAsciiHexDigit(c)) || source.DestinationHash != source.DestinationHash.ToLowerInvariant()
            || source.TokenizerVersion != AiKnowledgeCl100kTokenizer.Version || source.Options != new AiKnowledgeChunkOptions()) return null;
        var d = source.Document;
        if (d is null) return null;
        var document = AiKnowledgeParsedDocument.FromBlocks(d.Format, d.ParserVersion, d.ContentHash, d.SizeBytes, d.Blocks, token, d.Policy);
        if (document is null || document.Blocks.Any(b => b.Position.Image is { Confidence: < 0.8 })) return null;
        var count = source.Chunks?.Count ?? 0; if (count is < 1 or > 2048) return null;
        var chunks = new AiKnowledgeDocumentChunk[count];
        for (var index = 0; index < count; index++) { token.ThrowIfCancellationRequested(); chunks[index] = source.Chunks![index]; }
        var tokenizer = AiKnowledgeCl100kTokenizer.Get(token); long tokens = 0; var characters = 0; var next = 0;
        foreach (var block in document.Blocks)
        {
            token.ThrowIfCancellationRequested(); if (string.IsNullOrWhiteSpace(block.Text)) continue;
            var end = 0; var previous = -1;
            while (next < chunks.Length && chunks[next]?.BlockSequence == block.Sequence)
            {
                token.ThrowIfCancellationRequested(); var c = chunks[next];
                if (c.Position is null || c.Text is null || c.Sequence != next) return null;
                var offset = c.Position.Offset - block.Position.Offset;
                if (offset <= previous || offset > end || c.Position.Length <= 0 || c.Position.Length > block.Text.Length - offset
                    || offset + c.Position.Length <= end || block.Locate(offset, c.Position.Length) != c.Position
                    || c.Text.Length != c.Position.Length || !block.Text.AsSpan(offset, c.Position.Length).SequenceEqual(c.Text)
                    || c.TokenCount is < 1 or > 512 || tokenizer.CountTokens(c.Text) != c.TokenCount
                    || AiEmbeddingContentPolicy.ValidateInputs([c.Text]) is not null) return null;
                end = offset + c.Position.Length; previous = offset; tokens += c.TokenCount; characters += c.Text.Length; next++;
                if (tokens > source.Options.MaxTotalTokens || characters > AiKnowledgeTextChunker.MaxTotalCharacters) return null;
            }
            if (end != block.Text.Length) return null;
        }
        if (next != count) return null;
        var frozen = source with { Document = document, Chunks = Array.AsReadOnly(chunks) };
        var wire = AiKnowledgeParsedWireCodec.Encode(document, token); string documentHash;
        try { documentHash = Digest(writer => { writer.Write("knowledge-document-runtime-v1"); writer.Write(source.ParserRuntimeId); writer.Write(wire.Length); writer.Write(wire); }); }
        finally { CryptographicOperations.ZeroMemory(wire); }
        var batches = new List<AiKnowledgeDocumentBatch>(); var start = 0;
        while (start < chunks.Length)
        {
            token.ThrowIfCancellationRequested(); var finish = start; var chars = 0;
            while (finish < chunks.Length && finish - start < AiEmbeddingContentPolicy.MaxBatchSize
                && chunks[finish].Text.Length <= AiEmbeddingContentPolicy.MaxTotalCharacters - chars)
            { chars += chunks[finish].Text.Length; finish++; }
            if (finish == start) return null;
            var selected = Array.AsReadOnly(chunks[start..finish]); var ordinal = batches.Count;
            var hash = Digest(writer =>
            {
                Header(writer, frozen, documentHash); writer.Write(ordinal); writer.Write(selected.Count);
                foreach (var c in selected)
                {
                    token.ThrowIfCancellationRequested(); writer.Write(c.Sequence); writer.Write(c.BlockSequence);
                    writer.Write(c.Position.Offset); writer.Write(c.Position.Length); writer.Write(c.TokenCount); writer.Write(c.Text);
                }
            });
            batches.Add(new(ordinal, hash, selected)); start = finish;
        }
        var planHash = Digest(writer =>
        {
            Header(writer, frozen, documentHash); writer.Write(count); writer.Write(tokens); writer.Write(batches.Count);
            foreach (var batch in batches) { token.ThrowIfCancellationRequested(); writer.Write(batch.Ordinal); writer.Write(batch.SourceHash); }
        });
        token.ThrowIfCancellationRequested(); return new(frozen, planHash, documentHash, tokens, batches.ToArray());
    }

    private static void Header(BinaryWriter writer, AiKnowledgeDocumentModelChunks source, string documentHash)
    {
        writer.Write(FormatKey); writer.Write(documentHash); writer.Write(source.KnowledgeBaseId.ToString("N")); writer.Write(source.KnowledgeBaseVersion);
        writer.Write(source.TenantId?.ToString("N") ?? "host"); writer.Write(source.ModelConfigId.ToString("N")); writer.Write(source.ModelConfigVersion);
        writer.Write(source.ProviderKey); writer.Write(source.ModelId); writer.Write(source.TokenizerVersion);
        writer.Write(source.DataClassification); writer.Write(source.DestinationHash); writer.Write(source.ParserRuntimeId);
        writer.Write(source.Options.MaxTokens); writer.Write(source.Options.OverlapTokens); writer.Write(source.Options.MaxChunks); writer.Write(source.Options.MaxTotalTokens);
    }

    private static string Digest(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        try
        {
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true)) { write(writer); writer.Flush(); }
            return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
        }
        finally { CryptographicOperations.ZeroMemory(stream.GetBuffer()); }
    }
}
