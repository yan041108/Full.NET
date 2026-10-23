using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>统一计划绑定完整解析输出和原始坐标；部分覆盖或位置替换不得形成可派发批次。</summary>
[TestClass]
public sealed class AiKnowledgeDocumentPlanTests
{
    [TestMethod]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    public void Complete_plan_preserves_every_source_format_and_real_model_input(int formatNumber)
    {
        var source = Source((AiKnowledgeParsedFormat)formatNumber);
        var plan = AiKnowledgeDocumentPlan.Create(source, default);
        Assert.IsNotNull(plan); Assert.AreEqual(source.Document.Format, plan.Source.Document.Format);
        Assert.AreEqual(source.Chunks.Sum(c => c.TokenCount), plan.TokenCount);
        Assert.AreEqual(source.Chunks.Count, plan.Batches.Sum(b => b.Chunks.Count));
        Assert.AreEqual(plan.PlanHash, AiKnowledgeDocumentPlan.Create(source, default)!.PlanHash);
        Assert.AreEqual(64, plan.DocumentHash.Length); Assert.AreEqual(64, plan.PlanHash.Length);
    }

    [TestMethod]
    public void Metadata_and_source_location_change_hash_even_with_same_extracted_text()
    {
        var source = Source(AiKnowledgeParsedFormat.Excel); var block = source.Document.Blocks[0];
        var changed = AiKnowledgeParsedDocument.FromBlocks(source.Document.Format, source.Document.ParserVersion,
            source.Document.ContentHash, source.Document.SizeBytes, [block with { Position = block.Position with { Grid = block.Position.Grid! with { Row = 2 } } }], default)!;
        var next = AiKnowledgeDocumentModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(), AiKnowledgeDocxPlanningTests.Model(), changed, new(), default).Output! with { ParserRuntimeId = AiKnowledgeIndexDocumentCodecTests.RuntimeId };
        var first = AiKnowledgeDocumentPlan.Create(source, default)!; var second = AiKnowledgeDocumentPlan.Create(next, default)!;
        Assert.AreNotEqual(first.DocumentHash, second.DocumentHash); Assert.AreNotEqual(first.PlanHash, second.PlanHash);
        Assert.AreNotEqual(first.Batches[0].SourceHash, second.Batches[0].SourceHash);
    }

    [TestMethod]
    public void Truncated_or_forged_chunks_never_produce_a_complete_plan()
    {
        var source = Source(AiKnowledgeParsedFormat.Csv); var c = source.Chunks[0];
        foreach (var altered in new[] { c with { Text = "伪造" }, c with { Sequence = 1 }, c with { TokenCount = c.TokenCount + 1 },
            c with { Position = c.Position with { Offset = 1 } }, c with { Position = c.Position with { Grid = c.Position.Grid! with { Row = 2 } } } })
            Assert.IsNull(AiKnowledgeDocumentPlan.Create(source with { Chunks = new[] { altered } }, default));
        Assert.IsNull(AiKnowledgeDocumentPlan.Create(source with { Chunks = Array.Empty<AiKnowledgeDocumentChunk>() }, default));
    }

    [TestMethod]
    public void Omitted_last_cell_is_not_hidden_by_valid_first_cell_or_batch_count()
    {
        var source = Source(AiKnowledgeParsedFormat.Csv); var block = source.Document.Blocks[0];
        var two = AiKnowledgeParsedDocument.FromBlocks(source.Document.Format, source.Document.ParserVersion,
            source.Document.ContentHash, source.Document.SizeBytes, [block,
                block with { Sequence = 1, Position = block.Position with { Grid = block.Position.Grid! with { Column = 2 } } }], default)!;
        var full = AiKnowledgeDocumentModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(), AiKnowledgeDocxPlanningTests.Model(), two, new(), default).Output! with { ParserRuntimeId = AiKnowledgeIndexDocumentCodecTests.RuntimeId };
        Assert.IsNotNull(AiKnowledgeDocumentPlan.Create(full, default));
        Assert.IsNull(AiKnowledgeDocumentPlan.Create(full with { Chunks = full.Chunks.Take(1).ToArray() }, default));
    }

    private static AiKnowledgeDocumentModelChunks Source(AiKnowledgeParsedFormat format)
        => AiKnowledgeDocumentModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(), AiKnowledgeDocxPlanningTests.Model(),
            AiKnowledgeParsedWireTests.Fixture(format), new(), default).Output! with { ParserRuntimeId = AiKnowledgeIndexDocumentCodecTests.RuntimeId };
}
