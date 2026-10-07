using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>完整原文和空白块必须可恢复，不能只保存摘要列表。</summary>
[TestClass]
public sealed class AiKnowledgeIndexDocxCodecTests
{
    internal static AiKnowledgeIndexDocxPlan Plan(int count = 17, Guid? tenant = null, bool altered = false)
    {
        var doc = AiKnowledgeDocxPlanningTests.Document(Enumerable.Range(0, count).Select(n => $"{(altered ? "新文" : "正文")} {n} 😀").ToArray());
        var chunks = AiKnowledgeDocxModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(tenant), AiKnowledgeDocxPlanningTests.Model(tenant), doc, new(), default);
        return AiKnowledgeIndexDocxPlan.Create(chunks.Output!, default)!;
    }

    [TestMethod]
    public void Complete_plan_roundtrip_preserves_source_coordinates_and_batches()
    {
        var plan = Plan(); var bytes = AiKnowledgeIndexDocxManifestCodec.Encode(plan, default);
        var restored = AiKnowledgeIndexDocxManifestCodec.Decode(bytes, default);
        Assert.AreEqual(plan.Summary, restored.Summary); Assert.AreEqual(plan.Source.Document.Text, restored.Source.Document.Text);
        CollectionAssert.AreEqual(plan.Source.Document.Blocks.ToArray(), restored.Source.Document.Blocks.ToArray());
        CollectionAssert.AreEqual(plan.Source.Chunks.ToArray(), restored.Source.Chunks.ToArray());
        CollectionAssert.AreEqual(plan.Batches.Select(b => b.SourceHash).ToArray(), restored.Batches.Select(b => b.SourceHash).ToArray());
    }

    [TestMethod]
    public void Vector_batch_roundtrip_preserves_original_ranges_and_ieee_values()
    {
        var plan = Plan(); var vectors = Vectors(plan.Batches[0].Chunks.Count);
        var bytes = AiKnowledgeIndexDocxVectorCodec.Encode(plan, 0, vectors, default);
        var snapshot = AiKnowledgeIndexDocxVectorCodec.ReadBatch(AiKnowledgeIndexDocxManifestCodec.Encode(plan, default), 0, bytes, default);
        CollectionAssert.AreEqual(plan.Batches[0].Chunks.ToArray(), snapshot.Payload.Chunks.Select(c => c.Source).ToArray());
        Assert.AreEqual(plan.Source.Document.Blocks[0], snapshot.Payload.Chunks[0].Block);
        Assert.AreEqual(int.MinValue, BitConverter.SingleToInt32Bits(snapshot.Payload.Chunks[0].Vector.Span[1]));
        Assert.AreEqual(plan.Summary.PlanHash, snapshot.Payload.PlanHash);
    }

    internal static ReadOnlyMemory<float>[] Vectors(int count, int dimensions = 1536) => Enumerable.Range(0, count).Select(n =>
    { var vector = new float[dimensions]; vector[0] = n + 1; vector[1] = BitConverter.Int32BitsToSingle(int.MinValue); return new ReadOnlyMemory<float>(vector); }).ToArray();
}
