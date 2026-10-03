using Full.NET.AiRetrieval.Probe;

namespace Full.NET.UnitTests.Ai;

/// <summary>选型实验验证有限候选集、向量空间、数值与取消边界，不能以截断冒充完整检索。</summary>
[TestClass]
public sealed class AiRetrievalCandidateTests
{
    [TestMethod]
    public void Synthetic_embedding_is_deterministic_and_normalized()
    {
        var first = ProbeVectorSearch.Embed("合成制度 FN-DEMO-042");
        CollectionAssert.AreEqual(first, ProbeVectorSearch.Embed("合成制度 FN-DEMO-042"));
        Assert.AreEqual(1.0, ProbeVectorSearch.Cosine(first, first), 0.000001);
    }

    [TestMethod]
    public void Candidate_overflow_fails_instead_of_silently_truncating()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => ProbeVectorSearch.Rank([1, 0],
            [new("a", "m", 1, [1, 0]), new("b", "m", 1, [0, 1])], "m", 1, 1, 1));
    }

    [TestMethod]
    [DataRow("other-model", 1)]
    [DataRow("m", 2)]
    public void Same_dimension_does_not_allow_other_model_or_generation(string model, int generation)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => ProbeVectorSearch.Rank([1, 0],
            [new("a", model, generation, [1, 0])], "m", 1, 2, 1));
    }

    [TestMethod]
    public void Invalid_dimension_and_non_finite_vector_are_rejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => ProbeVectorSearch.Cosine([1, 0], [1]));
        Assert.ThrowsExactly<InvalidDataException>(() => ProbeVectorSearch.Cosine([1, 0], [float.NaN, 0]));
    }

    [TestMethod]
    public void Rank_breaks_equal_scores_by_stable_chunk_id()
    {
        CollectionAssert.AreEqual(new[] { "a", "b" }, ProbeVectorSearch.Rank([1, 0],
            [new("b", "m", 1, [1, 0]), new("a", "m", 1, [1, 0])], "m", 1, 2, 2));
    }

    [TestMethod]
    public void Cancelled_search_does_not_return_partial_hits()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => ProbeVectorSearch.Rank([1, 0],
            [new("a", "m", 1, [1, 0])], "m", 1, 2, 1, cancellation.Token));
    }
}
