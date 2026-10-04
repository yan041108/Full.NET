using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>派生载荷要保存完整定位与向量，损坏或超限不能返回部分批次。</summary>
[TestClass]
public sealed class AiKnowledgeIndexVectorCodecTests
{
    [TestMethod] [DataRow(1536)] [DataRow(3072)]
    public void Roundtrip_preserves_text_context_options_and_exact_vector(int dimensions)
    {
        var batch = Sample(dimensions);
        var bytes = AiKnowledgeIndexVectorCodec.Encode(batch, dimensions, default);
        var decoded = AiKnowledgeIndexVectorCodec.Decode(bytes, default);
        Assert.AreEqual(dimensions, decoded.Dimensions); Assert.AreEqual(batch.ParserVersion, decoded.ParserVersion);
        Assert.AreEqual(batch.TokenizerVersion, decoded.TokenizerVersion); Assert.AreEqual(batch.ChunkingVersion, decoded.ChunkingVersion);
        Assert.AreEqual(batch.Options, decoded.Options); Assert.AreEqual(1, decoded.Chunks.Count);
        var chunk = decoded.Chunks[0]; Assert.AreEqual(batch.Chunks[0].Source.Text, chunk.Source.Text);
        Assert.AreEqual(batch.Chunks[0].Source.EmbeddingText, chunk.Source.EmbeddingText);
        Assert.AreEqual(batch.Chunks[0].Source.Offset, chunk.Source.Offset); Assert.AreEqual(batch.Chunks[0].Source.Kind, chunk.Source.Kind);
        CollectionAssert.AreEqual(batch.Chunks[0].Source.Context.ToArray(), chunk.Source.Context.ToArray());
        CollectionAssert.AreEqual(batch.Chunks[0].Source.RepeatedContext.ToArray(), chunk.Source.RepeatedContext.ToArray());
        CollectionAssert.AreEqual(batch.Chunks[0].Vector.ToArray(), chunk.Vector.ToArray());
    }

    [TestMethod] [DataRow("empty")] [DataRow("count")] [DataRow("dimensions")] [DataRow("nan")]
    [DataRow("infinity")] [DataRow("zero")] [DataRow("offset")] [DataRow("length")] [DataRow("tokens")]
    [DataRow("sequence")] [DataRow("kind")] [DataRow("context")] [DataRow("text")]
    [DataRow("parser")] [DataRow("tokenizer")] [DataRow("chunker")] [DataRow("options")] [DataRow("utf16")]
    public void Invalid_batch_fails_before_producing_payload(string fault)
    {
        var batch = Sample(); var source = batch.Chunks[0].Source; var vector = batch.Chunks[0].Vector.ToArray();
        source = fault switch
        {
            "offset" => source with { Offset = -1 }, "length" => source with { Length = 1 }, "tokens" => source with { TokenCount = 513 },
            "sequence" => source with { Sequence = 2048 }, "kind" => source with { Kind = (AiKnowledgeTextBlockKind)99 },
            "context" => source with { Context = [new(AiKnowledgeContextKind.Heading, int.MaxValue, 10, 1)] },
            "text" => source with { EmbeddingText = new string('x', 8001) },
            "utf16" => source with { EmbeddingText = "\ud800" + source.Text }, _ => source,
        };
        if (fault == "nan") vector[0] = float.NaN; if (fault == "infinity") vector[0] = float.PositiveInfinity;
        if (fault == "zero") Array.Clear(vector); if (fault == "dimensions") vector = [1];
        batch = batch with { Chunks = [new(source, vector)] };
        batch = fault switch
        {
            "empty" => batch with { Chunks = [] }, "count" => batch with { Chunks = Enumerable.Repeat(batch.Chunks[0], 17).ToArray() },
            "parser" => batch with { ParserVersion = "other" }, "tokenizer" => batch with { TokenizerVersion = "other" },
            "chunker" => batch with { ChunkingVersion = "other" }, "options" => batch with { Options = new(MaxTokens: 0) }, _ => batch,
        };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexVectorCodec.Encode(batch, 1536, default));
    }

    [TestMethod] [DataRow("magic")] [DataRow("dimension")] [DataRow("truncated")]
    [DataRow("trailing")] [DataRow("large")] [DataRow("negative_length")]
    public void Corrupt_wire_format_is_rejected(string fault)
    {
        var payload = AiKnowledgeIndexVectorCodec.Encode(Sample(), 1536, default);
        switch (fault)
        {
            case "magic": payload[0] ^= 1; break;
            case "dimension": BitConverter.GetBytes(100).CopyTo(payload, 4); break;
            case "truncated": payload = payload[..^1]; break;
            case "trailing": payload = [.. payload, 0]; break;
            case "large": payload = new byte[AiKnowledgeIndexVectorCodec.MaxPayloadBytes + 1]; break;
            case "negative_length": BitConverter.GetBytes(-1).CopyTo(payload, 8); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexVectorCodec.Decode(payload, default));
    }

    [TestMethod]
    public void Wire_bytes_are_deterministic_and_detached_from_mutable_vector()
    {
        var batch = Sample(); var first = AiKnowledgeIndexVectorCodec.Encode(batch, 1536, default);
        CollectionAssert.AreEqual(first, AiKnowledgeIndexVectorCodec.Encode(batch, 1536, default));
        var original = batch.Chunks[0].Vector.ToArray();
        var decoded = AiKnowledgeIndexVectorCodec.Decode(first, default); first[^1] ^= 64;
        CollectionAssert.AreEqual(original, decoded.Chunks[0].Vector.ToArray());
    }

    [TestMethod]
    public void Maximum_batch_is_bounded_and_roundtrips_without_truncation()
    {
        var batch = Sample(3072); var chunks = Enumerable.Range(0, 16).Select(index => batch.Chunks[0] with
        { Source = batch.Chunks[0].Source with { Sequence = index } }).ToArray();
        var bytes = AiKnowledgeIndexVectorCodec.Encode(batch with { Chunks = chunks }, 3072, default);
        Assert.IsTrue(bytes.Length <= AiKnowledgeIndexVectorCodec.MaxPayloadBytes);
        Assert.AreEqual(16, AiKnowledgeIndexVectorCodec.Decode(bytes, default).Chunks.Count);
    }

    [TestMethod]
    public void Cancellation_is_observed_before_encoding_or_decoding()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeIndexVectorCodec.Encode(Sample(), 1536, cancel.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeIndexVectorCodec.Decode([], cancel.Token));
    }

    [TestMethod]
    public void Oversize_collection_is_rejected_before_enumeration_or_copying()
    {
        var batch = Sample() with { Chunks = new OversizeChunks() };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexVectorCodec.Encode(batch, 1536, default));
    }

    private sealed class OversizeChunks : IReadOnlyList<AiKnowledgeEmbeddedChunk>
    {
        public int Count => int.MaxValue;
        public AiKnowledgeEmbeddedChunk this[int index] => throw new InvalidOperationException("must not enumerate oversized input");
        public IEnumerator<AiKnowledgeEmbeddedChunk> GetEnumerator() => throw new InvalidOperationException("must not enumerate oversized input");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static AiKnowledgeIndexEmbeddingBatch Sample(int dimensions = 1536)
    {
        var vector = new float[dimensions]; vector[0] = 1; vector[^1] = -0.25f;
        AiKnowledgeSourceContext[] context = [new(AiKnowledgeContextKind.Heading, 0, 4, 1)];
        var chunk = new AiKnowledgeStructuredChunk(0, 4, 4, "正文😀", "标题\n正文😀", 7, AiKnowledgeTextBlockKind.Paragraph, context, context);
        return new(Guid.CreateVersion7(), 0, new AiKnowledgeIndexDispatchRecord(), "utf8-text-v1", AiKnowledgeCl100kTokenizer.Version,
            "structure-window-v1", new(), [new(chunk, vector)]);
    }
}
