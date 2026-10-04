using System.Text;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>持久清单的来源身份；正文和会话秘密不进入清单。</summary>
internal sealed record AiKnowledgeIndexTextPlanIdentity(Guid KnowledgeBaseId, int KnowledgeBaseVersion, Guid? TenantId,
    Guid ModelConfigId, int ModelConfigVersion, string ProviderKey, string ModelId, string ContentHash, AiKnowledgeTextFormat Format);
internal sealed record AiKnowledgeIndexTextManifestBatch(int Ordinal, int FirstSequence, int ChunkCount, long TokenCount, string SourceHash);
/// <summary>清单只冻结预期集合，不证明向量齐全或允许 ready。</summary>
internal sealed record AiKnowledgeIndexTextManifest(AiKnowledgeIndexTextPlanIdentity Identity,
    AiKnowledgeIndexTextPlanSummary Summary, IReadOnlyList<AiKnowledgeIndexTextManifestBatch> Batches);
/// <summary>携带实际持久字节的摘要；提交不能用重新编码结果替代原清单。</summary>
internal sealed record AiKnowledgeIndexTextManifestSnapshot(AiKnowledgeIndexTextManifest Manifest, string PayloadHash);

/// <summary>固定有界二进制清单；不静默演进既有向量载荷格式。</summary>
internal static class AiKnowledgeIndexTextManifestCodec
{
    internal const string FormatKey = "knowledge-text-manifest-v1";
    internal const int MaxPayloadBytes = 131072;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    /// <summary>只编码由完整规划器冻结的输入，不接受调用方拼接的清单或正文。</summary>
    internal static byte[] Encode(AiKnowledgeIndexTextPlan plan, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(plan);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Utf8, true);
        Text(writer, FormatKey); AiKnowledgeIndexTextPlan.WriteHeader(writer, plan.Identity);
        Text(writer, plan.Summary.PlanHash); writer.Write(plan.Summary.BatchCount); writer.Write(plan.Summary.ChunkCount); writer.Write(plan.Summary.TokenCount);
        foreach (var batch in plan.Batches)
        {
            token.ThrowIfCancellationRequested(); writer.Write(batch.Ordinal); writer.Write(batch.Chunks[0].Sequence); writer.Write(batch.Chunks.Count);
            writer.Write(batch.Chunks.Sum(chunk => (long)chunk.TokenCount)); Text(writer, batch.SourceHash);
        }
        writer.Flush(); if (stream.Length > MaxPayloadBytes) throw Invalid();
        var result = stream.ToArray(); _ = Decode(result, token); return result;
    }

    /// <summary>先限制字节和批次数，再物化不可变清单；损坏格式抛受控异常，取消传播。</summary>
    internal static AiKnowledgeIndexTextManifest Decode(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 8 or > MaxPayloadBytes) throw Invalid();
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), false); using var reader = new BinaryReader(stream, Utf8);
            if (Text(reader) != FormatKey || Text(reader) != AiKnowledgeIndexTextPlan.FormatKey) throw Invalid();
            var knowledge = GuidText(Text(reader)); var knowledgeVersion = reader.ReadInt32(); var tenantText = Text(reader);
            Guid? tenant = tenantText == "host" ? null : GuidText(tenantText);
            var model = GuidText(Text(reader)); var modelVersion = reader.ReadInt32(); var provider = Text(reader); var modelId = Text(reader); var contentHash = Text(reader);
            if (knowledgeVersion <= 0 || modelVersion <= 0 || provider != "openai_compatible"
                || modelId is not ("text-embedding-3-small" or "text-embedding-3-large") || !Hash(contentHash)
                || Text(reader) != "utf8-text-v1" || Text(reader) != AiKnowledgeCl100kTokenizer.Version || Text(reader) != "structure-window-v1") throw Invalid();
            var format = (AiKnowledgeTextFormat)reader.ReadInt32(); var options = new AiKnowledgeChunkOptions();
            if (format is not (AiKnowledgeTextFormat.PlainText or AiKnowledgeTextFormat.Markdown)
                || reader.ReadInt32() != options.MaxTokens || reader.ReadInt32() != options.OverlapTokens
                || reader.ReadInt32() != options.MaxChunks || reader.ReadInt32() != options.MaxTotalTokens) throw Invalid();
            var identity = new AiKnowledgeIndexTextPlanIdentity(knowledge, knowledgeVersion, tenant, model, modelVersion, provider, modelId, contentHash, format);
            var planHash = Text(reader); var count = reader.ReadInt32(); var chunks = reader.ReadInt32(); var tokens = reader.ReadInt64();
            if (!Hash(planHash) || count is < 1 or > 2048 || chunks is < 1 or > 2048 || count > chunks || tokens is < 1 or > 1048576) throw Invalid();
            var batches = new AiKnowledgeIndexTextManifestBatch[count]; var sequence = 0; long totalTokens = 0;
            for (var index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested(); var ordinal = reader.ReadInt32(); var first = reader.ReadInt32(); var size = reader.ReadInt32();
                var batchTokens = reader.ReadInt64(); var sourceHash = Text(reader);
                if (ordinal != index || first != sequence || size is < 1 or > 16 || batchTokens < size || batchTokens > size * 512L || !Hash(sourceHash)) throw Invalid();
                sequence += size; totalTokens += batchTokens; if (sequence > 2048) throw Invalid();
                batches[index] = new(ordinal, first, size, batchTokens, sourceHash);
            }
            var summary = new AiKnowledgeIndexTextPlanSummary(AiKnowledgeIndexTextPlan.FormatKey, planHash, count, chunks, tokens);
            if (sequence != chunks || totalTokens != tokens || stream.Position != stream.Length
                || AiKnowledgeIndexTextPlan.ComputePlanHash(identity, summary, batches, token) != planHash) throw Invalid();
            token.ThrowIfCancellationRequested(); return new(identity, summary, Array.AsReadOnly(batches));
        }
        catch (EndOfStreamException exception) { throw new InvalidDataException("Invalid text manifest.", exception); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Invalid text manifest.", exception); }
    }

    private static Guid GuidText(string text) => Guid.TryParseExact(text, "N", out var id) && id != Guid.Empty && id.ToString("N") == text ? id : throw Invalid();
    private static bool Hash(string text) => text.Length == 64 && text.All(char.IsAsciiHexDigit);
    private static void Text(BinaryWriter writer, string text) { var bytes = Utf8.GetBytes(text); writer.Write(bytes.Length); writer.Write(bytes); }
    private static string Text(BinaryReader reader)
    {
        var length = reader.ReadInt32(); if (length is < 1 or > 256 || length > reader.BaseStream.Length - reader.BaseStream.Position) throw Invalid();
        return Utf8.GetString(reader.ReadBytes(length));
    }
    private static InvalidDataException Invalid() => new("Invalid text manifest.");
}
