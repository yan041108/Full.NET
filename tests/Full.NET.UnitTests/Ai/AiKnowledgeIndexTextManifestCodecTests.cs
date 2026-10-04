using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiKnowledgeIndexTextManifestCodecTests
{
    [TestMethod] [DataRow(1)] [DataRow(17)] [DataRow(2048)]
    public void Whole_plan_round_trips_without_document_body(int count)
    {
        var source = AiKnowledgeIndexTextPlanTests.Source(count, 20);
        var plan = AiKnowledgeIndexTextPlan.Create(source, default)!;
        var bytes = AiKnowledgeIndexTextManifestCodec.Encode(plan, default);
        Assert.IsTrue(bytes.Length is > 8 and <= AiKnowledgeIndexTextManifestCodec.MaxPayloadBytes);
        var manifest = AiKnowledgeIndexTextManifestCodec.Decode(bytes, default);
        Assert.AreEqual(plan.Summary, manifest.Summary); Assert.AreEqual(source.KnowledgeBaseId, manifest.Identity.KnowledgeBaseId);
        Assert.AreEqual(source.ModelConfigId, manifest.Identity.ModelConfigId); Assert.AreEqual(source.TenantId, manifest.Identity.TenantId);
        Assert.AreEqual(count, manifest.Batches.Sum(b => b.ChunkCount)); Assert.AreEqual(plan.Summary.TokenCount, manifest.Batches.Sum(b => b.TokenCount));
        CollectionAssert.AreEqual(plan.Batches.Select(b => b.SourceHash).ToArray(), manifest.Batches.Select(b => b.SourceHash).ToArray());
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(bytes).Contains(new string('x', 20), StringComparison.Ordinal));
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<AiKnowledgeIndexTextManifestBatch>)manifest.Batches)[0] = manifest.Batches[0]);
        CollectionAssert.AreEqual(bytes, AiKnowledgeIndexTextManifestCodec.Encode(plan, default));
    }

    [TestMethod] [DataRow("empty")] [DataRow("truncated")] [DataRow("trailing")] [DataRow("large")] [DataRow("hash")]
    public void Malformed_or_changed_manifest_never_decodes(string fault)
    {
        var bytes = AiKnowledgeIndexTextManifestCodec.Encode(AiKnowledgeIndexTextPlan.Create(AiKnowledgeIndexTextPlanTests.Source(17), default)!, default);
        bytes = fault switch { "empty" => [], "truncated" => bytes[..^1], "trailing" => [..bytes, 0], "large" => new byte[AiKnowledgeIndexTextManifestCodec.MaxPayloadBytes + 1], _ => ChangeHash(bytes) };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexTextManifestCodec.Decode(bytes, default));
    }

    [TestMethod]
    public void Cancelled_encoding_and_decoding_propagate_without_allocating()
    {
        var plan = AiKnowledgeIndexTextPlan.Create(AiKnowledgeIndexTextPlanTests.Source(), default)!;
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeIndexTextManifestCodec.Encode(plan, cancel.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeIndexTextManifestCodec.Decode(new byte[AiKnowledgeIndexTextManifestCodec.MaxPayloadBytes + 1], cancel.Token));
    }

    private static byte[] ChangeHash(byte[] bytes)
    {
        // 批次摘要最后一个十六进制字符改变，保持合法编码形状，必须由整份计划摘要拒绝。
        var copy = bytes.ToArray(); copy[^1] = copy[^1] == (byte)'A' ? (byte)'B' : (byte)'A'; return copy;
    }
}
