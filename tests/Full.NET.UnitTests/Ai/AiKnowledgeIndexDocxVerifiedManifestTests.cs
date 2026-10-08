using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>共享对象来自完整原字节，不能接收调用方声明的摘要或可变计划。</summary>
[TestClass]
public sealed class AiKnowledgeIndexDocxVerifiedManifestTests
{
    [TestMethod]
    public void Captured_manifest_survives_caller_mutation_and_verifies_every_batch()
    {
        var plan = AiKnowledgeIndexDocxCodecTests.Plan();
        var bytes = AiKnowledgeIndexDocxManifestCodec.Encode(plan, default);
        var verified = AiKnowledgeIndexDocxVerifiedManifest.Capture(bytes, default);
        Array.Clear(bytes);
        foreach (var batch in plan.Batches)
        {
            var payload = AiKnowledgeIndexDocxVectorCodec.Encode(plan, batch.Ordinal, AiKnowledgeIndexDocxCodecTests.Vectors(batch.Chunks.Count), default);
            var restored = AiKnowledgeIndexDocxVectorCodec.ReadBatch(verified, batch.Ordinal, payload, default);
            Assert.AreEqual(plan.Summary.PlanHash, restored.Payload.PlanHash);
            CollectionAssert.AreEqual(batch.Chunks.ToArray(), restored.Payload.Chunks.Select(c => c.Source).ToArray());
        }
    }
    [TestMethod]
    public void Captured_manifest_rejects_vector_from_another_complete_source()
    {
        var plan = AiKnowledgeIndexDocxCodecTests.Plan();
        var verified = AiKnowledgeIndexDocxVerifiedManifest.Capture(AiKnowledgeIndexDocxManifestCodec.Encode(plan, default), default);
        var other = AiKnowledgeIndexDocxCodecTests.Plan(altered: true);
        var bytes = AiKnowledgeIndexDocxVectorCodec.Encode(other, 0, AiKnowledgeIndexDocxCodecTests.Vectors(16), default);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexDocxVectorCodec.ReadBatch(verified, 0, bytes, default));
    }
}
