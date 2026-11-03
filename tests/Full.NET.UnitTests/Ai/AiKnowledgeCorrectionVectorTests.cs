using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>人工向量只从完整清单恢复来源；核对真实 float 位、来源身份和派发指纹。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionVectorTests
{
    [TestMethod] [DataRow("text-embedding-3-small", 1536)] [DataRow("text-embedding-3-large", 3072)]
    public void Vector_roundtrip_preserves_bits_and_source_sets_without_aliasing(string model, int dimensions)
    {
        var plan = Plan(model); var vectors = Vectors(plan); var bytes = AiKnowledgeCorrectionVectorCodec.Encode(plan, 0, vectors, default);
        var first = AiKnowledgeCorrectionVectorCodec.ReadBatch(plan, 0, bytes, default);
        Assert.AreEqual(dimensions, first.Dimensions); Assert.AreEqual(plan.PlanHash, first.PlanHash);
        Assert.AreEqual(2, first.Chunks[0].Source.References.Count); Assert.IsNull(first.Chunks[0].Source.References[1].Position);
        for (var i = 0; i < dimensions; i++) Assert.AreEqual(BitConverter.SingleToInt32Bits(vectors[0].Span[i]), BitConverter.SingleToInt32Bits(first.Chunks[0].Vector.Span[i]));
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), first.PayloadHash);
        var repeated = AiKnowledgeCorrectionVectorCodec.ReadBatch(plan, 0, bytes, default);
        Assert.IsTrue(MemoryMarshal.TryGetArray(vectors[0], out var input)); input.Array![input.Offset] = 2;
        Assert.AreEqual(1f, first.Chunks[0].Vector.Span[0]);
        Assert.IsTrue(MemoryMarshal.TryGetArray(first.Chunks[0].Vector, out var decoded)); decoded.Array![decoded.Offset] = 3;
        Assert.AreEqual(1f, repeated.Chunks[0].Vector.Span[0]);
        CollectionAssert.AreEqual(first.Chunks[0].Source.References.ToArray(), repeated.Chunks[0].Source.References.ToArray());
    }

    [TestMethod]
    public void Vector_rejects_wrong_revision_truncation_order_nan_and_trailing_bytes()
    {
        var plan = Plan(); var bytes = AiKnowledgeCorrectionVectorCodec.Encode(plan, 0, Vectors(plan), default);
        var changed = AiKnowledgeCorrectionIndexPlan.Create(plan.Snapshot with { Identity = plan.Snapshot.Identity with { RevisionId = Guid.CreateVersion7() } }, plan.Model, default)!;
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeCorrectionVectorCodec.ReadBatch(changed, 0, bytes, default));
        var sequence = bytes.ToArray(); using (var stream = new MemoryStream(sequence))
        using (var reader = new BinaryReader(stream))
        { reader.ReadInt32(); AiKnowledgeIndexDocumentManifestCodec.Text(reader); AiKnowledgeIndexDocumentManifestCodec.Text(reader); reader.ReadInt32(); AiKnowledgeIndexDocumentManifestCodec.Text(reader); reader.ReadInt32(); reader.ReadInt32(); BinaryPrimitives.WriteInt32LittleEndian(sequence.AsSpan((int)stream.Position), 42); }
        var nan = bytes.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(nan.AsSpan(nan.Length - 4), BitConverter.SingleToInt32Bits(float.NaN));
        foreach (var invalid in new[] { bytes[..7], bytes[..^1], bytes.Concat(new byte[1]).ToArray(), sequence, nan })
            Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeCorrectionVectorCodec.ReadBatch(plan, 0, invalid, default));
    }

    [TestMethod]
    public void Vector_rejects_partial_dimensions_zero_and_nonfinite_inputs()
    {
        var plan = Plan(); var dimensions = AiKnowledgeCorrectionVectorCodec.Dimensions(plan);
        foreach (var vector in new[] { new float[dimensions - 1], new float[dimensions], Bad(float.NaN), Bad(float.PositiveInfinity) })
            Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeCorrectionVectorCodec.Encode(plan, 0, [vector], default));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeCorrectionVectorCodec.Encode(plan, 0, [], default));
        float[] Bad(float value) { var result = new float[dimensions]; result[0] = value; return result; }
    }

    [TestMethod]
    public void Budget_fingerprint_binds_revision_even_when_model_text_and_limits_are_equal()
    {
        var plan = Plan(); var model = new AiModelConfigRecord { Id = plan.Model.ModelConfigId, Version = 1, ModelId = plan.Model.ModelId,
            ProviderKey = "openai_compatible", EndpointBaseUrl = "https://api.openai.com/v1", IsEnabled = true };
        var changed = AiKnowledgeCorrectionIndexPlan.Create(plan.Snapshot with { Identity = plan.Snapshot.Identity with { RevisionId = Guid.CreateVersion7() } }, plan.Model, default)!;
        var first = AiKnowledgeCorrectionBudgetRequest.Create(Guid.CreateVersion7(), model, plan, 0);
        var next = AiKnowledgeCorrectionBudgetRequest.Create(Guid.CreateVersion7(), model, changed, 0);
        Assert.AreEqual(first.InputTokenLimit, next.InputTokenLimit); Assert.AreNotEqual(first.RequestHash, next.RequestHash);
        Assert.AreNotEqual(AiOperationBudgetStore.Fingerprint(first), AiOperationBudgetStore.Fingerprint(next));
        Assert.AreEqual(AiOperationBudgetStore.Fingerprint(first), AiOperationBudgetStore.Fingerprint(first with { OperationId = Guid.CreateVersion7() }));
    }

    [TestMethod] [DataRow(DatabaseProvider.SqlServer, false)] [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, false)] [DataRow(DatabaseProvider.MySql, true)]
    public void Sql_preserves_current_scope_settlement_manifest_hash_and_real_payload_guards(DatabaseProvider provider, bool tenant)
    {
        var write = AiKnowledgeCorrectionVectorSql.Write(provider, tenant); var read = AiKnowledgeCorrectionVectorSql.Read(provider, tenant);
        Assert.AreEqual(tenant ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, write.Scope);
        StringAssert.Contains(write.Text, "knowledge-correction-manifest-v1"); StringAssert.Contains(read.Text, "knowledge-correction-vector-v1");
        StringAssert.Contains(write.Text, "ledger.SettledAtUtc IS NOT NULL"); StringAssert.Contains(read.Text, "@DocumentManifestHash");
        StringAssert.Contains(read.Text, "CorrectionRevisionId IS NOT NULL"); StringAssert.Contains(read.Text, "524288");
        Assert.IsFalse(read.Text.Contains("knowledge-document-manifest-v1", StringComparison.Ordinal));
        if (provider == DatabaseProvider.MySql) { Assert.IsTrue(write.Text.EndsWith(" FOR SHARE", StringComparison.Ordinal)); StringAssert.Contains(read.Text, "SHA2(vector.Payload,256)"); }
        else StringAssert.Contains(read.Text, "HASHBYTES('SHA2_256',vector.Payload)");
    }

    [TestMethod]
    public void Vector_capture_uses_one_bounded_count_and_never_enumerates_a_supplier_collection()
    {
        var plan = Plan(); var values = new OneReadVectors(Vectors(plan));
        var bytes = AiKnowledgeCorrectionVectorCodec.Encode(plan, 0, values, default);
        Assert.AreEqual(1, values.CountReads); Assert.AreEqual(1, AiKnowledgeCorrectionVectorCodec.ReadBatch(plan, 0, bytes, default).Chunks.Count);
    }

    [TestMethod]
    public void Cancelled_codec_never_produces_a_partial_payload()
    {
        var plan = Plan(); var bytes = AiKnowledgeCorrectionVectorCodec.Encode(plan, 0, Vectors(plan), default);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeCorrectionVectorCodec.Encode(plan, 0, Vectors(plan), cancelled.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeCorrectionVectorCodec.ReadBatch(plan, 0, bytes, cancelled.Token));
    }

    private static AiKnowledgeCorrectionIndexPlan Plan(string model = "text-embedding-3-small")
    {
        const string text = "原始识别文字"; const string runtime = "managed:test-v1"; var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var source = AiKnowledgeParsedDocument.FromText(new(text, hash, Encoding.UTF8.GetByteCount(text)), AiKnowledgeTextFormat.PlainText, default)!;
        var draft = AiKnowledgeCorrectionDocument.Create(source, Guid.CreateVersion7, default); var block = draft.Blocks[0];
        draft = draft.Replace(block.Id, block.Parts[0].Id, "人工校对后的文字", default).Append(block.Id, "人工补充", Guid.CreateVersion7, default);
        draft = draft.Merge(block.Id, draft.Blocks[1].Id, default); var wire = AiKnowledgeCorrectionWireCodec.Encode(draft, runtime, default);
        var snapshot = new AiKnowledgeCorrectionSnapshot(new(Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
            AiKnowledgeCorrectionWireCodec.SourceHash(source, runtime, default), Convert.ToHexStringLower(SHA256.HashData(wire))), runtime, draft);
        return AiKnowledgeCorrectionIndexPlan.Create(snapshot, new(Guid.CreateVersion7(), 1, null, Guid.CreateVersion7(), 1,
            "openai_compatible", model, "internal", new string('a', 64)), default)!;
    }
    private static ReadOnlyMemory<float>[] Vectors(AiKnowledgeCorrectionIndexPlan plan) => plan.Batches[0].Chunks.Select(_ =>
    { var vector = new float[AiKnowledgeCorrectionVectorCodec.Dimensions(plan)]; vector[0] = 1; vector[1] = float.Epsilon; vector[2] = -0.0f; vector[3] = -0.5f; return new ReadOnlyMemory<float>(vector); }).ToArray();
    private sealed class OneReadVectors(ReadOnlyMemory<float>[] values) : IReadOnlyList<ReadOnlyMemory<float>>
    {
        internal int CountReads;
        public int Count => ++CountReads == 1 ? values.Length : throw new InvalidOperationException("Supplier count changed after validation.");
        public ReadOnlyMemory<float> this[int index] => values[index];
        public IEnumerator<ReadOnlyMemory<float>> GetEnumerator() => throw new InvalidOperationException("Supplier enumeration is forbidden.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
