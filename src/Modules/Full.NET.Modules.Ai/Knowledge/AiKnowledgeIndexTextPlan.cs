using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>完整计划的内部摘要，不授予派发授权，也不能证明向量已持久化。</summary>
internal sealed record AiKnowledgeIndexTextPlanSummary(string FormatKey, string PlanHash, int BatchCount, int ChunkCount, long TokenCount);
/// <summary>稳定批次顺序与冻结输入，不含向量或凭据。</summary>
internal sealed record AiKnowledgeIndexTextBatch(int Ordinal, string SourceHash, IReadOnlyList<AiKnowledgeStructuredChunk> Chunks);

/// <summary>有界完整规划；任何片段无效时不交付部分批次。</summary>
internal sealed class AiKnowledgeIndexTextPlan
{
    internal const string FormatKey = "knowledge-text-plan-v1";
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    internal AiKnowledgeIndexTextPlanSummary Summary { get; }
    internal AiKnowledgeIndexTextPlanIdentity Identity { get; }
    internal IReadOnlyList<AiKnowledgeIndexTextBatch> Batches { get; }
    private AiKnowledgeIndexTextPlan(AiKnowledgeIndexTextPlanIdentity identity, AiKnowledgeIndexTextPlanSummary summary, IReadOnlyList<AiKnowledgeIndexTextBatch> batches)
    { Identity = identity; Summary = summary; Batches = batches; }

    /// <summary>消费可信解析和获批分块结果；全部输入有效后才交付完整、不可变的计划。</summary>
    /// <remarks>摘要只说明规划内容，不证明来源授权、模型调用或数据库完整性。</remarks>
    internal static AiKnowledgeIndexTextPlan? Create(AiKnowledgeModelChunks source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(source);
        if (source.KnowledgeBaseId == Guid.Empty || source.KnowledgeBaseVersion <= 0 || source.TenantId == Guid.Empty
            || source.ModelConfigId == Guid.Empty || source.ModelConfigVersion <= 0 || source.ProviderKey != "openai_compatible"
            || source.ModelId is not ("text-embedding-3-small" or "text-embedding-3-large")
            || source.Chunks.ParserVersion != "utf8-text-v1" || source.Chunks.TokenizerVersion != AiKnowledgeCl100kTokenizer.Version
            || source.Chunks.Format is not (AiKnowledgeTextFormat.PlainText or AiKnowledgeTextFormat.Markdown)
            || source.Chunks.Options != new AiKnowledgeChunkOptions() || source.Chunks.ContentHash.Length != 64
            || source.Chunks.ContentHash.Any(character => !char.IsAsciiHexDigit(character))
            || source.Chunks.Chunks.Count is < 1 or > 2048) return null;
        try
        {
            // 先限定整个集合，再逐个限定上下文；复制后的对象不再暴露原数组。
            var chunks = new AiKnowledgeStructuredChunk[source.Chunks.Chunks.Count]; long tokens = 0;
            for (var index = 0; index < chunks.Length; index++)
            {
                token.ThrowIfCancellationRequested(); var chunk = source.Chunks.Chunks[index];
                if (chunk.Context.Count > 8 || chunk.RepeatedContext.Count > 8) return null;
                chunk = chunk with { Context = Array.AsReadOnly(chunk.Context.ToArray()), RepeatedContext = Array.AsReadOnly(chunk.RepeatedContext.ToArray()) };
                if (chunk.Sequence != index || chunk.Offset < 0 || chunk.Length <= 0 || chunk.Length != chunk.Text.Length
                    || (long)chunk.Offset + chunk.Length > AiKnowledgeTextParser.MaxOutputCharacters
                    || chunk.TokenCount is < 1 or > 512 || chunk.Kind is not (AiKnowledgeTextBlockKind.Paragraph or AiKnowledgeTextBlockKind.Code or AiKnowledgeTextBlockKind.Table)
                    || !chunk.EmbeddingText.EndsWith(chunk.Text, StringComparison.Ordinal)
                    || AiEmbeddingContentPolicy.ValidateInputs([chunk.EmbeddingText]) is not null
                    || !ValidContext(chunk.Context) || !ValidContext(chunk.RepeatedContext)) return null;
                // 严格编码在冻结时拒绝孤立代理项，不把替换字符写进稳定摘要。
                _ = Utf8.GetByteCount(chunk.Text); _ = Utf8.GetByteCount(chunk.EmbeddingText);
                chunks[index] = chunk; tokens += chunk.TokenCount;
            }
            if (tokens > source.Chunks.Options.MaxTotalTokens) return null;
            var batches = new List<AiKnowledgeIndexTextBatch>(); var start = 0;
            while (start < chunks.Length)
            {
                token.ThrowIfCancellationRequested(); var end = start; var characters = 0;
                while (end < chunks.Length && end - start < AiEmbeddingContentPolicy.MaxBatchSize
                    && characters + chunks[end].EmbeddingText.Length <= AiEmbeddingContentPolicy.MaxTotalCharacters)
                { characters += chunks[end].EmbeddingText.Length; end++; }
                var selected = Array.AsReadOnly(chunks[start..end]); var ordinal = batches.Count;
                var hash = Digest(writer =>
                {
                    Header(writer, source); writer.Write(ordinal); writer.Write(selected.Count);
                    foreach (var chunk in selected) { token.ThrowIfCancellationRequested(); WriteChunk(writer, chunk); }
                });
                batches.Add(new(ordinal, hash, selected)); start = end;
            }
            var planHash = Digest(writer =>
            {
                Header(writer, source); writer.Write(batches.Count); writer.Write(chunks.Length); writer.Write(tokens);
                foreach (var batch in batches) { token.ThrowIfCancellationRequested(); Text(writer, batch.SourceHash); }
            });
            token.ThrowIfCancellationRequested();
            return new(IdentityFrom(source), new(FormatKey, planHash, batches.Count, chunks.Length, tokens), Array.AsReadOnly(batches.ToArray()));
        }
        catch (EncoderFallbackException) { return null; }
    }

    private static bool ValidContext(IReadOnlyList<AiKnowledgeSourceContext> context) => context.Count <= 8 && context.All(item =>
        item.Offset >= 0 && item.Length > 0 && (long)item.Offset + item.Length <= AiKnowledgeTextParser.MaxOutputCharacters
        && (item.Kind == AiKnowledgeContextKind.Heading ? item.Level is >= 1 and <= 6 : item.Kind == AiKnowledgeContextKind.TableHeader && item.Level == 0));

    private static AiKnowledgeIndexTextPlanIdentity IdentityFrom(AiKnowledgeModelChunks source) => new(source.KnowledgeBaseId, source.KnowledgeBaseVersion,
        source.TenantId, source.ModelConfigId, source.ModelConfigVersion, source.ProviderKey, source.ModelId, source.Chunks.ContentHash, source.Chunks.Format);
    private static void Header(BinaryWriter writer, AiKnowledgeModelChunks source) => WriteHeader(writer, IdentityFrom(source));

    /// <summary>复用既有 v1 摘要头，不改变字段顺序；所有计划仅接受固定解析/词表/分块参数。</summary>
    internal static void WriteHeader(BinaryWriter writer, AiKnowledgeIndexTextPlanIdentity source)
    {
        // 字段顺序固定；整数为小端，字符串为 int32 UTF-8 字节长度加原字节，Guid 使用规范 N 形状。
        Text(writer, FormatKey); Text(writer, source.KnowledgeBaseId.ToString("N")); writer.Write(source.KnowledgeBaseVersion);
        Text(writer, source.TenantId?.ToString("N") ?? "host"); Text(writer, source.ModelConfigId.ToString("N")); writer.Write(source.ModelConfigVersion);
        Text(writer, source.ProviderKey); Text(writer, source.ModelId); Text(writer, source.ContentHash);
        Text(writer, "utf8-text-v1"); Text(writer, AiKnowledgeCl100kTokenizer.Version); Text(writer, "structure-window-v1");
        writer.Write((int)source.Format); var options = new AiKnowledgeChunkOptions();
        writer.Write(options.MaxTokens); writer.Write(options.OverlapTokens);
        writer.Write(options.MaxChunks); writer.Write(options.MaxTotalTokens);
    }

    /// <summary>从已存片段复算批次摘要；相同维度和片段数不能代替完整输入一致。</summary>
    internal static string ComputeBatchHash(AiKnowledgeIndexTextPlanIdentity identity, int ordinal, IReadOnlyList<AiKnowledgeStructuredChunk> chunks, CancellationToken token) => Digest(writer =>
    {
        WriteHeader(writer, identity); writer.Write(ordinal); writer.Write(chunks.Count);
        foreach (var chunk in chunks) { token.ThrowIfCancellationRequested(); WriteChunk(writer, chunk); }
    });
    internal static string ComputePlanHash(AiKnowledgeIndexTextPlanIdentity identity, AiKnowledgeIndexTextPlanSummary summary,
        IReadOnlyList<AiKnowledgeIndexTextManifestBatch> batches, CancellationToken token) => Digest(writer =>
    {
        WriteHeader(writer, identity); writer.Write(summary.BatchCount); writer.Write(summary.ChunkCount); writer.Write(summary.TokenCount);
        foreach (var batch in batches) { token.ThrowIfCancellationRequested(); Text(writer, batch.SourceHash); }
    });
    private static void WriteChunk(BinaryWriter writer, AiKnowledgeStructuredChunk chunk)
    {
        writer.Write(chunk.Sequence); writer.Write(chunk.Offset); writer.Write(chunk.Length); writer.Write(chunk.TokenCount); writer.Write((int)chunk.Kind);
        Text(writer, chunk.Text); Text(writer, chunk.EmbeddingText); Context(writer, chunk.Context); Context(writer, chunk.RepeatedContext);
    }
    private static void Context(BinaryWriter writer, IReadOnlyList<AiKnowledgeSourceContext> context)
    {
        writer.Write(context.Count);
        foreach (var item in context) { writer.Write((int)item.Kind); writer.Write(item.Offset); writer.Write(item.Length); writer.Write(item.Level); }
    }
    private static void Text(BinaryWriter writer, string text)
    { var bytes = Utf8.GetBytes(text); writer.Write(bytes.Length); writer.Write(bytes); }
    private static string Digest(Action<BinaryWriter> write)
    {
        // 流式哈希不累积整份文档编码；临时字节分配受单个输入上限约束。
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
