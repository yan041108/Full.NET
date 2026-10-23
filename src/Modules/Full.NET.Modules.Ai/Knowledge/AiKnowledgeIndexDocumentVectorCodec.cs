using System.Security.Cryptography;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>向量只绑定完整来源清单摘要，不重复存储原文、公式缓存或 OCR 元数据。</summary>
internal sealed record AiKnowledgeIndexDocumentVectorIdentity(Guid KnowledgeBaseId, int KnowledgeBaseVersion, Guid? TenantId,
    Guid ModelConfigId, int ModelConfigVersion, string ProviderKey, string ModelId, string ContentHash, int SizeBytes, string DocumentHash);
/// <summary>单批原始位置引用，必须与完整清单匹配后才可解释为可引用来源。</summary>
internal sealed record AiKnowledgeDocumentVectorSource(int Sequence, int BlockSequence, int Offset, int Length, string Text, int TokenCount);
/// <summary>尚未与完整清单核对的单个向量。</summary>
internal sealed record AiKnowledgeDocumentVectorEntry(AiKnowledgeDocumentVectorSource Source, ReadOnlyMemory<float> Vector);
/// <summary>与完整清单逐项相等的来源和向量，位置及块元数据来自该清单。</summary>
internal sealed record AiKnowledgeDocumentEmbeddedChunk(AiKnowledgeDocumentChunk Source, AiKnowledgeParsedBlock Block, ReadOnlyMemory<float> Vector);
/// <summary>单批格式合法不证明完整代次、来源授权或真实费用结算。</summary>
internal sealed record AiKnowledgeIndexDocumentVectorPayload(AiKnowledgeIndexDocumentVectorIdentity Identity, string PlanHash,
    int Ordinal, string SourceHash, int Dimensions, IReadOnlyList<AiKnowledgeDocumentVectorEntry> Chunks);
/// <summary>原始字节摘要及完整来源核对结果；后续仍需当前授权和活动代次复核。</summary>
internal sealed record AiKnowledgeIndexDocumentVectorSnapshot(AiKnowledgeIndexDocumentVectorPayload Payload, string PayloadHash,
    IReadOnlyList<AiKnowledgeDocumentEmbeddedChunk> Chunks);

/// <summary>统一结构来源的有界向量载荷；来源元数据只从已验证的完整清单恢复。</summary>
internal static class AiKnowledgeIndexDocumentVectorCodec
{
    internal const string FormatKey = "knowledge-document-vector-batch-v1";
    internal const int MaxPayloadBytes = 512 * 1024;
    private const int Magic = 0x3156474b;
    internal static byte[] Encode(AiKnowledgeDocumentPlan plan, int ordinal, IReadOnlyList<ReadOnlyMemory<float>> vectors, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(plan);
        if (ordinal < 0 || ordinal >= plan.Batches.Count) throw Invalid();
        var batch = plan.Batches[ordinal]; var count = vectors?.Count ?? 0; var identity = Identity(plan, token); var dimensions = Dimensions(identity.ModelId);
        if (count is < 1 or > 16 || count != batch.Chunks.Count) throw Invalid();
        var chunks = new AiKnowledgeDocumentVectorEntry[count];
        for (var index = 0; index < count; index++)
        { token.ThrowIfCancellationRequested(); var vector = vectors![index]; if (vector.Length != dimensions) throw Invalid(); chunks[index] = new(Project(batch.Chunks[index]), vector.ToArray()); }
        var payload = new AiKnowledgeIndexDocumentVectorPayload(identity, plan.PlanHash, ordinal, batch.SourceHash, dimensions, Array.AsReadOnly(chunks));
        Validate(payload, token);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, new System.Text.UTF8Encoding(false, true), true);
        writer.Write(Magic); AiKnowledgeIndexDocumentManifestCodec.Text(writer, FormatKey); WriteIdentity(writer, identity);
        AiKnowledgeIndexDocumentManifestCodec.Text(writer, payload.PlanHash); writer.Write(ordinal); AiKnowledgeIndexDocumentManifestCodec.Text(writer, payload.SourceHash);
        writer.Write(dimensions); writer.Write(count);
        foreach (var item in payload.Chunks)
        {
            token.ThrowIfCancellationRequested(); var c = item.Source;
            writer.Write(c.Sequence); writer.Write(c.BlockSequence); writer.Write(c.Offset); writer.Write(c.Length); writer.Write(c.TokenCount); AiKnowledgeIndexDocumentManifestCodec.Text(writer, c.Text);
            foreach (var value in item.Vector.Span) writer.Write(value);
        }
        writer.Flush(); token.ThrowIfCancellationRequested(); if (stream.Length > MaxPayloadBytes) throw Invalid(); return stream.ToArray();
    }

    internal static AiKnowledgeIndexDocumentVectorPayload Decode(byte[] bytes, CancellationToken token)
    { var captured = Capture(bytes, token); try { return DecodeFrozen(captured, token); } finally { CryptographicOperations.ZeroMemory(captured); } }

    internal static AiKnowledgeIndexDocumentVectorSnapshot ReadBatch(byte[] manifestBytes, int ordinal, byte[] bytes, CancellationToken token)
        => ReadBatch(AiKnowledgeIndexDocumentVerifiedManifest.Capture(manifestBytes, token), ordinal, bytes, token);

    /// <summary>完整计划决定全部期望来源；不得使用载荷自己声明的位置生成引用。</summary>
    internal static AiKnowledgeIndexDocumentVectorSnapshot ReadBatch(AiKnowledgeIndexDocumentVerifiedManifest manifest, int ordinal, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(manifest); var plan = manifest.Plan;
        if (ordinal < 0 || ordinal >= plan.Batches.Count) throw Invalid();
        var captured = Capture(bytes, token);
        try
        {
            var payload = DecodeFrozen(captured, token); var batch = plan.Batches[ordinal];
            if (payload.Identity != manifest.VectorIdentity || payload.PlanHash != plan.PlanHash || payload.Ordinal != ordinal
                || payload.SourceHash != batch.SourceHash || payload.Chunks.Count != batch.Chunks.Count) throw Invalid();
            var verified = new AiKnowledgeDocumentEmbeddedChunk[batch.Chunks.Count];
            for (var index = 0; index < verified.Length; index++)
            {
                token.ThrowIfCancellationRequested(); var item = payload.Chunks[index]; var expected = batch.Chunks[index];
                if (item.Source != Project(expected)) throw Invalid();
                verified[index] = new(expected, plan.Source.Document.Blocks[expected.BlockSequence], item.Vector);
            }
            var hash = Convert.ToHexString(SHA256.HashData(captured)); token.ThrowIfCancellationRequested();
            return new(payload, hash, Array.AsReadOnly(verified));
        }
        finally { CryptographicOperations.ZeroMemory(captured); }
    }

    internal static AiKnowledgeIndexDocumentVectorIdentity Identity(AiKnowledgeDocumentPlan plan, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var s = plan.Source;
        return new(s.KnowledgeBaseId, s.KnowledgeBaseVersion, s.TenantId, s.ModelConfigId, s.ModelConfigVersion, s.ProviderKey, s.ModelId,
            s.Document.ContentHash, s.Document.SizeBytes, plan.DocumentHash);
    }
    private static AiKnowledgeDocumentVectorSource Project(AiKnowledgeDocumentChunk c) => new(c.Sequence, c.BlockSequence, c.Offset, c.Length, c.Text, c.TokenCount);
    private static void WriteIdentity(BinaryWriter writer, AiKnowledgeIndexDocumentVectorIdentity i)
    {
        AiKnowledgeIndexDocumentManifestCodec.GuidValue(writer, i.KnowledgeBaseId); writer.Write(i.KnowledgeBaseVersion); writer.Write(i.TenantId.HasValue);
        if (i.TenantId is { } tenant) AiKnowledgeIndexDocumentManifestCodec.GuidValue(writer, tenant);
        AiKnowledgeIndexDocumentManifestCodec.GuidValue(writer, i.ModelConfigId); writer.Write(i.ModelConfigVersion);
        AiKnowledgeIndexDocumentManifestCodec.Text(writer, i.ProviderKey); AiKnowledgeIndexDocumentManifestCodec.Text(writer, i.ModelId);
        AiKnowledgeIndexDocumentManifestCodec.Text(writer, i.ContentHash); AiKnowledgeIndexDocumentManifestCodec.Text(writer, i.DocumentHash); writer.Write(i.SizeBytes);
    }
    private static AiKnowledgeIndexDocumentVectorPayload DecodeFrozen(byte[] bytes, CancellationToken token)
    {
        try
        {
            using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream, new System.Text.UTF8Encoding(false, true));
            if (reader.ReadInt32() != Magic || AiKnowledgeIndexDocumentManifestCodec.Text(reader) != FormatKey) throw Invalid();
            var knowledge = AiKnowledgeIndexDocumentManifestCodec.GuidValue(reader); var knowledgeVersion = reader.ReadInt32();
            Guid? tenant = AiKnowledgeIndexDocumentManifestCodec.Boolean(reader) ? AiKnowledgeIndexDocumentManifestCodec.GuidValue(reader) : null;
            var model = AiKnowledgeIndexDocumentManifestCodec.GuidValue(reader); var modelVersion = reader.ReadInt32();
            var provider = AiKnowledgeIndexDocumentManifestCodec.Text(reader); var modelId = AiKnowledgeIndexDocumentManifestCodec.Text(reader);
            var sourceHash = AiKnowledgeIndexDocumentManifestCodec.Text(reader); var documentHash = AiKnowledgeIndexDocumentManifestCodec.Text(reader); var size = reader.ReadInt32();
            var identity = new AiKnowledgeIndexDocumentVectorIdentity(knowledge, knowledgeVersion, tenant, model, modelVersion, provider, modelId, sourceHash, size, documentHash);
            var planHash = AiKnowledgeIndexDocumentManifestCodec.Text(reader); var ordinal = reader.ReadInt32(); var batchHash = AiKnowledgeIndexDocumentManifestCodec.Text(reader);
            var dimensions = reader.ReadInt32(); var count = reader.ReadInt32();
            if (dimensions == 0 || dimensions != Dimensions(modelId) || count is < 1 or > 16) throw Invalid();
            var chunks = new AiKnowledgeDocumentVectorEntry[count];
            for (var index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested(); var sequence = reader.ReadInt32(); var block = reader.ReadInt32(); var offset = reader.ReadInt32(); var length = reader.ReadInt32();
                var tokens = reader.ReadInt32(); var text = AiKnowledgeIndexDocumentManifestCodec.Text(reader, 8000);
                if (stream.Length - stream.Position < dimensions * sizeof(float)) throw Invalid();
                var vector = new float[dimensions]; for (var n = 0; n < dimensions; n++) vector[n] = reader.ReadSingle();
                chunks[index] = new(new(sequence, block, offset, length, text, tokens), vector);
            }
            if (stream.Position != stream.Length) throw Invalid();
            var payload = new AiKnowledgeIndexDocumentVectorPayload(identity, planHash, ordinal, batchHash, dimensions, Array.AsReadOnly(chunks)); Validate(payload, token); return payload;
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("Invalid document vector payload.", error); }
        catch (System.Text.DecoderFallbackException error) { throw new InvalidDataException("Invalid document vector payload.", error); }
    }
    private static void Validate(AiKnowledgeIndexDocumentVectorPayload payload, CancellationToken token)
    {
        var i = payload.Identity;
        if (i.KnowledgeBaseId == Guid.Empty || i.ModelConfigId == Guid.Empty || i.TenantId == Guid.Empty || i.KnowledgeBaseVersion <= 0 || i.ModelConfigVersion <= 0
            || i.ProviderKey != "openai_compatible" || !Hash(i.ContentHash) || !Hash(i.DocumentHash)
            || !AiKnowledgeParsedDocument.ValidIdentity(i.ContentHash, i.SizeBytes, Domain.AiKnowledgeFilePolicy.MaxBytes)
            || !Hash(payload.PlanHash) || !Hash(payload.SourceHash) || payload.Ordinal is < 0 or >= 2048
            || payload.Dimensions == 0 || payload.Dimensions != Dimensions(i.ModelId)) throw Invalid();
        var previous = -1; var previousBlock = -1; var previousOffset = -1; var previousEnd = -1;
        var tokenizer = AiKnowledgeCl100kTokenizer.Get(token);
        foreach (var item in payload.Chunks)
        {
            token.ThrowIfCancellationRequested(); var c = item.Source;
            if (c is null || c.Sequence is < 0 or >= 2048 || previous >= 0 && c.Sequence != previous + 1 || c.BlockSequence is < 0 or >= 8192
                || c.BlockSequence < previousBlock || c.Offset < 0 || c.Length <= 0 || c.Text is null || c.Length != c.Text.Length
                || (long)c.Offset + c.Length > AiKnowledgeTextParser.MaxOutputCharacters || !AiKnowledgeParsedDocument.ValidText(c.Text, token)
                || c.BlockSequence == previousBlock && (c.Offset <= previousOffset || c.Offset + c.Length <= previousEnd)
                || c.TokenCount is < 1 or > 512 || tokenizer.CountTokens(c.Text) != c.TokenCount) throw Invalid();
            double norm = 0; foreach (var value in item.Vector.Span) { if (!float.IsFinite(value)) throw Invalid(); norm += (double)value * value; }
            if (norm <= 0) throw Invalid(); previous = c.Sequence; previousBlock = c.BlockSequence; previousOffset = c.Offset; previousEnd = c.Offset + c.Length;
        }
        if (AiEmbeddingContentPolicy.ValidateInputs(payload.Chunks.Select(c => c.Source.Text).ToArray()) is not null) throw Invalid();
    }
    private static byte[] Capture(byte[] bytes, CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (bytes is null || bytes.Length is < 8 or > MaxPayloadBytes) throw Invalid(); return bytes.ToArray(); }
    private static int Dimensions(string model) => model switch { "text-embedding-3-small" => 1536, "text-embedding-3-large" => 3072, _ => 0 };
    private static bool Hash(string value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit) && value == value.ToLowerInvariant();
    private static InvalidDataException Invalid() => new("Invalid document vector payload.");
}