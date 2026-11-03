using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>人工计划保存完整原解析和修订，不复制分块正文；恢复时重新生成全部分块与真实来源集合。</summary>
internal static class AiKnowledgeCorrectionManifestCodec
{
    internal const string FormatKey = "knowledge-correction-manifest-v1";
    internal const int MaxPayloadBytes = 16 * 1024 * 1024 - 1;
    private const int Magic = 0x31434b41;

    internal static byte[] Encode(AiKnowledgeCorrectionIndexPlan plan, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(plan); token.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true);
        try
        {
            var snapshot = plan.Snapshot; var identity = snapshot.Identity; var model = plan.Model;
            writer.Write(Magic); Text(writer, FormatKey); GuidValue(writer, identity.SourceId); GuidValue(writer, identity.RevisionId!.Value);
            writer.Write(identity.HeadVersion); Text(writer, identity.SourceHash); Text(writer, identity.PayloadHash); Text(writer, snapshot.ParserRuntimeId);
            GuidValue(writer, model.KnowledgeBaseId); writer.Write(model.KnowledgeBaseVersion);
            writer.Write(model.TenantId.HasValue); if (model.TenantId is { } tenant) GuidValue(writer, tenant);
            GuidValue(writer, model.ModelConfigId); writer.Write(model.ModelConfigVersion); Text(writer, model.ProviderKey); Text(writer, model.ModelId);
            Text(writer, model.DataClassification); Text(writer, model.DestinationHash); Text(writer, AiKnowledgeCl100kTokenizer.Version);
            var original = AiKnowledgeParsedWireCodec.Encode(snapshot.Document.Source, token);
            try { Bytes(writer, original); }
            finally { CryptographicOperations.ZeroMemory(original); }
            var correction = AiKnowledgeCorrectionWireCodec.Encode(snapshot.Document, snapshot.ParserRuntimeId, token);
            try { Bytes(writer, correction); }
            finally { CryptographicOperations.ZeroMemory(correction); }
            Text(writer, plan.PlanHash); writer.Write(plan.Chunks.Count); writer.Write(plan.Batches.Count); writer.Write(plan.TokenCount);
            writer.Flush(); token.ThrowIfCancellationRequested(); if (stream.Length > MaxPayloadBytes) throw Invalid(); return stream.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(stream.GetBuffer()); }
    }

    /// <summary>格式、摘要、字节数及策略必须来自当前权威来源或已授权归档；载荷不能自行声明来源资格。</summary>
    internal static AiKnowledgeCorrectionIndexPlan Decode(byte[] bytes, AiKnowledgeParsedFormat format, string fileHash,
        int size, AiKnowledgeParsePolicy policy, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (bytes is null || bytes.Length is < 8 or > MaxPayloadBytes) throw Invalid();
        var owned = bytes.ToArray();
        try
        {
            using var stream = new MemoryStream(owned, false); using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
            if (reader.ReadInt32() != Magic || Text(reader) != FormatKey) throw Invalid();
            var identity = new AiKnowledgeCorrectionIdentity(GuidValue(reader), GuidValue(reader), reader.ReadInt32(), Text(reader), Text(reader));
            var runtime = Text(reader); var knowledge = GuidValue(reader); var knowledgeVersion = reader.ReadInt32();
            Guid? tenant = AiKnowledgeIndexDocumentManifestCodec.Boolean(reader) ? GuidValue(reader) : null;
            var model = new AiKnowledgeCorrectionModelBinding(knowledge, knowledgeVersion, tenant, GuidValue(reader), reader.ReadInt32(),
                Text(reader), Text(reader), Text(reader), Text(reader));
            if (Text(reader) != AiKnowledgeCl100kTokenizer.Version || !model.Valid) throw Invalid();
            var originalWire = Bytes(reader, AiKnowledgeParsedWireCodec.MaxWireBytes); AiKnowledgeParsedDocument? original;
            try { original = AiKnowledgeParsedWireCodec.Decode(originalWire, format, fileHash, size, token, policy); }
            finally { CryptographicOperations.ZeroMemory(originalWire); }
            if (original is null) throw Invalid();
            var correctionWire = Bytes(reader, AiKnowledgeCorrectionWireCodec.MaxWireBytes); AiKnowledgeCorrectionDocument? corrected;
            try
            {
                if (Convert.ToHexStringLower(SHA256.HashData(correctionWire)) != identity.PayloadHash) throw Invalid();
                corrected = AiKnowledgeCorrectionWireCodec.Decode(correctionWire, original, runtime, token);
            }
            finally { CryptographicOperations.ZeroMemory(correctionWire); }
            if (corrected is null) throw Invalid();
            var expectedHash = Text(reader); var chunks = reader.ReadInt32(); var batches = reader.ReadInt32(); var tokens = reader.ReadInt64();
            if (stream.Position != stream.Length) throw Invalid();
            var plan = AiKnowledgeCorrectionIndexPlan.Create(new(identity, runtime, corrected), model, token);
            if (plan is null || plan.PlanHash != expectedHash || plan.Chunks.Count != chunks || plan.Batches.Count != batches || plan.TokenCount != tokens) throw Invalid();
            token.ThrowIfCancellationRequested(); return plan;
        }
        catch (Exception error) when (error is EndOfStreamException or DecoderFallbackException or ArgumentException or OverflowException)
        { throw new InvalidDataException("Invalid correction manifest.", error); }
        finally { CryptographicOperations.ZeroMemory(owned); }
    }

    private static void Bytes(BinaryWriter writer, byte[] bytes)
    { if (bytes.Length > MaxPayloadBytes - writer.BaseStream.Length - 4) throw Invalid(); writer.Write(bytes.Length); writer.Write(bytes); }
    private static byte[] Bytes(BinaryReader reader, int limit)
    { var length = reader.ReadInt32(); if (length < 2 || length > limit || length > reader.BaseStream.Length - reader.BaseStream.Position) throw Invalid(); return reader.ReadBytes(length); }
    private static void Text(BinaryWriter writer, string value) => AiKnowledgeIndexDocumentManifestCodec.Text(writer, value);
    private static string Text(BinaryReader reader) => AiKnowledgeIndexDocumentManifestCodec.Text(reader);
    private static void GuidValue(BinaryWriter writer, Guid value) => AiKnowledgeIndexDocumentManifestCodec.GuidValue(writer, value);
    private static Guid GuidValue(BinaryReader reader) => AiKnowledgeIndexDocumentManifestCodec.GuidValue(reader);
    private static InvalidDataException Invalid() => new("Invalid correction manifest.");
}
