using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>原始结构而非序号决定关系；人工片段和独立补全预算保持完整。</summary>
[TestClass]
public sealed class AiKnowledgeSourceRelationsTests
{
    [TestMethod] [DataRow(0)] [DataRow(17)]
    public void Source_relations_follow_proved_parent_across_unrelated_neighbors(int anchor)
    {
        var chunks = Chunks(); var result = AiKnowledgeSourceRelations.Select(chunks.Reverse().ToArray(), anchor, default);
        Assert.AreEqual("complete", result.State); CollectionAssert.AreEqual(new[] { 0, 17 }, result.Blocks.Select(block => block.Sequence).ToArray());
        var edge = result.Relations.Single(); Assert.AreEqual(0, edge.ParentSequence); Assert.AreEqual(17, edge.ChildSequence);
        Assert.AreEqual(edge.ParentLocation, edge.ChildLocation.Image!.Parent);
        Assert.IsTrue(result.Blocks.All(block => block.Vector.IsEmpty));
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Source_relations_do_not_infer_neighbors_or_standalone_images(bool image)
    {
        var chunks = Chunks(); if (image) chunks[1] = chunks[1] with { SourceLocation = Image(2, null) };
        var result = AiKnowledgeSourceRelations.Select(chunks, 1, default);
        Assert.AreEqual("none", result.State); Assert.AreEqual(0, result.Relations.Count); Assert.AreEqual(1, result.Blocks.Count);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Source_relations_missing_or_different_parent_never_fabricates_parent_body(bool different)
    {
        var chunks = Chunks(); chunks[0] = chunks[0] with { SourceLocation = different ? Parent(0, 2) : null };
        var result = AiKnowledgeSourceRelations.Select(chunks, 17, default);
        Assert.AreEqual("parent_not_indexed", result.State); Assert.IsNull(result.Relations.Single().ParentSequence);
        Assert.AreEqual(17, result.Blocks.Single().Sequence);
    }
    [TestMethod] [DataRow(0.25)] [DataRow(1.0)]
    public void Source_relations_preserve_both_ends_inside_a_reviewed_merged_chunk(double confidence)
    {
        var chunk = Chunks()[0]; var parent = Parent(0); var child = Image(1, parent, confidence);
        chunk = chunk with { Text = "父子", SourceLocation = null, Correction = new(Guid.CreateVersion7(), 2,
            [new("corrected", 0, 1, parent, 0, 1), new("corrected", 1, 1, child, 0, 1)]) };
        var result = AiKnowledgeSourceRelations.Select([chunk], 0, default);
        Assert.AreEqual("complete", result.State); Assert.AreEqual(0, result.Relations.Single().ParentSequence);
        Assert.AreEqual(0, result.Relations.Single().ChildSequence); Assert.AreEqual(2, result.Blocks.Single().Correction!.Spans.Count);
    }
    [TestMethod]
    public void Source_relations_supplements_have_no_parent_provenance()
    {
        var chunk = Chunks()[0] with { SourceLocation = null, Correction = new(Guid.CreateVersion7(), 2, [new("supplement", 0, 2, null, null, null)]) };
        var result = AiKnowledgeSourceRelations.Select([chunk], 0, default); Assert.AreEqual("none", result.State);
    }
    [TestMethod] [DataRow("gap")] [DataRow("duplicate")] [DataRow("document")] [DataRow("publication")]
    [DataRow("manifest")] [DataRow("location")] [DataRow("backward")]
    public void Source_relations_reject_corrupt_or_mixed_candidates(string fault)
    {
        var chunks = Chunks();
        if (fault == "gap") chunks[1] = chunks[1] with { Sequence = 80 };
        if (fault == "duplicate") chunks[1] = chunks[1] with { Sequence = 0 };
        if (fault == "document") chunks[1] = chunks[1] with { Source = chunks[1].Source with { DocumentId = Guid.CreateVersion7() } };
        if (fault == "publication") chunks[1] = chunks[1] with { Source = chunks[1].Source with { PublicationId = Guid.CreateVersion7() } };
        if (fault == "manifest") chunks[1] = chunks[1] with { Source = chunks[1].Source with { ActiveManifestHash = new string('f', 64) } };
        if (fault == "location") chunks[1] = chunks[1] with { SourceLocation = Parent(0) with { Kind = "unknown" } };
        if (fault == "backward") chunks[17] = chunks[17] with { SourceLocation = Image(1, Parent(2)) };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeSourceRelations.Select(chunks, 0, default));
    }
    [TestMethod] [DataRow("blocks")] [DataRow("characters")]
    public void Source_relations_reject_entire_expansion_when_independent_budget_is_exceeded(string fault)
    {
        var chunks = Chunks();
        if (fault == "characters")
        { chunks[0] = chunks[0] with { Text = new string('父', 17000) }; chunks[17] = chunks[17] with { Text = new string('子', 17000) }; }
        else for (var index = 1; index <= 5; index++) chunks[index] = chunks[index] with { SourceLocation = Image(index, Parent(0)) };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeSourceRelations.Select(chunks, 0, default));
    }
    [TestMethod]
    public void Source_relations_reject_excessive_proof_edges_without_dropping_any()
    {
        var source = Chunks()[0]; var chunks = new AiKnowledgeRetrievalChunk[5];
        for (var index = 0; index < chunks.Length; index++)
        {
            var child = index >= 3;
            var spans = Enumerable.Range(0, 32).Select(offset => new AiKnowledgeCorrectionSpanResponse("corrected", offset, 1,
                child ? Image(32 + offset, Parent(offset, offset + 1)) : Parent(offset, offset + 1), 0, 1)).ToArray();
            chunks[index] = source with { Sequence = index, Text = new string('字', 32), SourceLocation = null,
                Correction = new(Guid.CreateVersion7(), 32, Array.AsReadOnly(spans)) };
        }
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeSourceRelations.Select(chunks, 0, default));
    }
    [TestMethod]
    public void Source_relations_honor_cancellation_before_releasing_bodies()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeSourceRelations.Select(Chunks(), 0, cancelled.Token));
    }
    private static AiKnowledgeSourceLocationResponse Parent(int block, int paragraph = 1) => new(block, "docx_paragraph", null, paragraph, null, null, null, null, null, null, null);
    private static AiKnowledgeSourceLocationResponse Image(int block, AiKnowledgeSourceLocationResponse? parent, double confidence = 1) =>
        new(block, "image_region", null, null, null, null, null, null, null, null, new(1, 100, 100, 0, 0, 100, 100, confidence, null, null, null, parent));
    private static AiKnowledgeRetrievalChunk[] Chunks()
    {
        var source = new AiKnowledgeIndexStatusRecord { DocumentId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(), TaskId = Guid.CreateVersion7(), PublicationId = Guid.CreateVersion7() };
        var chunks = Enumerable.Range(0, 18).Select(index => new AiKnowledgeRetrievalChunk(source, index, null, 0, 2, 2, "正文", new float[1536])).ToArray();
        chunks[0] = chunks[0] with { SourceLocation = Parent(0) }; chunks[17] = chunks[17] with { SourceLocation = Image(1, Parent(0)) }; return chunks;
    }
}
