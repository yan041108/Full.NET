using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>统一完整来源计划的持久协议；正文和全部坐标只保存一次，批次不携带重复元数据。</summary>
internal static class AiKnowledgeIndexDocumentManifestCodec
{
    internal const string FormatKey = "knowledge-document-manifest-v1";
    internal const int MaxPayloadBytes = 16 * 1024 * 1024 - 1;
    private const int Magic = 0x314d474b;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    internal static byte[] Encode(AiKnowledgeDocumentPlan plan, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(plan);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Utf8, true);
        var source = plan.Source; var doc = source.Document;
        writer.Write(Magic); Text(writer, FormatKey);
        GuidValue(writer, source.KnowledgeBaseId); writer.Write(source.KnowledgeBaseVersion);
        writer.Write(source.TenantId.HasValue); if (source.TenantId is { } tenant) GuidValue(writer, tenant);
        GuidValue(writer, source.ModelConfigId); writer.Write(source.ModelConfigVersion);
        Text(writer, source.ProviderKey); Text(writer, source.ModelId); Text(writer, source.TokenizerVersion);
        Text(writer, source.DataClassification); Text(writer, source.DestinationHash); Text(writer, source.ParserRuntimeId);
        writer.Write(source.Options.MaxTokens); writer.Write(source.Options.OverlapTokens); writer.Write(source.Options.MaxChunks); writer.Write(source.Options.MaxTotalTokens);
        writer.Write((int)doc.Format); Text(writer, doc.ContentHash); writer.Write(doc.SizeBytes);
        Text(writer, doc.Policy.InputFormat); Text(writer, doc.Policy.TextEncoding ?? ""); Text(writer, doc.Policy.CsvDelimiter ?? "");
        writer.Write(doc.Policy.CsvHeader); writer.Write(doc.Policy.IncludeHidden);
        var wire = AiKnowledgeParsedWireCodec.Encode(doc, token);
        try { writer.Write(wire.Length); writer.Write(wire); }
        finally { CryptographicOperations.ZeroMemory(wire); }
        writer.Write(source.Chunks.Count);
        foreach (var chunk in source.Chunks)
        { token.ThrowIfCancellationRequested(); writer.Write(chunk.Sequence); writer.Write(chunk.BlockSequence); writer.Write(chunk.Offset); writer.Write(chunk.Length); writer.Write(chunk.TokenCount); }
        Text(writer, plan.PlanHash); writer.Write(plan.Batches.Count); writer.Write(plan.TokenCount);
        writer.Flush(); token.ThrowIfCancellationRequested(); if (stream.Length > MaxPayloadBytes) throw Invalid(); return stream.ToArray();
    }

    /// <summary>先限制每次分配，再从完整来源恢复正文并重算全计划；不信任摘要或部分块。</summary>
    internal static AiKnowledgeDocumentPlan Decode(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (bytes is null || bytes.Length is < 8 or > MaxPayloadBytes) throw Invalid();
        var captured = bytes.ToArray();
        try
        {
            using var stream = new MemoryStream(captured, false); using var reader = new BinaryReader(stream, Utf8);
            if (reader.ReadInt32() != Magic || Text(reader) != FormatKey) throw Invalid();
            var knowledge = GuidValue(reader); var knowledgeVersion = reader.ReadInt32();
            Guid? tenant = Boolean(reader) ? GuidValue(reader) : null;
            var model = GuidValue(reader); var modelVersion = reader.ReadInt32(); var provider = Text(reader); var modelId = Text(reader); var tokenizer = Text(reader);
            var classification = Text(reader); var destination = Text(reader); var runtime = Text(reader);
            var options = new AiKnowledgeChunkOptions(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            var format = (AiKnowledgeParsedFormat)reader.ReadInt32(); if (!Enum.IsDefined(format)) throw Invalid();
            var hash = Text(reader); if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c)) || hash != hash.ToLowerInvariant()) throw Invalid();
            var size = reader.ReadInt32(); var inputFormat = Text(reader); var encoding = Text(reader); var delimiter = Text(reader);
            var policy = new AiKnowledgeParsePolicy(inputFormat, encoding.Length == 0 ? null : encoding, delimiter.Length == 0 ? null : delimiter, Boolean(reader), Boolean(reader));
            var wireLength = reader.ReadInt32();
            if (wireLength is < 2 or > AiKnowledgeParsedWireCodec.MaxWireBytes || wireLength > stream.Length - stream.Position) throw Invalid();
            var wire = reader.ReadBytes(wireLength); AiKnowledgeParsedDocument? document;
            try { document = AiKnowledgeParsedWireCodec.Decode(wire, format, hash, size, token, policy); }
            finally { CryptographicOperations.ZeroMemory(wire); }
            if (document is null) throw Invalid();
            var count = reader.ReadInt32(); if (count is < 1 or > 2048) throw Invalid();
            var chunks = new AiKnowledgeDocumentChunk[count]; var characters = 0;
            for (var index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested(); var sequence = reader.ReadInt32(); var blockSequence = reader.ReadInt32();
                var offset = reader.ReadInt32(); var length = reader.ReadInt32(); var tokens = reader.ReadInt32();
                if (blockSequence < 0 || blockSequence >= document.Blocks.Count || length <= 0 || length > AiKnowledgeTextChunker.MaxTotalCharacters - characters) throw Invalid();
                var block = document.Blocks[blockSequence]; var relative = offset - block.Position.Offset;
                var position = block.Locate(relative, length); if (position is null) throw Invalid();
                characters += length; chunks[index] = new(sequence, blockSequence, block.Text.Substring(relative, length), tokens, position);
            }
            var expectedHash = Text(reader); var batchCount = reader.ReadInt32(); var tokenCount = reader.ReadInt64();
            if (stream.Position != stream.Length) throw Invalid();
            var source = new AiKnowledgeDocumentModelChunks(knowledge, knowledgeVersion, tenant, model, modelVersion, provider, modelId, document, tokenizer, options, Array.AsReadOnly(chunks))
                { DataClassification = classification, DestinationHash = destination, ParserRuntimeId = runtime };
            var plan = AiKnowledgeDocumentPlan.Create(source, token);
            if (plan is null || plan.PlanHash != expectedHash || plan.Batches.Count != batchCount || plan.TokenCount != tokenCount) throw Invalid();
            token.ThrowIfCancellationRequested(); return plan;
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("Invalid document manifest.", error); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid document manifest.", error); }
        finally { CryptographicOperations.ZeroMemory(captured); }
    }

    internal static void Text(BinaryWriter writer, string value)
    { var bytes = Utf8.GetBytes(value); try { writer.Write(bytes.Length); writer.Write(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); } }
    internal static string Text(BinaryReader reader, int maxCharacters = 256)
    {
        var length = reader.ReadInt32(); if (length < 0 || length > checked(maxCharacters * 4) || length > reader.BaseStream.Length - reader.BaseStream.Position) throw Invalid();
        var text = Utf8.GetString(reader.ReadBytes(length)); if (text.Length > maxCharacters) throw Invalid(); return text;
    }
    internal static bool Boolean(BinaryReader reader) => reader.ReadByte() switch { 0 => false, 1 => true, _ => throw Invalid() };
    internal static Guid GuidValue(BinaryReader reader)
    { if (reader.BaseStream.Length - reader.BaseStream.Position < 16) throw Invalid(); var value = new Guid(reader.ReadBytes(16), bigEndian: true); return value != Guid.Empty ? value : throw Invalid(); }
    /// <summary>协议标识统一网络字节序，禁止混用平台默认 Guid 字节序。</summary>
    internal static void GuidValue(BinaryWriter writer, Guid value)
    { Span<byte> bytes = stackalloc byte[16]; if (!value.TryWriteBytes(bytes, bigEndian: true, out var written) || written != 16) throw Invalid(); writer.Write(bytes); }
    private static InvalidDataException Invalid() => new("Invalid document manifest.");
}
