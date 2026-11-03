using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>搜索选择贯通收费问答；形状与来源冻结不授予授权，也不改变已有用量语义。</summary>
public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    private static AiKnowledgeSearchAnswerScope SearchAnswerScope(PaidAnswerFixture f) =>
        new([f.R.F.Binding.DocumentId], f.R.Model.Version, [ContextSource(f.R.Query.Rows.Single())]);

    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public async Task Search_answer_retains_frozen_scope_all_dependencies_and_usage(bool tenant, bool unknown)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); if (unknown) f.Factory.Usage = null;
        var scope = SearchAnswerScope(f);
        var answer = await f.Service.ExecuteAsync([f.R.F.Binding.KnowledgeBaseId], f.R.Actor, f.R.Model, f.Generation, "问题", default, scope);
        CollectionAssert.AreEqual(scope.DocumentIds.ToArray(), answer.Prepared.Retrieval.SearchDocumentIds!.ToArray());
        CollectionAssert.AreEqual(scope.Sources.ToArray(), answer.Prepared.Retrieval.ExpectedSearchSources!.ToArray());
        Assert.AreEqual(17, answer.Prepared.Retrieval.Chunks.Count); Assert.AreEqual(1, answer.Answer.Citations.Count);
        Assert.AreEqual(2, f.Budget.Requests.Count); Assert.AreEqual(2, f.Budget.Settlements.Count);
        Assert.AreNotEqual(answer.EmbeddingOperationId, answer.GenerationOperationId);
        Assert.AreEqual(unknown ? null : 7, f.Budget.Settlements[0].Usage.InputTokens);
        Assert.AreEqual(unknown ? null : 3, f.Budget.Settlements[1].Usage.OutputTokens);
        await f.Service.RevalidateDeliveryAsync(answer, default);
    }
    [TestMethod] [DataRow("publication")] [DataRow("generation")] [DataRow("manifest")] [DataRow("documentVersion")]
    [DataRow("knowledgeVersion")] [DataRow("fileVersion")] [DataRow("modelVersion")]
    public async Task Search_answer_source_drift_is_rejected_before_budget_or_payload(string field)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var scope = SearchAnswerScope(f); var source = scope.Sources.Single();
        source = field switch { "publication" => source with { PublicationId = Guid.CreateVersion7() },
            "generation" => source with { GenerationId = Guid.CreateVersion7() }, "manifest" => source with { ManifestHash = new('A', 64) },
            "documentVersion" => source with { DocumentVersion = source.DocumentVersion + 1 },
            "knowledgeVersion" => source with { KnowledgeBaseVersion = source.KnowledgeBaseVersion + 1 },
            "fileVersion" => source with { VersionId = Guid.CreateVersion7() }, _ => source };
        scope = scope with { Sources = [source], EmbeddingModelVersion = field == "modelVersion" ? scope.EmbeddingModelVersion + 1 : scope.EmbeddingModelVersion };
        var before = f.R.F.PayloadReads;
        await Assert.ThrowsExactlyAsync<AiKnowledgeSearchSourceChangedException>(() => f.Service.ExecuteAsync([f.R.F.Binding.KnowledgeBaseId],
            f.R.Actor, f.R.Model, f.Generation, "问题", default, scope));
        Assert.AreEqual(0, f.Budget.Requests.Count); Assert.AreEqual(0, f.Budget.Settlements.Count);
        Assert.AreEqual(0, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls);
        Assert.AreEqual(0, f.R.Query.VectorReads); Assert.AreEqual(before, f.R.F.PayloadReads);
    }
    [TestMethod]
    public async Task Search_answer_final_delivery_rechecks_frozen_publication()
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var scope = SearchAnswerScope(f);
        var answer = await f.Service.ExecuteAsync([f.R.F.Binding.KnowledgeBaseId], f.R.Actor, f.R.Model, f.Generation, "问题", default, scope);
        f.R.Query.Rows = [f.R.Query.Rows.Single() with { PublicationId = Guid.CreateVersion7() }];
        await Assert.ThrowsExactlyAsync<AiKnowledgeSearchSourceChangedException>(() => f.Service.RevalidateDeliveryAsync(answer, default));
        Assert.AreEqual(2, f.Budget.Settlements.Count); Assert.AreEqual(1, f.Factory.ChatCalls);
    }
    [TestMethod]
    public async Task Search_answer_requires_search_permission_before_charging()
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var scope = SearchAnswerScope(f);
        f.R.DeniedPermission = AiKnowledgeSearchPermissions.Read;
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => f.Service.ExecuteAsync([f.R.F.Binding.KnowledgeBaseId],
            f.R.Actor, f.R.Model, f.Generation, "问题", default, scope));
        Assert.AreEqual(0, f.Budget.Requests.Count); Assert.AreEqual(0, f.Factory.EmbeddingCalls);
    }
    [TestMethod]
    public async Task Search_answer_late_search_revocation_rejects_delivery_after_settlement()
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var scope = SearchAnswerScope(f);
        f.Budget.BeforeSettle = request => { if (request.Kind == "chat") f.R.DeniedPermission = AiKnowledgeSearchPermissions.Read; };
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => f.Service.ExecuteAsync([f.R.F.Binding.KnowledgeBaseId],
            f.R.Actor, f.R.Model, f.Generation, "问题", default, scope));
        Assert.AreEqual(2, f.Budget.Settlements.Count); Assert.AreEqual(1, f.Factory.ChatCalls);
    }
    [TestMethod]
    public async Task Search_answer_freezes_caller_collection_before_budget_wait()
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var scope = SearchAnswerScope(f);
        var documents = scope.DocumentIds.ToArray(); var sources = scope.Sources.ToArray(); scope = scope with { DocumentIds = documents, Sources = sources };
        f.Budget.BeforeReserve = _ => { documents[0] = Guid.CreateVersion7(); sources[0] = sources[0] with { PublicationId = Guid.CreateVersion7() }; };
        var answer = await f.Service.ExecuteAsync([f.R.F.Binding.KnowledgeBaseId], f.R.Actor, f.R.Model, f.Generation, "问题", default, scope);
        Assert.AreEqual(f.R.F.Binding.DocumentId, answer.Prepared.Retrieval.SearchDocumentIds!.Single());
        Assert.AreEqual(ContextSource(f.R.Query.Rows.Single()), answer.Prepared.Retrieval.ExpectedSearchSources!.Single());
    }
    [TestMethod] [DataRow("duplicates")] [DataRow("missingDocument")] [DataRow("extraDocument")] [DataRow("wrongBase")]
    [DataRow("empty")] [DataRow("badHash")] [DataRow("badVersion")]
    public async Task Search_answer_invalid_scope_never_reaches_authority_or_budget(string fault)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var scope = SearchAnswerScope(f); var source = scope.Sources.Single();
        scope = fault switch { "duplicates" => scope with { Sources = [source, source] }, "missingDocument" => scope with { DocumentIds = [Guid.CreateVersion7()] },
            "extraDocument" => scope with { DocumentIds = [source.DocumentId, Guid.CreateVersion7()] }, "wrongBase" => scope with { Sources = [source with { KnowledgeBaseId = Guid.CreateVersion7() }] },
            "empty" => scope with { Sources = [] }, "badHash" => scope with { Sources = [source with { ManifestHash = "secret" }] },
            _ => scope with { EmbeddingModelVersion = 0 } };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Service.ExecuteAsync([f.R.F.Binding.KnowledgeBaseId],
            f.R.Actor, f.R.Model, f.Generation, "问题", default, scope));
        Assert.AreEqual(0, f.Budget.Requests.Count); Assert.AreEqual(0, f.R.Query.SourceReads);
    }
}
