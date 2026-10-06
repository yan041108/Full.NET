using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>分页载荷必须绑定完整清单，损坏输入不能返回部分向量。</summary>
[TestClass]
public sealed class AiKnowledgeIndexPdfVectorCodecTests
{
    [TestMethod] [DataRow(1536)] [DataRow(3072)]
    public void Complete_cross_page_batch_preserves_positions_and_exact_float_bits(int dimensions)
    {
        var source = AiKnowledgeIndexPdfPlanTests.Source(6);
        source = source with { ModelId = dimensions == 1536 ? "text-embedding-3-small" : "text-embedding-3-large",
            Chunks = source.Chunks.Select(c => c with { PageNumber = c.Sequence / 3 + 1, Offset = c.Sequence % 3 * c.Length }).ToArray() };
        var plan = AiKnowledgeIndexPdfPlan.Create(source, default)!; var vectors = Vectors(6, dimensions);
        var bytes = AiKnowledgeIndexPdfVectorCodec.Encode(plan, 0, vectors, default);
        var payload = AiKnowledgeIndexPdfVectorCodec.Decode(bytes, default);
        Assert.AreEqual(plan.Identity, payload.Identity); Assert.AreEqual(plan.Summary.PlanHash, payload.PlanHash);
        Assert.AreEqual(plan.Batches[0].SourceHash, payload.SourceHash); Assert.AreEqual(dimensions, payload.Dimensions);
        CollectionAssert.AreEqual(plan.Batches[0].Chunks.ToArray(), payload.Chunks.Select(c => c.Source).ToArray());
        foreach (var item in payload.Chunks) CollectionAssert.AreEqual(vectors[0].Span.ToArray().Select(BitConverter.SingleToInt32Bits).ToArray(), item.Vector.ToArray().Select(BitConverter.SingleToInt32Bits).ToArray());
        var snapshot = AiKnowledgeIndexPdfVectorCodec.ReadBatch(AiKnowledgeIndexPdfManifestCodec.Encode(plan, default), 0, bytes, default);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), snapshot.PayloadHash);
        Assert.AreEqual(2, snapshot.Payload.Chunks[^1].Source.PageNumber); Assert.AreEqual(0, snapshot.Payload.Chunks[3].Source.Offset);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<AiKnowledgePdfEmbeddedChunk>)payload.Chunks)[0] = payload.Chunks[0]);
    }

    [TestMethod]
    public void Later_batch_can_start_at_nonzero_global_sequence_and_nonfirst_page()
    {
        var plan = Plan(17); var bytes = Encode(plan, 1);
        var snapshot = AiKnowledgeIndexPdfVectorCodec.ReadBatch(AiKnowledgeIndexPdfManifestCodec.Encode(plan, default), 1, bytes, default);
        Assert.AreEqual(1, snapshot.Payload.Ordinal); Assert.AreEqual(16, snapshot.Payload.Chunks[0].Source.Sequence);
        Assert.AreEqual(2, snapshot.Payload.Chunks[0].Source.PageNumber);
    }

    [TestMethod]
    public void Fixed_pdf_wire_matches_independent_little_endian_reference()
    {
        var bytes = Encode(Plan());
        Assert.AreEqual(13011, bytes.Length);
        Assert.AreEqual("4A338029C9055F00BB64FB5A1CCBA5D83A113DCB0F823AA5217F5DF17C95575C", Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [TestMethod]
    public void Finite_subnormal_and_extreme_float_bits_are_preserved_without_normalizing()
    {
        var plan = Plan(); var vector = new float[1536]; vector[0] = float.Epsilon;
        var tiny = AiKnowledgeIndexPdfVectorCodec.Decode(AiKnowledgeIndexPdfVectorCodec.Encode(plan, 0, [vector, vector], default), default);
        Assert.AreEqual(BitConverter.SingleToInt32Bits(float.Epsilon), BitConverter.SingleToInt32Bits(tiny.Chunks[0].Vector.Span[0]));
        vector[1] = -0.0f; vector[2] = float.MaxValue; vector[3] = float.MinValue;
        var decoded = AiKnowledgeIndexPdfVectorCodec.Decode(AiKnowledgeIndexPdfVectorCodec.Encode(plan, 0, [vector, vector], default), default);
        CollectionAssert.AreEqual(vector.Select(BitConverter.SingleToInt32Bits).ToArray(), decoded.Chunks[0].Vector.ToArray().Select(BitConverter.SingleToInt32Bits).ToArray());
    }

    [TestMethod] [DataRow(1536)] [DataRow(3072)]
    public void Maximum_unicode_input_batch_is_complete_within_payload_limit(int dimensions)
    {
        var source = AiKnowledgeIndexPdfPlanTests.Source(16, 2000);
        source = source with { ModelId = dimensions == 1536 ? "text-embedding-3-small" : "text-embedding-3-large",
            Chunks = source.Chunks.Select(c => c with { Text = new string('中', 2000), EmbeddingText = new string('中', 2000) }).ToArray() };
        var plan = AiKnowledgeIndexPdfPlan.Create(source, default)!;
        var bytes = AiKnowledgeIndexPdfVectorCodec.Encode(plan, 0, Vectors(16, dimensions), default);
        Assert.IsTrue(bytes.Length <= AiKnowledgeIndexPdfVectorCodec.MaxPayloadBytes);
        var snapshot = AiKnowledgeIndexPdfVectorCodec.ReadBatch(AiKnowledgeIndexPdfManifestCodec.Encode(plan, default), 0, bytes, default);
        Assert.AreEqual(16, snapshot.Payload.Chunks.Count); Assert.AreEqual(32000, snapshot.Payload.Chunks.Sum(c => c.Source.Text.Length));
    }

    [TestMethod]
    public void Encoded_and_decoded_data_are_detached_from_mutable_inputs()
    {
        var plan = Plan(); var vector = Vector(1536);
        var bytes = AiKnowledgeIndexPdfVectorCodec.Encode(plan, 0, [vector, vector], default);
        var expected = vector.Select(BitConverter.SingleToInt32Bits).ToArray(); Array.Clear(vector);
        var snapshot = AiKnowledgeIndexPdfVectorCodec.ReadBatch(AiKnowledgeIndexPdfManifestCodec.Encode(plan, default), 0, bytes, default);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)); Array.Clear(bytes);
        CollectionAssert.AreEqual(expected, snapshot.Payload.Chunks[0].Vector.ToArray().Select(BitConverter.SingleToInt32Bits).ToArray());
        Assert.AreEqual(hash, snapshot.PayloadHash);
        CollectionAssert.AreEqual(Encode(plan), Encode(plan));
    }

    [TestMethod] [DataRow("empty")] [DataRow("count")] [DataRow("short")] [DataRow("long")]
    [DataRow("wrong-model-dimension")] [DataRow("nan")] [DataRow("infinity")] [DataRow("negative-infinity")]
    [DataRow("zero")] [DataRow("negative-ordinal")] [DataRow("missing-ordinal")]
    public void Invalid_vectors_or_batch_selection_never_produce_payload(string fault)
    {
        var plan = Plan(); var vector = Vector(1536); var vectors = Vectors(2, 1536); var ordinal = 0;
        switch (fault)
        {
            case "empty": vectors = []; break; case "count": vectors = Vectors(3, 1536); break;
            case "short": vectors = [Vector(1535), vector]; break; case "long": vectors = [Vector(1537), vector]; break;
            case "wrong-model-dimension": vectors = Vectors(2, 3072); break;
            case "nan": vector[0] = float.NaN; vectors = [vector, vector]; break;
            case "infinity": vector[0] = float.PositiveInfinity; vectors = [vector, vector]; break;
            case "negative-infinity": vector[0] = float.NegativeInfinity; vectors = [vector, vector]; break;
            case "zero": Array.Clear(vector); vectors = [vector, vector]; break;
            case "negative-ordinal": ordinal = -1; break; case "missing-ordinal": ordinal = 1; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexPdfVectorCodec.Encode(plan, ordinal, vectors, default));
    }

    [TestMethod] [DataRow("magic")] [DataRow("format")] [DataRow("plan-format")] [DataRow("knowledge")]
    [DataRow("knowledge-version")] [DataRow("model-version")] [DataRow("provider")] [DataRow("model-name")]
    [DataRow("content-hash")] [DataRow("parser")] [DataRow("tokenizer")] [DataRow("chunker")]
    [DataRow("page-count")] [DataRow("max-tokens")] [DataRow("overlap")] [DataRow("max-chunks")]
    [DataRow("max-total-tokens")] [DataRow("ordinal")] [DataRow("source-hash")]
    [DataRow("dimensions")] [DataRow("count")] [DataRow("sequence")] [DataRow("page")]
    [DataRow("offset")] [DataRow("length")] [DataRow("tokens")] [DataRow("text")]
    [DataRow("embedding")] [DataRow("utf8")] [DataRow("huge-length")] [DataRow("negative-length")]
    [DataRow("truncated")] [DataRow("trailing")] [DataRow("large")] [DataRow("nan")]
    [DataRow("infinity")] [DataRow("zero")] [DataRow("source-hash-legal")]
    public void Corrupt_wire_never_returns_partial_chunks(string fault)
    {
        var bytes = Encode(Plan()); var positions = Positions(bytes);
        switch (fault)
        {
            case "truncated": bytes = bytes[..^1]; break; case "trailing": bytes = [.. bytes, 0]; break;
            case "large": bytes = new byte[AiKnowledgeIndexPdfVectorCodec.MaxPayloadBytes + 1]; break;
            case "utf8": bytes[positions["text"] + 4] = 0xff; break;
            case "huge-length": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(positions["text"]), int.MaxValue); break;
            case "negative-length": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(positions["text"]), -1); break;
            case "nan": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(positions["vector"]), BitConverter.SingleToInt32Bits(float.NaN)); break;
            case "infinity": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(positions["vector"]), BitConverter.SingleToInt32Bits(float.PositiveInfinity)); break;
            case "zero": bytes.AsSpan(positions["vector"], 1536 * sizeof(float)).Clear(); break;
            case "source-hash-legal": var at = positions["source-hash"] + 4; bytes[at] = bytes[at] == (byte)'A' ? (byte)'B' : (byte)'A'; break;
            default:
                if (fault is "magic" or "knowledge-version" or "model-version" or "page-count" or "max-tokens" or "overlap" or "max-chunks" or "max-total-tokens" or "ordinal" or "dimensions" or "count" or "sequence" or "page" or "offset" or "length" or "tokens")
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(positions[fault]), int.MaxValue);
                else bytes[positions[fault] + 4] = (byte)'!';
                break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexPdfVectorCodec.Decode(bytes, default));
    }

    [TestMethod] [DataRow("sequence-gap")] [DataRow("page-gap")] [DataRow("page-back")]
    [DataRow("same-offset")] [DataRow("end-regression")] [DataRow("embedding-context")]
    public void Correctly_hashed_but_impossible_page_positions_are_rejected(string fault)
    {
        var plan = Plan(48); var chunks = plan.Batches[0].Chunks.ToArray(); var chunk = chunks[1];
        chunks[1] = fault switch
        {
            "sequence-gap" => chunk with { Sequence = 3 }, "page-gap" => chunk with { PageNumber = 3, Offset = 0 },
            "page-back" => chunk with { PageNumber = 0 }, "same-offset" => chunk with { Offset = 0 },
            "end-regression" => chunk with { Offset = 1, Length = 1, Text = "x", EmbeddingText = "x" },
            _ => chunk with { EmbeddingText = "context" + chunk.Text }
        };
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(0x3156504b); Text(writer, AiKnowledgeIndexPdfVectorCodec.FormatKey); AiKnowledgeIndexPdfPlan.WriteHeader(writer, plan.Identity);
        Text(writer, plan.Summary.PlanHash); writer.Write(0); Text(writer, AiKnowledgeIndexPdfPlan.ComputeBatchHash(plan.Identity, 0, chunks, default));
        writer.Write(1536); writer.Write(chunks.Length);
        foreach (var item in chunks)
        {
            writer.Write(item.Sequence); writer.Write(item.PageNumber); writer.Write(item.Offset); writer.Write(item.Length); writer.Write(item.TokenCount);
            Text(writer, item.Text); Text(writer, item.EmbeddingText); foreach (var value in Vector(1536)) writer.Write(value);
        }
        writer.Flush(); Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexPdfVectorCodec.Decode(stream.ToArray(), default));
    }

    private static void Text(BinaryWriter writer, string value)
    { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }

    [TestMethod] [DataRow("plan-hash")] [DataRow("different-plan")] [DataRow("model-version")]
    [DataRow("knowledge-version")] [DataRow("tenant")] [DataRow("file")]
    [DataRow("ordinal")] [DataRow("manifest-truncated")] [DataRow("manifest-hash")]
    public void Self_consistent_vectors_still_require_the_expected_complete_manifest(string fault)
    {
        var plan = Plan(17); var bytes = Encode(plan); var manifest = AiKnowledgeIndexPdfManifestCodec.Encode(plan, default); var expectedOrdinal = 0;
        if (fault == "plan-hash") { var p = Positions(bytes)["plan-hash"] + 4; bytes[p] = bytes[p] == (byte)'A' ? (byte)'B' : (byte)'A'; Assert.IsNotNull(AiKnowledgeIndexPdfVectorCodec.Decode(bytes, default)); }
        else if (fault == "different-plan")
        {
            var different = Plan(18); Assert.AreEqual(plan.Batches[0].SourceHash, different.Batches[0].SourceHash);
            bytes = Encode(different);
        }
        else if (fault is "model-version" or "knowledge-version" or "tenant" or "file")
        {
            var source = AiKnowledgeIndexPdfPlanTests.Source(17); source = fault switch
            {
                "model-version" => source with { ModelConfigVersion = 4 }, "knowledge-version" => source with { KnowledgeBaseVersion = 4 },
                "tenant" => source with { TenantId = Guid.Parse("019a2ba0-1234-7000-8000-000000000003") }, _ => source with { ContentHash = new string('b', 64) }
            };
            bytes = Encode(AiKnowledgeIndexPdfPlan.Create(source, default)!);
        }
        else if (fault == "ordinal") expectedOrdinal = 1;
        else if (fault == "manifest-truncated") manifest = manifest[..^1];
        else manifest[^1] = manifest[^1] == (byte)'A' ? (byte)'B' : (byte)'A';
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexPdfVectorCodec.ReadBatch(manifest, expectedOrdinal, bytes, default));
    }

    [TestMethod]
    public void Changed_finite_vector_is_detected_by_actual_payload_hash()
    {
        var plan = Plan(); var bytes = Encode(plan); var manifest = AiKnowledgeIndexPdfManifestCodec.Encode(plan, default);
        var before = AiKnowledgeIndexPdfVectorCodec.ReadBatch(manifest, 0, bytes, default);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(Positions(bytes)["vector"]), BitConverter.SingleToInt32Bits(0.5f));
        var after = AiKnowledgeIndexPdfVectorCodec.ReadBatch(manifest, 0, bytes, default);
        Assert.AreEqual(before.Payload.SourceHash, after.Payload.SourceHash); Assert.AreNotEqual(before.PayloadHash, after.PayloadHash);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), after.PayloadHash);
    }

    [TestMethod] [DataRow("count")] [DataRow("tokens")] [DataRow("sequence")] [DataRow("pages")]
    public void Self_consistent_manifest_metadata_must_match_actual_vector_chunks(string fault)
    {
        var plan = Plan(17); var ordinal = fault == "sequence" ? 1 : 0; var bytes = Encode(plan, ordinal);
        var manifest = AiKnowledgeIndexPdfManifestCodec.Decode(AiKnowledgeIndexPdfManifestCodec.Encode(plan, default), default);
        var batches = manifest.Batches.ToArray(); var summary = manifest.Summary;
        if (fault == "count") { batches[0] = batches[0] with { ChunkCount = 15 }; batches[1] = batches[1] with { FirstSequence = 15, ChunkCount = 2 }; }
        else if (fault == "tokens") { batches[0] = batches[0] with { TokenCount = 31 }; batches[1] = batches[1] with { TokenCount = 3 }; }
        else if (fault == "sequence") { batches[0] = batches[0] with { ChunkCount = 15 }; batches[1] = batches[1] with { FirstSequence = 15 }; summary = summary with { ChunkCount = 16 }; }
        else batches[0] = batches[0] with { LastPageNumber = 2 };
        summary = summary with { PlanHash = AiKnowledgeIndexPdfPlan.ComputePlanHash(manifest.Identity, summary, batches, default) };
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        Text(writer, AiKnowledgeIndexPdfManifestCodec.FormatKey); AiKnowledgeIndexPdfPlan.WriteHeader(writer, manifest.Identity);
        Text(writer, summary.PlanHash); writer.Write(summary.BatchCount); writer.Write(summary.ChunkCount); writer.Write(summary.TokenCount);
        foreach (var item in batches) AiKnowledgeIndexPdfPlan.WriteBatch(writer, item); writer.Flush();
        var changedManifest = stream.ToArray(); Assert.IsNotNull(AiKnowledgeIndexPdfManifestCodec.Decode(changedManifest, default));
        Encoding.ASCII.GetBytes(summary.PlanHash).CopyTo(bytes, Positions(bytes)["plan-hash"] + 4);
        Assert.IsNotNull(AiKnowledgeIndexPdfVectorCodec.Decode(bytes, default));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexPdfVectorCodec.ReadBatch(changedManifest, ordinal, bytes, default));
    }

    [TestMethod]
    public void Both_legacy_vector_and_pdf_vector_formats_reject_each_other()
    {
        var legacy = AiKnowledgeIndexVectorCodec.Encode(AiKnowledgeIndexVectorCodecTests.Sample(), 1536, default);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexPdfVectorCodec.Decode(legacy, default));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexVectorCodec.Decode(Encode(Plan()), default));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexPdfVectorCodec.ReadBatch(
            AiKnowledgeIndexTextManifestCodec.Encode(AiKnowledgeIndexTextPlan.Create(AiKnowledgeIndexTextPlanTests.Source(), default)!, default), 0, Encode(Plan()), default));
    }

    [TestMethod]
    public void Cancellation_and_oversized_collections_precede_allocation_or_enumeration()
    {
        var plan = Plan(); var oversized = new OversizedVectors(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeIndexPdfVectorCodec.Encode(plan, 0, oversized, cancel.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeIndexPdfVectorCodec.Decode(new byte[524289], cancel.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeIndexPdfVectorCodec.ReadBatch([], 0, [], cancel.Token));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexPdfVectorCodec.Encode(plan, 0, oversized, default));
    }

    private static AiKnowledgeIndexPdfPlan Plan(int count = 2) => AiKnowledgeIndexPdfPlan.Create(AiKnowledgeIndexPdfPlanTests.Source(count), default)!;
    private static float[] Vector(int dimensions)
    { var vector = new float[dimensions]; vector[0] = 1; vector[1] = -0.0f; vector[^1] = -0.25f; return vector; }
    private static IReadOnlyList<ReadOnlyMemory<float>> Vectors(int count, int dimensions)
        => Enumerable.Range(0, count).Select(_ => (ReadOnlyMemory<float>)Vector(dimensions)).ToArray();
    private static byte[] Encode(AiKnowledgeIndexPdfPlan plan, int ordinal = 0)
        => AiKnowledgeIndexPdfVectorCodec.Encode(plan, ordinal, Vectors(plan.Batches[ordinal].Chunks.Count, plan.Identity.ModelId == "text-embedding-3-small" ? 1536 : 3072), default);

    private static Dictionary<string, int> Positions(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream); var positions = new Dictionary<string, int>();
        void Integer(string name) { positions[name] = (int)stream.Position; _ = reader.ReadInt32(); }
        void Text(string name) { positions[name] = (int)stream.Position; var length = reader.ReadInt32(); _ = reader.ReadBytes(length); }
        Integer("magic"); Text("format"); Text("plan-format"); Text("knowledge"); Integer("knowledge-version"); Text("tenant");
        Text("model"); Integer("model-version"); Text("provider"); Text("model-name"); Text("content-hash");
        Text("parser"); Text("tokenizer"); Text("chunker"); Integer("page-count"); Integer("max-tokens"); Integer("overlap");
        Integer("max-chunks"); Integer("max-total-tokens"); Text("plan-hash"); Integer("ordinal"); Text("source-hash");
        Integer("dimensions"); Integer("count"); Integer("sequence"); Integer("page"); Integer("offset"); Integer("length"); Integer("tokens");
        Text("text"); Text("embedding"); positions["vector"] = (int)stream.Position; return positions;
    }

    private sealed class OversizedVectors : IReadOnlyList<ReadOnlyMemory<float>>
    {
        public int Count => int.MaxValue;
        public ReadOnlyMemory<float> this[int index] => throw new AssertFailedException("超限或已取消集合不能读取。");
        public IEnumerator<ReadOnlyMemory<float>> GetEnumerator() => throw new AssertFailedException("超限或已取消集合不能枚举。");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
