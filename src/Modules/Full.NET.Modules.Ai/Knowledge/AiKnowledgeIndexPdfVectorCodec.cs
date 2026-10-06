using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>向量绑定原始分页块，保留页内定位，不引入文本结构上下文。</summary>
internal sealed record AiKnowledgePdfEmbeddedChunk(AiKnowledgePdfChunk Source, ReadOnlyMemory<float> Vector);
/// <summary>完整单批次载荷；合法数值和摘要不证明模型调用或派发授权。</summary>
internal sealed record AiKnowledgeIndexPdfVectorPayload(AiKnowledgeIndexPdfPlanIdentity Identity, string PlanHash,
    int Ordinal, string SourceHash, int Dimensions, IReadOnlyList<AiKnowledgePdfEmbeddedChunk> Chunks);
/// <summary>清单绑定的捕获结果；摘要来自实际原字节，持久化仍需权威作用域与代次复核。</summary>
internal sealed record AiKnowledgeIndexPdfVectorSnapshot(AiKnowledgeIndexPdfVectorPayload Payload, string PayloadHash);

/// <summary>PDF 专用有界二进制载荷，不改动既有文本 v1 或授予持久化资格。</summary>
internal static class AiKnowledgeIndexPdfVectorCodec
{
    internal const string FormatKey = "knowledge-pdf-vector-batch-v1";
    internal const int MaxPayloadBytes = 512 * 1024;
    private const int Magic = 0x3156504b;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    /// <summary>只能编码完整计划的指定批次；先限界和复制向量，再验证全批次后输出。</summary>
    internal static byte[] Encode(AiKnowledgeIndexPdfPlan plan, int ordinal, IReadOnlyList<ReadOnlyMemory<float>> vectors, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(plan);
        if (ordinal < 0 || ordinal >= plan.Batches.Count) throw Invalid();
        var batch = plan.Batches[ordinal]; var count = vectors?.Count ?? 0; var dimensions = Dimensions(plan.Identity);
        if (count is < 1 or > 16 || count != batch.Chunks.Count || dimensions == 0) throw Invalid();
        var chunks = new AiKnowledgePdfEmbeddedChunk[count];
        for (var index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested(); var vector = vectors![index];
            // 每条维度在复制前限制，不能先物化调用方的任意长内存。
            if (vector.Length != dimensions) throw Invalid();
            chunks[index] = new(batch.Chunks[index], vector.ToArray());
        }
        var payload = new AiKnowledgeIndexPdfVectorPayload(plan.Identity, plan.Summary.PlanHash, ordinal, batch.SourceHash,
            dimensions, Array.AsReadOnly(chunks));
        try
        {
            Validate(payload, token);
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Utf8, true);
            writer.Write(Magic); AiKnowledgeIndexPdfPlan.Text(writer, FormatKey); AiKnowledgeIndexPdfPlan.WriteHeader(writer, payload.Identity);
            AiKnowledgeIndexPdfPlan.Text(writer, payload.PlanHash); writer.Write(payload.Ordinal); AiKnowledgeIndexPdfPlan.Text(writer, payload.SourceHash);
            writer.Write(dimensions); writer.Write(count);
            foreach (var item in payload.Chunks)
            {
                token.ThrowIfCancellationRequested(); var source = item.Source;
                writer.Write(source.Sequence); writer.Write(source.PageNumber); writer.Write(source.Offset); writer.Write(source.Length); writer.Write(source.TokenCount);
                AiKnowledgeIndexPdfPlan.Text(writer, source.Text); AiKnowledgeIndexPdfPlan.Text(writer, source.EmbeddingText);
                // IEEE float 原位写入，保留负零和有限次正规数，不归一化或裁剪向量。
                foreach (var value in item.Vector.Span) writer.Write(value);
            }
            writer.Flush(); token.ThrowIfCancellationRequested(); if (stream.Length > MaxPayloadBytes) throw Invalid();
            return stream.ToArray();
        }
        catch (EncoderFallbackException exception) { throw new InvalidDataException("Invalid PDF vector payload.", exception); }
    }

    /// <summary>只验证单批次格式和来源摘要；完整计划绑定必须继续通过 ReadBatch。</summary>
    internal static AiKnowledgeIndexPdfVectorPayload Decode(byte[] bytes, CancellationToken token)
        => DecodeFrozen(Capture(bytes, token), token);

    /// <summary>消费完整清单与原始载荷；数量或维度相同不能代替同一计划、批次和页位置。</summary>
    /// <remarks>不能据此改变 ready；数据库消费者仍须核对任务/操作、来源版本、可信租户和精确模型批准。</remarks>
    internal static AiKnowledgeIndexPdfVectorSnapshot ReadBatch(byte[] manifestBytes, int ordinal, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var manifest = AiKnowledgeIndexPdfManifestCodec.Decode(manifestBytes, token);
        if (ordinal < 0 || ordinal >= manifest.Batches.Count) throw Invalid();
        var captured = Capture(bytes, token); var payload = DecodeFrozen(captured, token); var expected = manifest.Batches[ordinal];
        if (payload.Identity != manifest.Identity || payload.PlanHash != manifest.Summary.PlanHash || payload.Ordinal != ordinal
            || payload.SourceHash != expected.SourceHash || payload.Chunks.Count != expected.ChunkCount
            || payload.Chunks[0].Source.Sequence != expected.FirstSequence
            || payload.Chunks.Sum(item => (long)item.Source.TokenCount) != expected.TokenCount
            || payload.Chunks[0].Source.PageNumber != expected.FirstPageNumber || payload.Chunks[^1].Source.PageNumber != expected.LastPageNumber) throw Invalid();
        // 原字节在解码与哈希之间不再受调用方数组修改；不以重新编码结果代替实际载荷。
        var hash = Convert.ToHexString(SHA256.HashData(captured)); token.ThrowIfCancellationRequested(); return new(payload, hash);
    }

    private static byte[] Capture(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (bytes is null || bytes.Length is < 8 or > MaxPayloadBytes) throw Invalid();
        return bytes.ToArray();
    }

    private static AiKnowledgeIndexPdfVectorPayload DecodeFrozen(byte[] bytes, CancellationToken token)
    {
        try
        {
            using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream, Utf8);
            if (reader.ReadInt32() != Magic || Text(reader) != FormatKey) throw Invalid();
            var identity = Header(reader); var planHash = Text(reader); var ordinal = reader.ReadInt32(); var sourceHash = Text(reader);
            var dimensions = reader.ReadInt32(); var count = reader.ReadInt32();
            if (!Hash(planHash) || !Hash(sourceHash) || ordinal is < 0 or >= 2048 || dimensions != Dimensions(identity) || count is < 1 or > 16) throw Invalid();
            var chunks = new AiKnowledgePdfEmbeddedChunk[count];
            for (var index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                var sequence = reader.ReadInt32(); var page = reader.ReadInt32(); var offset = reader.ReadInt32(); var length = reader.ReadInt32(); var tokens = reader.ReadInt32();
                var text = Text(reader, 32000, 8000); var embedding = Text(reader, 32000, 8000);
                // 模型维度已是固定白名单，剩余字节不足时不分配向量。
                if (stream.Length - stream.Position < dimensions * sizeof(float)) throw Invalid();
                var vector = new float[dimensions]; for (var n = 0; n < dimensions; n++) vector[n] = reader.ReadSingle();
                chunks[index] = new(new(sequence, page, offset, length, text, embedding, tokens), vector);
            }
            if (stream.Position != stream.Length) throw Invalid();
            var payload = new AiKnowledgeIndexPdfVectorPayload(identity, planHash, ordinal, sourceHash, dimensions, Array.AsReadOnly(chunks));
            Validate(payload, token); token.ThrowIfCancellationRequested(); return payload;
        }
        catch (EndOfStreamException exception) { throw new InvalidDataException("Invalid PDF vector payload.", exception); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Invalid PDF vector payload.", exception); }
    }

    private static AiKnowledgeIndexPdfPlanIdentity Header(BinaryReader reader)
    {
        if (Text(reader) != AiKnowledgeIndexPdfPlan.FormatKey) throw Invalid();
        var knowledge = GuidText(Text(reader)); var knowledgeVersion = reader.ReadInt32(); var tenantText = Text(reader);
        Guid? tenant = tenantText == "host" ? null : GuidText(tenantText);
        var model = GuidText(Text(reader)); var modelVersion = reader.ReadInt32(); var provider = Text(reader); var modelId = Text(reader); var contentHash = Text(reader);
        if (knowledgeVersion <= 0 || modelVersion <= 0 || provider != "openai_compatible"
            || modelId is not ("text-embedding-3-small" or "text-embedding-3-large") || !AiKnowledgeIndexPdfManifestCodec.Hash(contentHash)
            || contentHash != contentHash.ToLowerInvariant() || Text(reader) != AiKnowledgeParsedPdf.ParserVersion
            || Text(reader) != AiKnowledgeCl100kTokenizer.Version || Text(reader) != "pdf-page-window-v1") throw Invalid();
        var pages = reader.ReadInt32(); var options = new AiKnowledgeChunkOptions();
        if (pages is < 1 or > 128 || reader.ReadInt32() != options.MaxTokens || reader.ReadInt32() != options.OverlapTokens
            || reader.ReadInt32() != options.MaxChunks || reader.ReadInt32() != options.MaxTotalTokens) throw Invalid();
        return new(knowledge, knowledgeVersion, tenant, model, modelVersion, provider, modelId, contentHash, pages);
    }

    private static void Validate(AiKnowledgeIndexPdfVectorPayload payload, CancellationToken token)
    {
        if (payload.Ordinal is < 0 or >= 2048 || payload.Dimensions != Dimensions(payload.Identity) || payload.Chunks.Count is < 1 or > 16
            || !Hash(payload.PlanHash) || !Hash(payload.SourceHash)
            || AiEmbeddingContentPolicy.ValidateInputs(payload.Chunks.Select(item => item.Source.EmbeddingText).ToArray()) is not null) throw Invalid();
        var previous = -1; var page = -1; var offset = -1; var end = 0; long coveredCharacters = 0;
        foreach (var item in payload.Chunks)
        {
            token.ThrowIfCancellationRequested(); var source = item.Source;
            if (source.Sequence is < 0 or >= 2048 || (previous >= 0 && source.Sequence != previous + 1)
                || source.PageNumber < 1 || source.PageNumber > payload.Identity.PageCount
                || source.Offset < 0 || source.Length <= 0 || source.Length != source.Text.Length
                || (long)source.Offset + source.Length > AiKnowledgeTextParser.MaxOutputCharacters
                || source.TokenCount is < 1 or > 512 || source.EmbeddingText != source.Text || source.Text.Contains('\0')
                || item.Vector.Length != payload.Dimensions) throw Invalid();
            if (page != source.PageNumber)
            {
                // 批次可从后续页开始，但批次内仍不允许跳页或回退。
                if (page >= 0 && source.PageNumber != page + 1) throw Invalid();
                coveredCharacters += end; page = source.PageNumber; offset = -1; end = 0;
            }
            var nextEnd = source.Offset + source.Length;
            if (source.Offset <= offset || nextEnd <= end || coveredCharacters + nextEnd > AiKnowledgeTextParser.MaxOutputCharacters) throw Invalid();
            _ = Utf8.GetByteCount(source.Text); double norm = 0;
            foreach (var value in item.Vector.Span)
            { if (!float.IsFinite(value)) throw Invalid(); norm += (double)value * value; }
            if (norm <= 0) throw Invalid(); previous = source.Sequence; offset = source.Offset; end = nextEnd;
        }
        var sources = payload.Chunks.Select(item => item.Source).ToArray();
        if (AiKnowledgeIndexPdfPlan.ComputeBatchHash(payload.Identity, payload.Ordinal, sources, token) != payload.SourceHash) throw Invalid();
    }

    private static int Dimensions(AiKnowledgeIndexPdfPlanIdentity identity) => identity.ModelId switch
    { "text-embedding-3-small" => 1536, "text-embedding-3-large" => 3072, _ => 0 };
    private static Guid GuidText(string value) => Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty && id.ToString("N") == value ? id : throw Invalid();
    private static bool Hash(string value) => AiKnowledgeIndexPdfManifestCodec.Hash(value) && value == value.ToUpperInvariant();
    private static string Text(BinaryReader reader, int maxBytes = 256, int maxCharacters = 256)
    {
        var length = reader.ReadInt32(); if (length < 1 || length > maxBytes || length > reader.BaseStream.Length - reader.BaseStream.Position) throw Invalid();
        var text = Utf8.GetString(reader.ReadBytes(length)); if (text.Length > maxCharacters) throw Invalid(); return text;
    }
    private static InvalidDataException Invalid() => new("Invalid PDF vector payload.");
}
