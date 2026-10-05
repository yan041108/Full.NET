using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>完整分页规划摘要；不能证明向量已持久化或允许 ready。</summary>
internal sealed record AiKnowledgeIndexPdfPlanSummary(string FormatKey, string PlanHash, int BatchCount, int ChunkCount, long TokenCount);
/// <summary>精确来源及批准模型身份；总页数绑定连续分页集合。</summary>
internal sealed record AiKnowledgeIndexPdfPlanIdentity(Guid KnowledgeBaseId, int KnowledgeBaseVersion, Guid? TenantId,
    Guid ModelConfigId, int ModelConfigVersion, string ProviderKey, string ModelId, string ContentHash, int PageCount);
/// <summary>冻结页内位置的稳定批次；不含向量或凭据。</summary>
internal sealed record AiKnowledgeIndexPdfBatch(int Ordinal, string SourceHash, IReadOnlyList<AiKnowledgePdfChunk> Chunks);

/// <summary>消费已验收的完整分页分块，不授予来源访问、派发或持久化资格。</summary>
internal sealed class AiKnowledgeIndexPdfPlan
{
    internal const string FormatKey = "knowledge-pdf-plan-v1";
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    internal AiKnowledgeIndexPdfPlanIdentity Identity { get; }
    internal AiKnowledgeIndexPdfPlanSummary Summary { get; }
    internal IReadOnlyList<AiKnowledgeIndexPdfBatch> Batches { get; }
    private AiKnowledgeIndexPdfPlan(AiKnowledgeIndexPdfPlanIdentity identity, AiKnowledgeIndexPdfPlanSummary summary, IReadOnlyList<AiKnowledgeIndexPdfBatch> batches)
    { Identity = identity; Summary = summary; Batches = batches; }

    /// <summary>先验证并冻结全量输入，任一页、位置或预算无效时不返回部分计划。</summary>
    internal static AiKnowledgeIndexPdfPlan? Create(AiKnowledgePdfModelChunks source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(source);
        if (source.KnowledgeBaseId == Guid.Empty || source.KnowledgeBaseVersion <= 0 || source.TenantId == Guid.Empty
            || source.ModelConfigId == Guid.Empty || source.ModelConfigVersion <= 0 || source.ProviderKey != "openai_compatible"
            || source.ModelId is not ("text-embedding-3-small" or "text-embedding-3-large")
            || source.ParserVersion != AiKnowledgeParsedPdf.ParserVersion || source.TokenizerVersion != AiKnowledgeCl100kTokenizer.Version
            || source.Options != new AiKnowledgeChunkOptions() || !AiKnowledgeIndexPdfManifestCodec.Hash(source.ContentHash)
            || source.Chunks is null || source.Chunks.Count is < 1 or > 2048) return null;
        try
        {
            var chunks = new AiKnowledgePdfChunk[source.Chunks.Count]; long tokens = 0;
            var page = 0; var offset = -1; var end = 0; long coveredCharacters = 0; long copiedCharacters = 0;
            for (var index = 0; index < chunks.Length; index++)
            {
                token.ThrowIfCancellationRequested(); var chunk = source.Chunks[index];
                if (chunk is null || chunk.Sequence != index || chunk.PageNumber is < 1 or > 128
                    || chunk.Offset < 0 || chunk.Length <= 0 || chunk.Text is null || chunk.Length != chunk.Text.Length
                    || (long)chunk.Offset + chunk.Length > AiKnowledgeTextParser.MaxOutputCharacters
                    || chunk.TokenCount is < 1 or > 512 || chunk.EmbeddingText != chunk.Text || chunk.Text.Contains('\0')
                    || AiEmbeddingContentPolicy.ValidateInputs([chunk.EmbeddingText]) is not null) return null;
                if (chunk.PageNumber != page)
                {
                    if (chunk.PageNumber != page + 1) return null;
                    coveredCharacters += end; page = chunk.PageNumber; offset = -1; end = 0;
                }
                // 页内重叠可存在，但起点和新覆盖终点必须同时前进；换页才重置位置。
                var nextEnd = chunk.Offset + chunk.Length;
                if (chunk.Offset <= offset || nextEnd <= end) return null;
                offset = chunk.Offset; end = nextEnd; tokens += chunk.TokenCount; copiedCharacters += chunk.Length;
                if (coveredCharacters + end > AiKnowledgeTextParser.MaxOutputCharacters
                    || copiedCharacters > AiKnowledgeTextChunker.MaxTotalCharacters || tokens > source.Options.MaxTotalTokens) return null;
                _ = Utf8.GetByteCount(chunk.Text); chunks[index] = chunk;
            }
            var identity = new AiKnowledgeIndexPdfPlanIdentity(source.KnowledgeBaseId, source.KnowledgeBaseVersion, source.TenantId,
                source.ModelConfigId, source.ModelConfigVersion, source.ProviderKey, source.ModelId, source.ContentHash.ToLowerInvariant(), page);
            var batches = new List<AiKnowledgeIndexPdfBatch>(); var start = 0;
            while (start < chunks.Length)
            {
                token.ThrowIfCancellationRequested(); var finish = start; var characters = 0;
                while (finish < chunks.Length && finish - start < AiEmbeddingContentPolicy.MaxBatchSize
                    && characters + chunks[finish].EmbeddingText.Length <= AiEmbeddingContentPolicy.MaxTotalCharacters)
                { characters += chunks[finish].EmbeddingText.Length; finish++; }
                var selected = Array.AsReadOnly(chunks[start..finish]); var ordinal = batches.Count;
                batches.Add(new(ordinal, ComputeBatchHash(identity, ordinal, selected, token), selected)); start = finish;
            }
            var summary = new AiKnowledgeIndexPdfPlanSummary(FormatKey, "", batches.Count, chunks.Length, tokens);
            var metadata = batches.Select(Metadata).ToArray();
            summary = summary with { PlanHash = ComputePlanHash(identity, summary, metadata, token) };
            token.ThrowIfCancellationRequested(); return new(identity, summary, Array.AsReadOnly(batches.ToArray()));
        }
        catch (EncoderFallbackException) { return null; }
    }

    /// <summary>批次正文与位置共同进入摘要；等量或等维度不能代替输入一致。</summary>
    internal static string ComputeBatchHash(AiKnowledgeIndexPdfPlanIdentity identity, int ordinal, IReadOnlyList<AiKnowledgePdfChunk> chunks, CancellationToken token) => Digest(writer =>
    {
        WriteHeader(writer, identity); writer.Write(ordinal); writer.Write(chunks.Count);
        foreach (var chunk in chunks)
        {
            token.ThrowIfCancellationRequested(); writer.Write(chunk.Sequence); writer.Write(chunk.PageNumber);
            writer.Write(chunk.Offset); writer.Write(chunk.Length); writer.Write(chunk.TokenCount); Text(writer, chunk.Text); Text(writer, chunk.EmbeddingText);
        }
    });

    /// <summary>总摘要覆盖页范围和全部批次元数据；清单解码不能仅验证正文摘要列表。</summary>
    internal static string ComputePlanHash(AiKnowledgeIndexPdfPlanIdentity identity, AiKnowledgeIndexPdfPlanSummary summary,
        IReadOnlyList<AiKnowledgeIndexPdfManifestBatch> batches, CancellationToken token) => Digest(writer =>
    {
        WriteHeader(writer, identity); writer.Write(summary.BatchCount); writer.Write(summary.ChunkCount); writer.Write(summary.TokenCount);
        foreach (var batch in batches) { token.ThrowIfCancellationRequested(); WriteBatch(writer, batch); }
    });

    internal static AiKnowledgeIndexPdfManifestBatch Metadata(AiKnowledgeIndexPdfBatch batch) => new(batch.Ordinal, batch.Chunks[0].Sequence,
        batch.Chunks.Count, batch.Chunks.Sum(chunk => (long)chunk.TokenCount), batch.Chunks[0].PageNumber, batch.Chunks[^1].PageNumber, batch.SourceHash);

    /// <summary>PDF 独立 v1 格式：小端整数、int32 长度 UTF-8 字符串及规范 N 形状 Guid。</summary>
    internal static void WriteHeader(BinaryWriter writer, AiKnowledgeIndexPdfPlanIdentity identity)
    {
        Text(writer, FormatKey); Text(writer, identity.KnowledgeBaseId.ToString("N")); writer.Write(identity.KnowledgeBaseVersion);
        Text(writer, identity.TenantId?.ToString("N") ?? "host"); Text(writer, identity.ModelConfigId.ToString("N")); writer.Write(identity.ModelConfigVersion);
        Text(writer, identity.ProviderKey); Text(writer, identity.ModelId); Text(writer, identity.ContentHash);
        Text(writer, AiKnowledgeParsedPdf.ParserVersion); Text(writer, AiKnowledgeCl100kTokenizer.Version); Text(writer, "pdf-page-window-v1");
        writer.Write(identity.PageCount); var options = new AiKnowledgeChunkOptions();
        writer.Write(options.MaxTokens); writer.Write(options.OverlapTokens); writer.Write(options.MaxChunks); writer.Write(options.MaxTotalTokens);
    }

    internal static void WriteBatch(BinaryWriter writer, AiKnowledgeIndexPdfManifestBatch batch)
    {
        writer.Write(batch.Ordinal); writer.Write(batch.FirstSequence); writer.Write(batch.ChunkCount); writer.Write(batch.TokenCount);
        writer.Write(batch.FirstPageNumber); writer.Write(batch.LastPageNumber); Text(writer, batch.SourceHash);
    }
    internal static void Text(BinaryWriter writer, string value)
    { var bytes = Utf8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
    private static string Digest(Action<BinaryWriter> write)
    {
        // 流式摘要不累积完整正文编码，临时字节数组受单块 8000 字符约束。
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var writer = new BinaryWriter(new HashStream(hash), Utf8); write(writer); writer.Flush();
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private sealed class HashStream(IncrementalHash hash) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => hash.AppendData(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => hash.AppendData(buffer);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
