using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>字面搜索的完整候选、稳定排序及 Unicode/资源边界，不替代真实来源授权验证。</summary>
[TestClass]
public sealed class AiKnowledgeKeywordSearchTests
{
    [TestMethod]
    public void Diagnostics_is_assignable_from_the_search_page_authorization_tree()
    {
        var catalog = new Full.NET.Modules.Ai.AiAuthorizationContributor();
        var action = catalog.Actions.Single(row => row.PermissionCode == Full.NET.Modules.Ai.Contracts.AiKnowledgeSearchPermissions.Diagnostics);
        Assert.AreEqual("ai-knowledge-search", action.NavigationId);
    }
    [TestMethod]
    public void All_terms_match_case_independently_and_ties_are_stable()
    {
        var document = Guid.Parse("00000000-0000-7000-8000-000000000001");
        var one = Chunk(document, 0, "事实😀 Alpha beta"); var two = Chunk(document, 1, "alpha BETA alpha");
        var missing = Chunk(document, 2, "alpha");
        var result = AiKnowledgeKeywordRanker.Rank("ALPHA beta ALPHA", [missing, one, two], default);
        Assert.AreEqual(2, result.Count); Assert.AreSame(two, result[0].Chunk); Assert.AreEqual(3d, result[0].Score);
        Assert.AreSame(one, result[1].Chunk); Assert.AreEqual(2d, result[1].Score);
        Assert.AreEqual(1, AiKnowledgeKeywordRanker.Rank("事实😀", [one, two], default).Count);
    }
    [TestMethod]
    public void Equal_scores_use_document_then_sequence_and_zero_matches_are_real()
    {
        var document = Guid.Parse("00000000-0000-7000-8000-000000000001");
        var a = Chunk(document, 0, "事实"); var b = Chunk(document, 1, "事实");
        var c = Chunk(Guid.Parse("00000000-0000-7000-8000-000000000002"), 0, "事实");
        CollectionAssert.AreEqual(new[] { a, b, c }, AiKnowledgeKeywordRanker.Rank("事实", [c, b, a], default).Select(row => row.Chunk).ToArray());
        Assert.AreEqual(0, AiKnowledgeKeywordRanker.Rank("未命中", [a, b, c], default).Count);
    }
    [TestMethod]
    [DataRow("")]
    [DataRow(" \t\r\n")]
    [DataRow("word\0")]
    public void Invalid_queries_fail_before_candidate_read(string query) =>
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeKeywordRanker.Terms(query, default));

    [TestMethod]
    public void Invalid_utf16_is_constructed_without_attribute_metadata_replacement() =>
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeKeywordRanker.Terms(new string((char)0xD800, 1), default));

    [TestMethod]
    public void Query_and_complete_candidate_limits_are_enforced_without_truncation()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeKeywordRanker.Terms(new string('字', 513), default));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeKeywordRanker.Terms(new string('字', 65), default));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeKeywordRanker.Terms(string.Join(' ', Enumerable.Range(0, 17)), default));
        var chunk = Chunk(Guid.CreateVersion7(), 0, "事实");
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeKeywordRanker.Rank("事实", Enumerable.Repeat(chunk, 257).ToArray(), default));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeKeywordRanker.Rank("未命中", [chunk, chunk], default));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeKeywordRanker.Rank("事实", [chunk with { Text = new string('字', 32769) }], default));
    }
    [TestMethod]
    [DataRow(0f)]
    [DataRow(float.NaN)]
    [DataRow(float.PositiveInfinity)]
    public void Keyword_generation_validation_rejects_corrupt_vectors(float value)
    {
        var chunk = Chunk(Guid.CreateVersion7(), 0, "事实"); var values = new float[1536]; values[0] = value;
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeVectorRanker.ValidateCandidates([chunk with { Vector = values }], 1536, default));
    }
    [TestMethod]
    public async Task Cancelled_search_does_not_return_buffered_hits()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Task.Run(() => AiKnowledgeKeywordRanker.Rank("事实", [Chunk(Guid.CreateVersion7(), 0, "事实")], cancelled.Token)));
    }
    private static AiKnowledgeRetrievalChunk Chunk(Guid document, int sequence, string text)
    {
        var vector = new float[1536]; vector[0] = 1;
        return new(new AiKnowledgeIndexStatusRecord { TaskId = document, DocumentId = document, DataClassification = "internal" }, sequence, null, 0, text.Length, text.Length, text, vector);
    }
}
