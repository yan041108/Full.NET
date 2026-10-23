using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>同一个真实词表对各来源格式保持完整块覆盖、坐标和总预算；不调用外部模型。</summary>
[TestClass]
public sealed class AiKnowledgeDocumentModelChunkerTests
{
    [TestMethod]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    public void Parsed_format_and_exact_position_survive_real_model_chunking(int formatNumber)
    {
        var source = AiKnowledgeParsedWireTests.Fixture((AiKnowledgeParsedFormat)formatNumber);
        var result = AiKnowledgeDocumentModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(),
            AiKnowledgeDocxPlanningTests.Model(), source, new(), default);
        Assert.IsTrue(result.IsSuccess, result.Failure + ":" + result.ChunkFailure);
        Assert.AreEqual(source.Format, result.Output!.Document.Format);
        var chunk = result.Output.Chunks.Single();
        Assert.AreEqual(source.Blocks[0].Position, chunk.Position);
        Assert.AreEqual(source.Blocks[0].Text, chunk.Text);
        Assert.AreEqual(AiKnowledgeCl100kTokenizer.Get(default).CountTokens(chunk.Text), chunk.TokenCount);
    }

    [TestMethod]
    public void Low_confidence_ocr_remains_available_for_correction_but_has_no_indexable_chunks()
    {
        var source = AiKnowledgeParsedWireTests.Fixture(AiKnowledgeParsedFormat.Image); var block = source.Blocks[0];
        var uncertain = AiKnowledgeParsedDocument.FromBlocks(source.Format, source.ParserVersion, source.ContentHash, source.SizeBytes,
            [block with { Position = block.Position with { Image = block.Position.Image! with { Confidence = 0.79 } } }], default);
        Assert.IsNotNull(uncertain);
        var result = AiKnowledgeDocumentModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(),
            AiKnowledgeDocxPlanningTests.Model(), uncertain, new(), default);
        Assert.IsNull(result.Output); Assert.AreEqual(AiKnowledgeChunkFailure.UncertainSource, result.ChunkFailure);
    }

    [TestMethod]
    public void A_second_cell_cannot_reset_document_token_or_chunk_budget()
    {
        var source = AiKnowledgeParsedWireTests.Fixture(AiKnowledgeParsedFormat.Csv); var block = source.Blocks[0];
        var two = AiKnowledgeParsedDocument.FromBlocks(source.Format, source.ParserVersion, source.ContentHash, source.SizeBytes,
            [block, block with { Sequence = 1, Position = block.Position with { Grid = block.Position.Grid! with { Column = 2 } } }], default)!;
        var full = AiKnowledgeDocumentModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(), AiKnowledgeDocxPlanningTests.Model(), two, new(), default);
        Assert.IsTrue(full.IsSuccess); Assert.AreEqual(2, full.Output!.Chunks.Count);
        var bounded = AiKnowledgeDocumentModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(), AiKnowledgeDocxPlanningTests.Model(), two, new(MaxChunks: 1), default);
        Assert.IsNull(bounded.Output); Assert.AreEqual(AiKnowledgeChunkFailure.TooManyChunks, bounded.ChunkFailure);
        Assert.AreEqual(2, full.Output.Document.Blocks.Count);
    }

    [TestMethod]
    public void Source_formats_do_not_change_model_approval_or_tenant_rules()
    {
        var source = AiKnowledgeParsedWireTests.Fixture(AiKnowledgeParsedFormat.Excel);
        var rejected = AiKnowledgeDocumentModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(approvedVersion: 2),
            AiKnowledgeDocxPlanningTests.Model(), source, new(), default);
        Assert.IsNull(rejected.Output); Assert.AreEqual(AiKnowledgeModelChunkFailure.ModelNotApproved, rejected.Failure);
        rejected = AiKnowledgeDocumentModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(Guid.NewGuid()),
            AiKnowledgeDocxPlanningTests.Model(Guid.NewGuid()), source, new(), default);
        Assert.IsNull(rejected.Output); Assert.AreEqual(AiKnowledgeModelChunkFailure.ModelScopeMismatch, rejected.Failure);
    }
}
