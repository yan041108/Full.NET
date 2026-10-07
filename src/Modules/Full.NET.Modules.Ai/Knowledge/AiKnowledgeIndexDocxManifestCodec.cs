using System.Text;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>完整 DOCX 计划的有界持久协议。</summary>
internal static class AiKnowledgeIndexDocxManifestCodec
{
    internal const string FormatKey = "knowledge-docx-manifest-v1";
    internal const int MaxPayloadBytes = 8 * 1024 * 1024;
    private const int Magic = 0x314d444b;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    /// <summary>正文只保存一次，块文本从全局区间恢复；清单不含向量、凭据或费用证明。</summary>
    internal static byte[] Encode(AiKnowledgeIndexDocxPlan plan, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(plan);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Utf8, true);
        var source = plan.Source; var doc = source.Document;
        writer.Write(Magic); Text(writer, FormatKey);
        GuidValue(writer, source.KnowledgeBaseId); writer.Write(source.KnowledgeBaseVersion);
        writer.Write(source.TenantId.HasValue); if (source.TenantId is { } tenant) GuidValue(writer, tenant);
        GuidValue(writer, source.ModelConfigId); writer.Write(source.ModelConfigVersion);
        Text(writer, source.ProviderKey); Text(writer, source.ModelId); Text(writer, source.TokenizerVersion);
        writer.Write(source.Options.MaxTokens); writer.Write(source.Options.OverlapTokens); writer.Write(source.Options.MaxChunks); writer.Write(source.Options.MaxTotalTokens);
        Text(writer, AiKnowledgeParsedDocx.ParserVersion); Text(writer, doc.ContentHash); writer.Write(doc.SizeBytes); Text(writer, doc.Text);
        writer.Write(doc.Blocks.Count);
        foreach (var block in doc.Blocks)
        {
            token.ThrowIfCancellationRequested(); writer.Write(block.Sequence); Text(writer, block.Kind); writer.Write(block.Offset); writer.Write(block.Length);
            writer.Write(block.ParagraphNumber); writer.Write(block.TableNumber ?? 0); writer.Write(block.RowNumber ?? 0); writer.Write(block.CellNumber ?? 0);
            writer.Write(block.CellParagraphCount); writer.Write(block.IsHeader);
        }
        writer.Write(source.Chunks.Count);
        foreach (var chunk in source.Chunks)
        { token.ThrowIfCancellationRequested(); writer.Write(chunk.Sequence); writer.Write(chunk.BlockSequence); writer.Write(chunk.Offset); writer.Write(chunk.Length); writer.Write(chunk.TokenCount); }
        Text(writer, plan.Summary.PlanHash); writer.Write(plan.Summary.BatchCount); writer.Write(plan.Summary.TokenCount);
        writer.Flush(); token.ThrowIfCancellationRequested(); if (stream.Length > MaxPayloadBytes) throw Invalid(); return stream.ToArray();
    }

    /// <summary>捕获实际原字节后严格解码并重建全计划；每个分配先受长度和累计字符预算约束。</summary>
    internal static AiKnowledgeIndexDocxPlan Decode(byte[] bytes, CancellationToken token)
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
            var options = new AiKnowledgeChunkOptions(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            if (Text(reader) != AiKnowledgeParsedDocx.ParserVersion) throw Invalid();
            var hash = Text(reader);
            // 完整计划会规范化来源摘要；持久协议先拒绝别名，不能让解码悄然改写原载荷身份。
            if (hash.Length != 64 || hash.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))) throw Invalid();
            var size = reader.ReadInt32(); var text = Text(reader, AiKnowledgeTextParser.MaxOutputCharacters);
            var count = reader.ReadInt32(); if (count is < 1 or > 8192) throw Invalid();
            var blocks = new AiKnowledgeDocxBlock[count];
            for (var index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested(); var sequence = reader.ReadInt32(); var kind = Text(reader, 16); var offset = reader.ReadInt32(); var length = reader.ReadInt32();
                var paragraph = reader.ReadInt32(); var table = reader.ReadInt32(); var row = reader.ReadInt32(); var cell = reader.ReadInt32(); var paragraphs = reader.ReadInt32(); var header = Boolean(reader);
                blocks[index] = new(sequence, kind, offset, length, paragraph, table == 0 ? null : table, row == 0 ? null : row, cell == 0 ? null : cell, paragraphs, header);
            }
            var document = new AiKnowledgeParsedDocx(hash, size, text, Array.AsReadOnly(blocks));
            if (AiKnowledgeDocxStructure.Validate(document, token) != AiKnowledgeDocxReadFailure.None) throw Invalid();
            count = reader.ReadInt32(); if (count is < 1 or > 2048) throw Invalid();
            var chunks = new AiKnowledgeDocxChunk[count]; var characters = 0;
            for (var index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested(); var sequence = reader.ReadInt32(); var block = reader.ReadInt32(); var offset = reader.ReadInt32(); var length = reader.ReadInt32(); var tokens = reader.ReadInt32();
                if (offset < 0 || length <= 0 || offset > text.Length || length > text.Length - offset || length > AiKnowledgeTextChunker.MaxTotalCharacters - characters) throw Invalid();
                characters += length; chunks[index] = new(sequence, block, offset, length, text.Substring(offset, length), tokens);
            }
            var expectedHash = Text(reader); var batchCount = reader.ReadInt32(); var tokenCount = reader.ReadInt64();
            if (stream.Position != stream.Length) throw Invalid();
            var source = new AiKnowledgeDocxModelChunks(knowledge, knowledgeVersion, tenant, model, modelVersion, provider, modelId, document, tokenizer, options, Array.AsReadOnly(chunks));
            var plan = AiKnowledgeIndexDocxPlan.Create(source, token);
            if (plan is null || plan.Summary.PlanHash != expectedHash || plan.Summary.BatchCount != batchCount || plan.Summary.TokenCount != tokenCount) throw Invalid();
            token.ThrowIfCancellationRequested(); return plan;
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("Invalid DOCX manifest.", error); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid DOCX manifest.", error); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(captured); }
    }

    internal static void Text(BinaryWriter writer, string value)
    { var bytes = Utf8.GetBytes(value); try { writer.Write(bytes.Length); writer.Write(bytes); } finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); } }
    internal static string Text(BinaryReader reader, int maxCharacters = 256)
    {
        var length = reader.ReadInt32(); if (length < 0 || length > checked(maxCharacters * 4) || length > reader.BaseStream.Length - reader.BaseStream.Position) throw Invalid();
        var text = Utf8.GetString(reader.ReadBytes(length)); if (text.Length > maxCharacters) throw Invalid(); return text;
    }
    internal static bool Boolean(BinaryReader reader) => reader.ReadByte() switch { 0 => false, 1 => true, _ => throw Invalid() };
    internal static Guid GuidValue(BinaryReader reader)
    { if (reader.BaseStream.Length - reader.BaseStream.Position < 16) throw Invalid(); var value = new Guid(reader.ReadBytes(16), bigEndian: true); return value != Guid.Empty ? value : throw Invalid(); }
    /// <summary>协议标识统一网络字节序，禁止平台默认 Guid 字节转换与 Binary16 存储混用。</summary>
    internal static void GuidValue(BinaryWriter writer, Guid value)
    { Span<byte> bytes = stackalloc byte[16]; if (!value.TryWriteBytes(bytes, bigEndian: true, out var written) || written != 16) throw Invalid(); writer.Write(bytes); }
    private static InvalidDataException Invalid() => new("Invalid DOCX manifest.");
}
