using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiKnowledgeRetrievalRuleTests
{
    [TestMethod]
    public void Ranking_combines_text_and_pdf_and_orders_exact_cosine()
    {
        var text = Chunk("text/plain", 1, Vector(1, 1)); var pdf = Chunk("application/pdf", 0, Vector(1, 0));
        var rows = AiKnowledgeVectorRanker.Rank(Vector(1, 0), [text, pdf], default);
        Assert.AreSame(pdf, rows[0].Chunk); Assert.AreEqual(1d, rows[0].Score, 1e-12);
        Assert.AreEqual(Math.Sqrt(.5), rows[1].Score, 1e-12);
    }
    [TestMethod] [DataRow(1536)] [DataRow(3072)]
    public void Ranking_uses_double_accumulation_for_extreme_finite_vectors(int dimensions)
    {
        var v = Enumerable.Repeat(float.MaxValue, dimensions).ToArray();
        Assert.AreEqual(1d, AiKnowledgeVectorRanker.Rank(v, [Chunk("text/plain", 0, v)], default)[0].Score, 1e-12);
    }
    [TestMethod] [DataRow("zero")] [DataRow("nan")] [DataRow("infinity")] [DataRow("dimensions")]
    public void Ranking_rejects_invalid_query(string fault)
    {
        var v = Vector(1, 0);
        if (fault == "zero") v[0] = 0; if (fault == "nan") v[0] = float.NaN;
        if (fault == "infinity") v[0] = float.PositiveInfinity; if (fault == "dimensions") v = [1];
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeVectorRanker.Rank(v, [], default));
    }
    [TestMethod] [DataRow("zero")] [DataRow("nan")] [DataRow("dimensions")] [DataRow("duplicate")] [DataRow("overflow")]
    public void Ranking_refuses_invalid_or_unbounded_candidate_set(string fault)
    {
        var v = Vector(1, 0); if (fault == "zero") v[0] = 0; if (fault == "nan") v[0] = float.NaN;
        if (fault == "dimensions") v = new float[3072];
        var chunk = Chunk("text/plain", 0, v);
        AiKnowledgeRetrievalChunk[] rows = fault == "duplicate" ? [chunk, chunk] : fault == "overflow" ? Enumerable.Repeat(chunk, 257).ToArray() : [chunk];
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeVectorRanker.Rank(Vector(1, 0), rows, default));
    }
    [TestMethod]
    public void Ranking_ties_have_canonical_document_and_sequence_order()
    {
        var first = Chunk("text/plain", 0, Vector(1, 0)) with { Source = Source(Guid.Parse("00000000-0000-7000-8000-000000000001")) };
        var second = Chunk("text/plain", 0, Vector(1, 0)) with { Source = Source(Guid.Parse("00000000-0000-7000-8000-000000000002")) };
        var third = first with { Sequence = 1 };
        CollectionAssert.AreEqual(new[] { first, third, second }, AiKnowledgeVectorRanker.Rank(Vector(1, 0), [second, third, first], default).Select(r => r.Chunk).ToArray());
    }
    [TestMethod]
    public void Ranking_observes_pre_cancelled_work()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeVectorRanker.Rank(Vector(1, 0), [], stop.Token));
    }
    internal static float[] Vector(float first, float second) { var v = new float[1536]; v[0] = first; v[1] = second; return v; }
    internal static AiKnowledgeIndexStatusRecord Source(Guid? document = null) => new()
    { KnowledgeBaseId = Guid.CreateVersion7(), DocumentId = document ?? Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(), TaskId = Guid.CreateVersion7() };
    internal static AiKnowledgeRetrievalChunk Chunk(string type, int sequence, ReadOnlyMemory<float> vector) => new(
        Source() with { ContentType = type }, sequence, type == "application/pdf" ? 1 : null, 0, 2, 1, "证据", vector);
}
