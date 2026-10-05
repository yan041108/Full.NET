using System.Text;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>批次序号、全局块序号与连续页码范围，不含正文或向量。</summary>
internal sealed record AiKnowledgeIndexPdfManifestBatch(int Ordinal, int FirstSequence, int ChunkCount, long TokenCount,
    int FirstPageNumber, int LastPageNumber, string SourceHash);
/// <summary>完整预期集合；消费方仍须复核来源、批准版本与实际持久片段。</summary>
internal sealed record AiKnowledgeIndexPdfManifest(AiKnowledgeIndexPdfPlanIdentity Identity, AiKnowledgeIndexPdfPlanSummary Summary,
    IReadOnlyList<AiKnowledgeIndexPdfManifestBatch> Batches);

/// <summary>PDF 专用有界清单，拒绝未知或损坏格式，不扩展文本 v1。</summary>
internal static class AiKnowledgeIndexPdfManifestCodec
{
    internal const string FormatKey = "knowledge-pdf-manifest-v1";
    internal const int MaxPayloadBytes = 131072;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    /// <summary>只编码完整规划器的冻结结果，正文和凭据不会写入清单。</summary>
    internal static byte[] Encode(AiKnowledgeIndexPdfPlan plan, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(plan);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Utf8, true);
        AiKnowledgeIndexPdfPlan.Text(writer, FormatKey); AiKnowledgeIndexPdfPlan.WriteHeader(writer, plan.Identity);
        AiKnowledgeIndexPdfPlan.Text(writer, plan.Summary.PlanHash); writer.Write(plan.Summary.BatchCount);
        writer.Write(plan.Summary.ChunkCount); writer.Write(plan.Summary.TokenCount);
        foreach (var batch in plan.Batches)
        { token.ThrowIfCancellationRequested(); AiKnowledgeIndexPdfPlan.WriteBatch(writer, AiKnowledgeIndexPdfPlan.Metadata(batch)); }
        writer.Flush(); if (stream.Length > MaxPayloadBytes) throw Invalid();
        var bytes = stream.ToArray(); _ = Decode(bytes, token); return bytes;
    }

    /// <summary>先限制载荷、字符串和集合，再验证完整元数据与根摘要；取消原样传播。</summary>
    internal static AiKnowledgeIndexPdfManifest Decode(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 8 or > MaxPayloadBytes) throw Invalid();
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), false); using var reader = new BinaryReader(stream, Utf8);
            if (Text(reader) != FormatKey || Text(reader) != AiKnowledgeIndexPdfPlan.FormatKey) throw Invalid();
            var knowledge = GuidText(Text(reader)); var knowledgeVersion = reader.ReadInt32(); var tenantText = Text(reader);
            Guid? tenant = tenantText == "host" ? null : GuidText(tenantText);
            var model = GuidText(Text(reader)); var modelVersion = reader.ReadInt32(); var provider = Text(reader); var modelId = Text(reader); var contentHash = Text(reader);
            if (knowledgeVersion <= 0 || modelVersion <= 0 || provider != "openai_compatible"
                || modelId is not ("text-embedding-3-small" or "text-embedding-3-large") || !Hash(contentHash) || contentHash != contentHash.ToLowerInvariant()
                || Text(reader) != AiKnowledgeParsedPdf.ParserVersion || Text(reader) != AiKnowledgeCl100kTokenizer.Version || Text(reader) != "pdf-page-window-v1") throw Invalid();
            var pages = reader.ReadInt32(); var options = new AiKnowledgeChunkOptions();
            if (pages is < 1 or > 128 || reader.ReadInt32() != options.MaxTokens || reader.ReadInt32() != options.OverlapTokens
                || reader.ReadInt32() != options.MaxChunks || reader.ReadInt32() != options.MaxTotalTokens) throw Invalid();
            var identity = new AiKnowledgeIndexPdfPlanIdentity(knowledge, knowledgeVersion, tenant, model, modelVersion, provider, modelId, contentHash, pages);
            var planHash = Text(reader); var count = reader.ReadInt32(); var chunks = reader.ReadInt32(); var tokens = reader.ReadInt64();
            if (!Hash(planHash) || count is < 1 or > 2048 || chunks is < 1 or > 2048 || count > chunks || pages > chunks || tokens < chunks || tokens > 1048576) throw Invalid();
            var batches = new AiKnowledgeIndexPdfManifestBatch[count]; var sequence = 0; var lastPage = 0; long totalTokens = 0;
            for (var index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested(); var ordinal = reader.ReadInt32(); var first = reader.ReadInt32(); var size = reader.ReadInt32();
                var batchTokens = reader.ReadInt64(); var firstPage = reader.ReadInt32(); var finalPage = reader.ReadInt32(); var sourceHash = Text(reader);
                if (ordinal != index || first != sequence || size is < 1 or > 16 || batchTokens < size || batchTokens > size * 512L || !Hash(sourceHash)
                    || firstPage < 1 || firstPage < lastPage || firstPage > lastPage + 1 || (index == 0 && firstPage != 1)
                    || finalPage < firstPage || finalPage > pages || finalPage - firstPage + 1 > size) throw Invalid();
                sequence += size; totalTokens += batchTokens; lastPage = finalPage; if (sequence > chunks || totalTokens > tokens) throw Invalid();
                batches[index] = new(ordinal, first, size, batchTokens, firstPage, finalPage, sourceHash);
            }
            var summary = new AiKnowledgeIndexPdfPlanSummary(AiKnowledgeIndexPdfPlan.FormatKey, planHash, count, chunks, tokens);
            if (sequence != chunks || totalTokens != tokens || lastPage != pages || stream.Position != stream.Length
                || AiKnowledgeIndexPdfPlan.ComputePlanHash(identity, summary, batches, token) != planHash) throw Invalid();
            token.ThrowIfCancellationRequested(); return new(identity, summary, Array.AsReadOnly(batches));
        }
        catch (EndOfStreamException exception) { throw new InvalidDataException("Invalid PDF manifest.", exception); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Invalid PDF manifest.", exception); }
    }

    private static Guid GuidText(string value) => Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty && id.ToString("N") == value ? id : throw Invalid();
    internal static bool Hash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static string Text(BinaryReader reader)
    {
        var length = reader.ReadInt32(); if (length is < 1 or > 256 || length > reader.BaseStream.Length - reader.BaseStream.Position) throw Invalid();
        return Utf8.GetString(reader.ReadBytes(length));
    }
    private static InvalidDataException Invalid() => new("Invalid PDF manifest.");
}
