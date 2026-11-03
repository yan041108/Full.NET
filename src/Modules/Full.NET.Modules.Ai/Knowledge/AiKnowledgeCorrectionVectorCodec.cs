using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>人工来源只从已核证的完整清单取得；向量字节不能自行声明正文或原文位置。</summary>
internal sealed record AiKnowledgeCorrectionEmbeddedChunk(AiKnowledgeCorrectionIndexChunk Source, ReadOnlyMemory<float> Vector);
/// <summary>单批原字节及完整修订计划的核证结果，不代表收费、授权或整代次完整。</summary>
internal sealed record AiKnowledgeCorrectionVectorBatch(string PlanHash, int Ordinal, string SourceHash, int Dimensions,
    string PayloadHash, IReadOnlyList<AiKnowledgeCorrectionEmbeddedChunk> Chunks);

/// <summary>有界人工向量协议只保存片段顺序和数值；全部来源集合从完整计划恢复。</summary>
internal static class AiKnowledgeCorrectionVectorCodec
{
    internal const string FormatKey = "knowledge-correction-vector-v1";
    internal const int MaxPayloadBytes = 512 * 1024;
    private const int Magic = 0x3143564b;
    internal static int Dimensions(AiKnowledgeCorrectionIndexPlan plan) => plan.Model.ModelId == "text-embedding-3-small" ? 1536 : 3072;

    internal static byte[] Encode(AiKnowledgeCorrectionIndexPlan plan, int ordinal, IReadOnlyList<ReadOnlyMemory<float>> vectors, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(plan);
        if (ordinal < 0 || ordinal >= plan.Batches.Count) throw Invalid();
        var batch = plan.Batches[ordinal]; var dimensions = Dimensions(plan);
        var count = vectors?.Count ?? 0;
        if (count != batch.Chunks.Count || count is < 1 or > 16) throw Invalid();
        // 写入前捕获全部数值，不允许调用者在流式验证期间改写后续数组。
        var owned = new float[count][];
        for (var i = 0; i < owned.Length; i++) { token.ThrowIfCancellationRequested(); owned[i] = vectors![i].ToArray(); Validate(owned[i], dimensions); }
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true);
        try
        {
            writer.Write(Magic); AiKnowledgeIndexDocumentManifestCodec.Text(writer, FormatKey);
            AiKnowledgeIndexDocumentManifestCodec.Text(writer, plan.PlanHash); writer.Write(ordinal);
            AiKnowledgeIndexDocumentManifestCodec.Text(writer, batch.SourceHash); writer.Write(dimensions); writer.Write(owned.Length);
            for (var i = 0; i < owned.Length; i++)
            { token.ThrowIfCancellationRequested(); writer.Write(batch.Chunks[i].Sequence); foreach (var value in owned[i]) writer.Write(value); }
            writer.Flush(); token.ThrowIfCancellationRequested(); if (stream.Length > MaxPayloadBytes) throw Invalid(); return stream.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(stream.GetBuffer()); }
    }

    /// <summary>唯一解码入口必须取得完整计划；遗漏、重排、错修订、坏数值和尾随数据均拒绝整批。</summary>
    internal static AiKnowledgeCorrectionVectorBatch ReadBatch(AiKnowledgeCorrectionIndexPlan plan, int ordinal, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(plan);
        if (ordinal < 0 || ordinal >= plan.Batches.Count || bytes is null || bytes.Length is < 8 or > MaxPayloadBytes) throw Invalid();
        var owned = bytes.ToArray();
        try
        {
            using var stream = new MemoryStream(owned, false); using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
            if (reader.ReadInt32() != Magic || AiKnowledgeIndexDocumentManifestCodec.Text(reader) != FormatKey
                || AiKnowledgeIndexDocumentManifestCodec.Text(reader) != plan.PlanHash || reader.ReadInt32() != ordinal
                || AiKnowledgeIndexDocumentManifestCodec.Text(reader) != plan.Batches[ordinal].SourceHash) throw Invalid();
            var dimensions = reader.ReadInt32(); var count = reader.ReadInt32(); var expected = plan.Batches[ordinal];
            if (dimensions != Dimensions(plan) || count is < 1 or > 16 || count != expected.Chunks.Count) throw Invalid();
            var chunks = new AiKnowledgeCorrectionEmbeddedChunk[count];
            for (var i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (reader.ReadInt32() != expected.Chunks[i].Sequence || stream.Length - stream.Position < dimensions * sizeof(float)) throw Invalid();
                var vector = new float[dimensions]; for (var j = 0; j < dimensions; j++) vector[j] = reader.ReadSingle();
                Validate(vector, dimensions); chunks[i] = new(expected.Chunks[i], vector);
            }
            if (stream.Position != stream.Length) throw Invalid(); token.ThrowIfCancellationRequested();
            return new(plan.PlanHash, ordinal, expected.SourceHash, dimensions, Convert.ToHexString(SHA256.HashData(owned)), Array.AsReadOnly(chunks));
        }
        catch (Exception error) when (error is EndOfStreamException or DecoderFallbackException)
        { throw new InvalidDataException("Invalid correction vector payload.", error); }
        finally { CryptographicOperations.ZeroMemory(owned); }
    }
    private static void Validate(float[] vector, int dimensions)
    {
        if (vector.Length != dimensions) throw Invalid(); double norm = 0;
        foreach (var value in vector) { if (!float.IsFinite(value)) throw Invalid(); norm += (double)value * value; }
        if (norm <= 0) throw Invalid();
    }
    private static InvalidDataException Invalid() => new("Invalid correction vector payload.");
}
