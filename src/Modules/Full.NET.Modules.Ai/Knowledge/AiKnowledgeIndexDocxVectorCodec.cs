using System.Security.Cryptography;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>完整模型与原始文档绑定，不含权威授权或费用证明。</summary>
internal sealed record AiKnowledgeIndexDocxVectorIdentity(Guid KnowledgeBaseId, int KnowledgeBaseVersion, Guid? TenantId,
    Guid ModelConfigId, int ModelConfigVersion, string ProviderKey, string ModelId, string ContentHash, int SizeBytes, string DocumentHash);
/// <summary>原逻辑块和全局区间随向量保留，不能替换为页码。</summary>
internal sealed record AiKnowledgeDocxEmbeddedChunk(AiKnowledgeDocxChunk Source, AiKnowledgeDocxBlock Block, ReadOnlyMemory<float> Vector);
/// <summary>指定计划的单批载荷，合法数值仍不证明真实模型执行。</summary>
internal sealed record AiKnowledgeIndexDocxVectorPayload(AiKnowledgeIndexDocxVectorIdentity Identity, string PlanHash,
    int Ordinal, string SourceHash, int Dimensions, IReadOnlyList<AiKnowledgeDocxEmbeddedChunk> Chunks);
/// <summary>实际捕获原字节的摘要，不以重编码代替载荷证明。</summary>
internal sealed record AiKnowledgeIndexDocxVectorSnapshot(AiKnowledgeIndexDocxVectorPayload Payload, string PayloadHash);

/// <summary>DOCX 指定计划批次的有界二进制载荷。</summary>
internal static class AiKnowledgeIndexDocxVectorCodec
{
    internal const string FormatKey = "knowledge-docx-vector-batch-v1";
    internal const int MaxPayloadBytes = 512 * 1024;
    private const int Magic = 0x3156444b;
    /// <summary>先限制数量和维度，再冻结向量；只编码完整计划中的指定批次。</summary>
    internal static byte[] Encode(AiKnowledgeIndexDocxPlan plan, int ordinal, IReadOnlyList<ReadOnlyMemory<float>> vectors, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(plan);
        if (ordinal < 0 || ordinal >= plan.Batches.Count) throw Invalid();
        var batch = plan.Batches[ordinal]; var count = vectors?.Count ?? 0; var identity = Identity(plan, token); var dimensions = Dimensions(identity.ModelId);
        if (count is < 1 or > 16 || count != batch.Chunks.Count) throw Invalid();
        var chunks = new AiKnowledgeDocxEmbeddedChunk[count];
        for (var index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested(); var vector = vectors![index]; if (vector.Length != dimensions) throw Invalid();
            var source = batch.Chunks[index]; chunks[index] = new(source, plan.Source.Document.Blocks[source.BlockSequence], vector.ToArray());
        }
        var payload = new AiKnowledgeIndexDocxVectorPayload(identity, plan.Summary.PlanHash, ordinal, batch.SourceHash, dimensions, Array.AsReadOnly(chunks));
        Validate(payload, token);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, new System.Text.UTF8Encoding(false, true), true);
        writer.Write(Magic); AiKnowledgeIndexDocxManifestCodec.Text(writer, FormatKey); WriteIdentity(writer, identity);
        AiKnowledgeIndexDocxManifestCodec.Text(writer, payload.PlanHash); writer.Write(ordinal); AiKnowledgeIndexDocxManifestCodec.Text(writer, payload.SourceHash);
        writer.Write(dimensions); writer.Write(count);
        foreach (var item in payload.Chunks)
        {
            token.ThrowIfCancellationRequested(); var c = item.Source; var b = item.Block;
            writer.Write(c.Sequence); writer.Write(c.BlockSequence); writer.Write(c.Offset); writer.Write(c.Length); writer.Write(c.TokenCount); AiKnowledgeIndexDocxManifestCodec.Text(writer, c.Text);
            writer.Write(b.Sequence); AiKnowledgeIndexDocxManifestCodec.Text(writer, b.Kind); writer.Write(b.Offset); writer.Write(b.Length); writer.Write(b.ParagraphNumber);
            writer.Write(b.TableNumber ?? 0); writer.Write(b.RowNumber ?? 0); writer.Write(b.CellNumber ?? 0); writer.Write(b.CellParagraphCount); writer.Write(b.IsHeader);
            // IEEE float 原位保存，包括负零和有限次正规数，不裁剪或归一化。
            foreach (var value in item.Vector.Span) writer.Write(value);
        }
        writer.Flush(); token.ThrowIfCancellationRequested(); if (stream.Length > MaxPayloadBytes) throw Invalid(); return stream.ToArray();
    }

    /// <summary>单批格式验收不替代完整清单、权威授权和费用回执。</summary>
    internal static AiKnowledgeIndexDocxVectorPayload Decode(byte[] bytes, CancellationToken token)
    { var captured = Capture(bytes, token); try { return DecodeFrozen(captured, token); } finally { CryptographicOperations.ZeroMemory(captured); } }

    /// <summary>原始批次须逐块等于完整计划中的原文、Token 和逻辑坐标。</summary>
    internal static AiKnowledgeIndexDocxVectorSnapshot ReadBatch(byte[] manifestBytes, int ordinal, byte[] bytes, CancellationToken token)
    {
        return ReadBatch(AiKnowledgeIndexDocxVerifiedManifest.Capture(manifestBytes, token), ordinal, bytes, token);
    }

    /// <summary>共享已经验证的完整计划；仍逐批校验实际向量字节及全部逻辑坐标。</summary>
    internal static AiKnowledgeIndexDocxVectorSnapshot ReadBatch(AiKnowledgeIndexDocxVerifiedManifest manifest, int ordinal, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(manifest); var plan = manifest.Plan;
        if (ordinal < 0 || ordinal >= plan.Batches.Count) throw Invalid();
        var captured = Capture(bytes, token);
        try
        {
            var payload = DecodeFrozen(captured, token); var batch = plan.Batches[ordinal];
            if (payload.Identity != manifest.VectorIdentity || payload.PlanHash != plan.Summary.PlanHash || payload.Ordinal != ordinal
                || payload.SourceHash != batch.SourceHash || payload.Chunks.Count != batch.Chunks.Count) throw Invalid();
            for (var index = 0; index < payload.Chunks.Count; index++)
            {
                token.ThrowIfCancellationRequested(); var item = payload.Chunks[index]; var expected = batch.Chunks[index];
                if (item.Source != expected || item.Block != plan.Source.Document.Blocks[expected.BlockSequence]) throw Invalid();
            }
            var hash = Convert.ToHexString(SHA256.HashData(captured)); token.ThrowIfCancellationRequested(); return new(payload, hash);
        }
        finally { CryptographicOperations.ZeroMemory(captured); }
    }

    internal static AiKnowledgeIndexDocxVectorIdentity Identity(AiKnowledgeIndexDocxPlan plan, CancellationToken token)
    { var s = plan.Source; return new(s.KnowledgeBaseId, s.KnowledgeBaseVersion, s.TenantId, s.ModelConfigId, s.ModelConfigVersion, s.ProviderKey, s.ModelId, s.Document.ContentHash, s.Document.SizeBytes, AiKnowledgeDocxPlanHash.Document(s.Document, token)); }
    private static void WriteIdentity(BinaryWriter writer, AiKnowledgeIndexDocxVectorIdentity identity)
    {
        AiKnowledgeIndexDocxManifestCodec.GuidValue(writer, identity.KnowledgeBaseId); writer.Write(identity.KnowledgeBaseVersion); writer.Write(identity.TenantId.HasValue);
        if (identity.TenantId is { } tenant) AiKnowledgeIndexDocxManifestCodec.GuidValue(writer, tenant);
        AiKnowledgeIndexDocxManifestCodec.GuidValue(writer, identity.ModelConfigId); writer.Write(identity.ModelConfigVersion);
        foreach (var text in new[] { identity.ProviderKey, identity.ModelId, identity.ContentHash, identity.DocumentHash }) AiKnowledgeIndexDocxManifestCodec.Text(writer, text);
        writer.Write(identity.SizeBytes);
    }
    private static AiKnowledgeIndexDocxVectorPayload DecodeFrozen(byte[] bytes, CancellationToken token)
    {
        try
        {
            using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream, new System.Text.UTF8Encoding(false, true));
            if (reader.ReadInt32() != Magic || AiKnowledgeIndexDocxManifestCodec.Text(reader) != FormatKey) throw Invalid();
            var knowledge = AiKnowledgeIndexDocxManifestCodec.GuidValue(reader); var knowledgeVersion = reader.ReadInt32();
            Guid? tenant = AiKnowledgeIndexDocxManifestCodec.Boolean(reader) ? AiKnowledgeIndexDocxManifestCodec.GuidValue(reader) : null;
            var model = AiKnowledgeIndexDocxManifestCodec.GuidValue(reader); var modelVersion = reader.ReadInt32();
            var provider = AiKnowledgeIndexDocxManifestCodec.Text(reader); var modelId = AiKnowledgeIndexDocxManifestCodec.Text(reader); var sourceHash = AiKnowledgeIndexDocxManifestCodec.Text(reader); var documentHash = AiKnowledgeIndexDocxManifestCodec.Text(reader); var size = reader.ReadInt32();
            var identity = new AiKnowledgeIndexDocxVectorIdentity(knowledge, knowledgeVersion, tenant, model, modelVersion, provider, modelId, sourceHash, size, documentHash);
            var planHash = AiKnowledgeIndexDocxManifestCodec.Text(reader); var ordinal = reader.ReadInt32(); var batchHash = AiKnowledgeIndexDocxManifestCodec.Text(reader);
            var dimensions = reader.ReadInt32(); var count = reader.ReadInt32();
            if (dimensions == 0 || dimensions != Dimensions(modelId) || count is < 1 or > 16) throw Invalid();
            var chunks = new AiKnowledgeDocxEmbeddedChunk[count];
            for (var index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested(); var sequence = reader.ReadInt32(); var blockSequence = reader.ReadInt32(); var offset = reader.ReadInt32(); var length = reader.ReadInt32(); var tokens = reader.ReadInt32(); var text = AiKnowledgeIndexDocxManifestCodec.Text(reader, 8000);
                var block = reader.ReadInt32(); var kind = AiKnowledgeIndexDocxManifestCodec.Text(reader, 16); var blockOffset = reader.ReadInt32(); var blockLength = reader.ReadInt32(); var paragraph = reader.ReadInt32();
                var table = reader.ReadInt32(); var row = reader.ReadInt32(); var cell = reader.ReadInt32(); var paragraphs = reader.ReadInt32(); var header = AiKnowledgeIndexDocxManifestCodec.Boolean(reader);
                if (stream.Length - stream.Position < dimensions * sizeof(float)) throw Invalid();
                var vector = new float[dimensions]; for (var n = 0; n < dimensions; n++) vector[n] = reader.ReadSingle();
                chunks[index] = new(new(sequence, blockSequence, offset, length, text, tokens), new(block, kind, blockOffset, blockLength, paragraph, table == 0 ? null : table, row == 0 ? null : row, cell == 0 ? null : cell, paragraphs, header), vector);
            }
            if (stream.Position != stream.Length) throw Invalid();
            var payload = new AiKnowledgeIndexDocxVectorPayload(identity, planHash, ordinal, batchHash, dimensions, Array.AsReadOnly(chunks)); Validate(payload, token); return payload;
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("Invalid DOCX vector payload.", error); }
        catch (System.Text.DecoderFallbackException error) { throw new InvalidDataException("Invalid DOCX vector payload.", error); }
    }

    private static void Validate(AiKnowledgeIndexDocxVectorPayload payload, CancellationToken token)
    {
        var i = payload.Identity;
        if (i.KnowledgeBaseVersion <= 0 || i.ModelConfigVersion <= 0 || i.ProviderKey != "openai_compatible" || !Hash(i.ContentHash) || !Hash(i.DocumentHash)
            || !AiKnowledgeDocxOutputReader.ValidSource(i.ContentHash, i.SizeBytes) || !Hash(payload.PlanHash) || !Hash(payload.SourceHash) || payload.Ordinal is < 0 or >= 2048
            || payload.Dimensions == 0 || payload.Dimensions != Dimensions(i.ModelId) || AiEmbeddingContentPolicy.ValidateInputs(payload.Chunks.Select(c => c.Source.Text).ToArray()) is not null) throw Invalid();
        var previous = -1; var previousBlock = -1; var previousOffset = -1; var previousEnd = -1;
        var tokenizer = AiKnowledgeCl100kTokenizer.Get(token);
        foreach (var item in payload.Chunks)
        {
            token.ThrowIfCancellationRequested(); var c = item.Source; var b = item.Block;
            if (c.Sequence is < 0 or >= 2048 || previous >= 0 && c.Sequence != previous + 1 || c.BlockSequence != b.Sequence || b.Sequence is < 0 or >= 8192
                || b.Sequence < previousBlock || b.Offset < 0 || b.Length < 0 || (long)b.Offset + b.Length > AiKnowledgeTextParser.MaxOutputCharacters || b.ParagraphNumber is < 1 or > 8192
                || c.Offset < b.Offset || c.Length <= 0 || c.Length != c.Text.Length || (long)c.Offset + c.Length > (long)b.Offset + b.Length
                || c.Offset <= previousOffset || c.Offset + c.Length <= previousEnd || c.TokenCount is < 1 or > 512 || tokenizer.CountTokens(c.Text) != c.TokenCount || c.Text.Contains('\0')) throw Invalid();
            if (b.Kind == "paragraph" ? b.TableNumber is not null || b.RowNumber is not null || b.CellNumber is not null || b.CellParagraphCount != 0 || b.IsHeader
                : b.Kind != "tableCell" || b.TableNumber is not (>= 1 and <= 128) || b.RowNumber is not (>= 1 and <= 4096) || b.CellNumber is not (>= 1 and <= 64) || b.CellParagraphCount is < 1 or > 8192) throw Invalid();
            double norm = 0; foreach (var value in item.Vector.Span) { if (!float.IsFinite(value)) throw Invalid(); norm += (double)value * value; }
            if (norm <= 0) throw Invalid(); previous = c.Sequence; previousBlock = b.Sequence; previousOffset = c.Offset; previousEnd = c.Offset + c.Length;
        }
    }
    private static byte[] Capture(byte[] bytes, CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (bytes is null || bytes.Length is < 8 or > MaxPayloadBytes) throw Invalid(); return bytes.ToArray(); }
    private static int Dimensions(string model) => model switch { "text-embedding-3-small" => 1536, "text-embedding-3-large" => 3072, _ => 0 };
    private static bool Hash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit) && value == value.ToLowerInvariant();
    private static InvalidDataException Invalid() => new("Invalid DOCX vector payload.");
}
