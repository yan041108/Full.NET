using System.Text;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>一个完整文本批次的派生载荷，恢复不依赖模型再次派发。</summary>
internal sealed record AiKnowledgeIndexVectorPayload(int Dimensions, string ParserVersion, string TokenizerVersion,
    string ChunkingVersion, AiKnowledgeChunkOptions Options, IReadOnlyList<AiKnowledgeEmbeddedChunk> Chunks);

/// <summary>固定有界格式；不使用反射序列化或供应商原生向量类型。</summary>
internal static class AiKnowledgeIndexVectorCodec
{
    internal const int MaxPayloadBytes = 512 * 1024;
    internal const string FormatKey = "knowledge-vector-batch-v1";
    private const int Magic = 0x3156424B;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    /// <summary>先冻结所有可变集合，验证后按固定小端字段写入，不裁剪正文或向量。</summary>
    internal static byte[] Encode(AiKnowledgeIndexEmbeddingBatch batch, int dimensions, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(batch);
        // 在复制前限制集合和向量，不能等到校验阶段才拒绝巨大分配。
        if (dimensions is not (1536 or 3072) || batch.Chunks.Count is < 1 or > 16) throw Invalid();
        foreach (var item in batch.Chunks)
            if (item.Vector.Length != dimensions || item.Source.Context.Count > 8 || item.Source.RepeatedContext.Count > 8) throw Invalid();
        var payload = new AiKnowledgeIndexVectorPayload(dimensions, batch.ParserVersion, batch.TokenizerVersion, batch.ChunkingVersion,
            batch.Options, Array.AsReadOnly(batch.Chunks.Select(item => new AiKnowledgeEmbeddedChunk(item.Source with
            { Context = item.Source.Context.ToArray(), RepeatedContext = item.Source.RepeatedContext.ToArray() }, item.Vector.ToArray())).ToArray()));
        Validate(payload, token);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Utf8, true);
        writer.Write(Magic); writer.Write(dimensions);
        WriteText(writer, payload.ParserVersion); WriteText(writer, payload.TokenizerVersion); WriteText(writer, payload.ChunkingVersion);
        writer.Write(payload.Options.MaxTokens); writer.Write(payload.Options.OverlapTokens);
        writer.Write(payload.Options.MaxChunks); writer.Write(payload.Options.MaxTotalTokens); writer.Write(payload.Chunks.Count);
        foreach (var item in payload.Chunks)
        {
            token.ThrowIfCancellationRequested(); var chunk = item.Source;
            writer.Write(chunk.Sequence); writer.Write(chunk.Offset); writer.Write(chunk.Length); writer.Write(chunk.TokenCount); writer.Write((int)chunk.Kind);
            WriteText(writer, chunk.Text); WriteText(writer, chunk.EmbeddingText);
            WriteContext(writer, chunk.Context); WriteContext(writer, chunk.RepeatedContext);
            foreach (var value in item.Vector.Span) writer.Write(value);
        }
        token.ThrowIfCancellationRequested();
        if (stream.Length > MaxPayloadBytes) throw Invalid();
        return stream.ToArray();
    }

    /// <summary>每个长度在分配前检查；格式、数值或尾随数据异常时不返回任何片段。</summary>
    internal static AiKnowledgeIndexVectorPayload Decode(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (bytes is null || bytes.Length is < 8 or > MaxPayloadBytes) throw Invalid();
        try
        {
            using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream, Utf8, true);
            if (reader.ReadInt32() != Magic) throw Invalid();
            var dimensions = reader.ReadInt32(); if (dimensions is not (1536 or 3072)) throw Invalid();
            var parser = ReadText(reader); var tokenizer = ReadText(reader); var chunker = ReadText(reader);
            var options = new AiKnowledgeChunkOptions(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            var count = Count(reader, 16); if (count == 0) throw Invalid();
            var chunks = new AiKnowledgeEmbeddedChunk[count];
            for (var index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                var sequence = reader.ReadInt32(); var offset = reader.ReadInt32(); var length = reader.ReadInt32();
                var tokens = reader.ReadInt32(); var kind = (AiKnowledgeTextBlockKind)reader.ReadInt32();
                var text = ReadText(reader); var embedding = ReadText(reader); var context = ReadContext(reader); var repeated = ReadContext(reader);
                // 维度已是固定白名单；完整剩余字节必须足够才分配向量。
                if (stream.Length - stream.Position < dimensions * sizeof(float)) throw Invalid();
                var vector = new float[dimensions]; for (var n = 0; n < dimensions; n++) vector[n] = reader.ReadSingle();
                chunks[index] = new(new(sequence, offset, length, text, embedding, tokens, kind, context, repeated), vector);
            }
            if (stream.Position != stream.Length) throw Invalid();
            var result = new AiKnowledgeIndexVectorPayload(dimensions, parser, tokenizer, chunker, options, Array.AsReadOnly(chunks));
            Validate(result, token); return result;
        }
        catch (EndOfStreamException) { throw Invalid(); }
        catch (DecoderFallbackException) { throw Invalid(); }
    }

    private static void Validate(AiKnowledgeIndexVectorPayload payload, CancellationToken token)
    {
        if (payload.Dimensions is not (1536 or 3072) || payload.ParserVersion != "utf8-text-v1"
            || payload.TokenizerVersion != AiKnowledgeCl100kTokenizer.Version || payload.ChunkingVersion != "structure-window-v1"
            || payload.Options != new AiKnowledgeChunkOptions()
            || AiEmbeddingContentPolicy.ValidateInputs(payload.Chunks.Select(item => item.Source.EmbeddingText).ToArray()) is not null) throw Invalid();
        var previous = -1;
        foreach (var item in payload.Chunks)
        {
            token.ThrowIfCancellationRequested(); var chunk = item.Source;
            if (chunk.Sequence is < 0 or >= 2048 || (previous >= 0 && chunk.Sequence != previous + 1)
                || chunk.Offset < 0 || chunk.Length <= 0 || (long)chunk.Offset + chunk.Length > AiKnowledgeTextParser.MaxOutputCharacters
                || chunk.Length != chunk.Text.Length || !chunk.EmbeddingText.EndsWith(chunk.Text, StringComparison.Ordinal)
                || chunk.TokenCount is < 1 or > 512 || chunk.Kind is not (AiKnowledgeTextBlockKind.Paragraph or AiKnowledgeTextBlockKind.Code or AiKnowledgeTextBlockKind.Table)
                || item.Vector.Length != payload.Dimensions) throw Invalid();
            ValidateContext(chunk.Context); ValidateContext(chunk.RepeatedContext);
            double norm = 0;
            foreach (var value in item.Vector.Span)
            { if (!float.IsFinite(value)) throw Invalid(); norm += (double)value * value; }
            if (norm <= 0) throw Invalid(); previous = chunk.Sequence;
        }
    }

    private static void ValidateContext(IReadOnlyList<AiKnowledgeSourceContext> context)
    {
        if (context.Count > 8) throw Invalid();
        foreach (var item in context)
            if (item.Offset < 0 || item.Length <= 0 || (long)item.Offset + item.Length > AiKnowledgeTextParser.MaxOutputCharacters
                || (item.Kind == AiKnowledgeContextKind.Heading ? item.Level is < 1 or > 6
                    : item.Kind != AiKnowledgeContextKind.TableHeader || item.Level != 0)) throw Invalid();
    }
    private static void WriteText(BinaryWriter writer, string value)
    {
        try { var bytes = Utf8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
        catch (EncoderFallbackException) { throw Invalid(); }
    }
    private static string ReadText(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is < 0 or > 32000 || length > reader.BaseStream.Length - reader.BaseStream.Position) throw Invalid();
        var text = Utf8.GetString(reader.ReadBytes(length)); if (text.Length > 8000) throw Invalid(); return text;
    }
    private static void WriteContext(BinaryWriter writer, IReadOnlyList<AiKnowledgeSourceContext> context)
    {
        writer.Write(context.Count);
        foreach (var item in context) { writer.Write((int)item.Kind); writer.Write(item.Offset); writer.Write(item.Length); writer.Write(item.Level); }
    }
    private static IReadOnlyList<AiKnowledgeSourceContext> ReadContext(BinaryReader reader)
    {
        var items = new AiKnowledgeSourceContext[Count(reader, 8)];
        for (var index = 0; index < items.Length; index++) items[index] = new((AiKnowledgeContextKind)reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        return Array.AsReadOnly(items);
    }
    private static int Count(BinaryReader reader, int max)
    { var count = reader.ReadInt32(); return count >= 0 && count <= max ? count : throw Invalid(); }
    private static InvalidDataException Invalid() => new("Invalid knowledge vector batch payload.");
}
