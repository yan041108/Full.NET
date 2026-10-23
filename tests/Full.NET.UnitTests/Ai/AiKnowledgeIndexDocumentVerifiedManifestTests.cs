using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>共享对象来自完整原字节，不能接收调用方声明的摘要或可变计划。</summary>
[TestClass]
public sealed class AiKnowledgeIndexDocumentVerifiedManifestTests
{
    [TestMethod]
    public void Captured_manifest_survives_caller_mutation_and_verifies_every_batch()
    {
        var plan = AiKnowledgeIndexDocumentCodecTests.Plan();
        var bytes = AiKnowledgeIndexDocumentManifestCodec.Encode(plan, default);
        var verified = AiKnowledgeIndexDocumentVerifiedManifest.Capture(bytes, default);
        Array.Clear(bytes);
        foreach (var batch in plan.Batches)
        {
            var payload = AiKnowledgeIndexDocumentVectorCodec.Encode(plan, batch.Ordinal, AiKnowledgeIndexDocumentCodecTests.Vectors(batch.Chunks.Count), default);
            var restored = AiKnowledgeIndexDocumentVectorCodec.ReadBatch(verified, batch.Ordinal, payload, default);
            Assert.AreEqual(plan.Summary.PlanHash, restored.Payload.PlanHash);
            CollectionAssert.AreEqual(batch.Chunks.ToArray(), restored.Chunks.Select(c => c.Source).ToArray());
        }
    }
    [TestMethod]
    public void Captured_manifest_rejects_vector_from_another_complete_source()
    {
        var plan = AiKnowledgeIndexDocumentCodecTests.Plan();
        var verified = AiKnowledgeIndexDocumentVerifiedManifest.Capture(AiKnowledgeIndexDocumentManifestCodec.Encode(plan, default), default);
        var other = AiKnowledgeIndexDocumentCodecTests.Plan(altered: true);
        var bytes = AiKnowledgeIndexDocumentVectorCodec.Encode(other, 0, AiKnowledgeIndexDocumentCodecTests.Vectors(16), default);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexDocumentVectorCodec.ReadBatch(verified, 0, bytes, default));
    }
}
