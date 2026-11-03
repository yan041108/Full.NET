using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.SearchKnowledge;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>同输入比较真实复用查询向量收费边界，且两组排名只消费一次完整候选。</summary>
public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public async Task Comparison_uses_same_complete_candidates_and_one_embedding_operation(bool tenant, bool unknown)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); if (unknown) f.Factory.Usage = null;
        var scope = SearchAnswerScope(f);
        var embedded = await f.Service.EmbedSearchAsync([f.R.F.Binding.KnowledgeBaseId], f.R.Actor, f.R.Model, scope, "x", false, default);
        var result = await f.R.Service.RetrieveComparisonAsync([f.R.F.Binding.KnowledgeBaseId], f.R.Actor, f.R.Model, scope, "x", embedded.Vector, false, default);
        await f.Service.RevalidateSearchDeliveryAsync(embedded, result, default);
        Assert.IsNotNull(result.Comparison); Assert.AreEqual(17, result.CandidateCount); Assert.AreEqual(1, f.R.Query.VectorReads);
        Assert.AreEqual(17, result.Comparison.Keyword.Count); Assert.AreEqual(17, result.Comparison.Vector.Count);
        Assert.IsTrue(result.Comparison.Keyword.All(row => result.Comparison.Vector.Any(other => ReferenceEquals(row.Chunk, other.Chunk))));
        Assert.IsNull(result.GenerationModel); Assert.AreEqual(0, result.Chunks.Count);
        Assert.AreEqual(1, f.Budget.Requests.Count); Assert.AreEqual("embedding", f.Budget.Requests.Single().Kind);
        Assert.AreEqual(1, f.Budget.Settlements.Count); Assert.AreEqual(unknown ? null : 7, f.Budget.Settlements[0].Usage.InputTokens);
        Assert.AreEqual(embedded.Usage.OperationId, f.Budget.Requests[0].OperationId); Assert.AreEqual(unknown ? "unknown" : "reported", embedded.Usage.Status);
        Assert.AreEqual(1, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls);
    }
    [TestMethod]
    public async Task Comparison_empty_keyword_results_do_not_suppress_vector_ranking()
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var scope = SearchAnswerScope(f); const string text = "确定不存在的词项";
        var embedded = await f.Service.EmbedSearchAsync([f.R.F.Binding.KnowledgeBaseId], f.R.Actor, f.R.Model, scope, text, false, default);
        var result = await f.R.Service.RetrieveComparisonAsync([f.R.F.Binding.KnowledgeBaseId], f.R.Actor, f.R.Model, scope, text, embedded.Vector, false, default);
        Assert.AreEqual(0, result.Comparison!.Keyword.Count); Assert.AreEqual(17, result.Comparison.Vector.Count);
        Assert.AreEqual(1, f.R.Query.VectorReads); Assert.AreEqual(1, f.Factory.EmbeddingCalls);
    }
    [TestMethod] [DataRow("source")] [DataRow("model")] [DataRow("read")] [DataRow("compare")] [DataRow("diagnostics")] [DataRow("policy")]
    public async Task Comparison_admission_rejects_before_budget_or_payload(string fault)
    {
        var policy = fault == "policy" ? new AiDataProcessingPolicy(new Dictionary<string, IReadOnlyList<string>> { ["rerank"] = ["public"] }) : null;
        using var f = await PaidAnswerFixture.CreateAsync(false, policy); var scope = SearchAnswerScope(f); var before = f.R.F.PayloadReads;
        if (fault == "source") scope = scope with { Sources = [scope.Sources[0] with { PublicationId = Guid.CreateVersion7() }] };
        if (fault == "model") scope = scope with { EmbeddingModelVersion = scope.EmbeddingModelVersion + 1 };
        if (fault is "read" or "compare" or "diagnostics") f.R.DeniedPermission = fault == "compare" ? AiKnowledgeSearchPermissions.Compare : fault == "read" ? AiKnowledgeSearchPermissions.Read : AiKnowledgeSearchPermissions.Diagnostics;
        await Assert.ThrowsAsync<Exception>(() => f.Service.EmbedSearchAsync([f.R.F.Binding.KnowledgeBaseId], f.R.Actor, f.R.Model, scope, "x", fault == "diagnostics", default));
        Assert.AreEqual(0, f.Budget.Requests.Count); Assert.AreEqual(0, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls);
        Assert.AreEqual(0, f.R.Query.VectorReads); Assert.AreEqual(before, f.R.F.PayloadReads);
    }
    [TestMethod] [DataRow("source")] [DataRow("read")] [DataRow("scope")] [DataRow("compare")]
    public async Task Comparison_settlement_wait_rechecks_source_permission_and_original_tenant(string fault)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var scope = SearchAnswerScope(f);
        f.Budget.BeforeSettle = _ =>
        {
            if (fault == "source") f.R.Query.Rows = [f.R.Query.Rows[0] with { PublicationId = Guid.CreateVersion7() }];
            if (fault == "read") f.R.DeniedPermission = AiKnowledgeSearchPermissions.Read;
            if (fault == "compare") f.R.DeniedPermission = AiKnowledgeSearchPermissions.Compare;
            if (fault == "scope") f.R.F.Tenant.SetTenant(new(Guid.CreateVersion7(), "changed", "变更租户"));
        };
        await Assert.ThrowsAsync<Exception>(() => f.Service.EmbedSearchAsync([f.R.F.Binding.KnowledgeBaseId], f.R.Actor, f.R.Model, scope, "x", false, default));
        Assert.AreEqual(1, f.Budget.Requests.Count); Assert.AreEqual(1, f.Budget.Settlements.Count);
        Assert.AreEqual(1, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls); Assert.IsNull(f.Budget.SettlementTenants.Single());
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Comparison_precise_dispatch_permission_does_not_require_host_chat_send(bool tenant)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); f.R.DeniedPermission = AiChatPermissions.Send;
        var result = await f.Service.EmbedSearchAsync([f.R.F.Binding.KnowledgeBaseId], f.R.Actor, f.R.Model, SearchAnswerScope(f), "x", false, default);
        Assert.AreEqual("reported", result.Usage.Status); Assert.IsTrue(result.Preflight.SearchComparison);
        Assert.AreEqual(1, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls);
    }
    [TestMethod] [DataRow("dispatch")] [DataRow("settlement")] [DataRow("duplicate")]
    public async Task Comparison_failure_does_not_retry_or_turn_unknown_usage_into_zero(string fault)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var scope = SearchAnswerScope(f);
        if (fault == "dispatch") f.Factory.ThrowKind = "embedding";
        if (fault == "settlement") f.Budget.SettleFailureKind = "embedding";
        if (fault == "duplicate") f.Budget.DuplicateKind = "embedding";
        await Assert.ThrowsAsync<Exception>(() => f.Service.EmbedSearchAsync([f.R.F.Binding.KnowledgeBaseId], f.R.Actor, f.R.Model, scope, "x", false, default));
        Assert.AreEqual(1, f.Budget.Requests.Count); Assert.AreEqual(fault == "duplicate" ? 0 : 1, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls);
        if (fault == "dispatch") { Assert.AreEqual(1, f.Budget.Settlements.Count); Assert.IsNull(f.Budget.Settlements[0].Usage.InputTokens); }
        if (fault == "duplicate") Assert.AreEqual(0, f.Budget.Settlements.Count);
    }
    [TestMethod]
    public async Task Comparison_delivery_preserves_preflight_classification_in_addition_to_public_identity()
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var scope = SearchAnswerScope(f);
        var embedded = await f.Service.EmbedSearchAsync([f.R.F.Binding.KnowledgeBaseId], f.R.Actor, f.R.Model, scope, "x", false, default);
        var result = await f.R.Service.RetrieveComparisonAsync([f.R.F.Binding.KnowledgeBaseId], f.R.Actor, f.R.Model, scope, "x", embedded.Vector, false, default);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Service.RevalidateSearchDeliveryAsync(embedded,
            result with { Sources = [result.Sources[0] with { DataClassification = "public" }] }, default));
        Assert.AreEqual(1, f.Budget.Requests.Count); Assert.AreEqual(1, f.Factory.EmbeddingCalls);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Comparison_public_service_maps_frozen_source_and_input_failures_without_billing(bool inputInvalid)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var scope = SearchAnswerScope(f);
        scope = scope with { EmbeddingModelVersion = scope.EmbeddingModelVersion + 1 };
        var service = new AiKnowledgeSearchComparisonService(f.R.Query, f.R.F.Tenant, f.R.F.Transactions, f.R.Permissions, f.R.Service, f.Service);
        var result = await service.CompareAsync(new([f.R.F.Binding.KnowledgeBaseId], f.R.Model.Id, inputInvalid ? " " : "x", 2, false, scope), f.R.Actor, default);
        Assert.IsFalse(result.IsSuccess); Assert.IsNull(result.Value);
        Assert.AreEqual(inputInvalid ? AiKnowledgeErrorCodes.SearchInputInvalid : AiKnowledgeErrorCodes.SearchSourceChanged, result.Error!.Code);
        Assert.AreEqual(0, f.Budget.Requests.Count); Assert.AreEqual(0, f.Factory.EmbeddingCalls);
    }
}
