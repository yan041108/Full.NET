using System.Text;
using System.Text.Json;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.SearchKnowledge;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>外部改写和排列只参与独立实验，逐步结算并保留原查询及完整发布证明。</summary>
public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    private static RefineAiKnowledgeSearchRequest RefinementRequest(PaidAnswerFixture f, string mode = "rewrite_rerank") =>
        new([f.R.F.Binding.KnowledgeBaseId], f.R.Model.Id, f.Generation.Id, f.Generation.Version, "x", mode, 5, false, SearchAnswerScope(f));
    private static AiKnowledgeSearchRefinementService RefinementService(PaidAnswerFixture f) =>
        new(f.R.Query, f.R.F.Tenant, f.R.F.Transactions, f.R.Permissions, f.R.Service, f.Service);
    private static string RefinementReply(JsonElement input) => input.GetProperty("task").GetString() == "knowledge_query_rewrite_v1"
        ? "{\"query\":\"x x\",\"refusal\":false}"
        : JsonSerializer.Serialize(new { order = input.GetProperty("candidates").EnumerateArray().Reverse().Select(row => row.GetProperty("id").GetString()).ToArray(), refusal = false });

    [TestMethod] [DataRow(false, "rewrite", 3)] [DataRow(true, "rewrite", 3)]
    [DataRow(false, "rerank", 2)] [DataRow(true, "rerank", 2)]
    [DataRow(false, "rewrite_rerank", 4)] [DataRow(true, "rewrite_rerank", 4)]
    public async Task Refinement_modes_use_approved_models_independent_rankings_and_distinct_receipts(bool tenant, string mode, int calls)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); f.Factory.RefinementReply = RefinementReply; f.R.DeniedPermission = AiChatPermissions.Send;
        var request = RefinementRequest(f, mode); var result = await RefinementService(f).RefineAsync(request, f.R.Actor, default);
        Assert.IsTrue(result.IsSuccess, result.Error?.Code); var value = result.Value!;
        Assert.AreEqual("x", value.Query); Assert.AreEqual(mode == "rerank" ? "x" : "x x", value.EffectiveQuery);
        CollectionAssert.AreEqual(request.SearchScope.Sources.ToArray(), value.Sources.ToArray()); Assert.IsNull(value.Diagnostics);
        Assert.AreEqual(calls, value.Operations.Count); Assert.AreEqual(calls, f.Budget.Requests.Count); Assert.AreEqual(calls, f.Budget.Settlements.Count);
        Assert.AreEqual(calls, value.Operations.Select(row => row.OperationId).Distinct().Count()); Assert.IsTrue(value.Operations.All(row => row.Status == "reported" && row.OperationId.Version == 7));
        Assert.IsTrue(f.Budget.Settlements.All(row => row.Outcome == "succeeded")); Assert.AreEqual(0, f.Factory.StreamCalls); Assert.IsNull(f.Factory.Options);
        Assert.AreEqual(mode == "rerank" ? 1 : 2, f.R.Query.VectorReads);
        Assert.AreEqual("keyword_occurrences", value.OriginalKeyword.ScoreKind); Assert.AreEqual("cosine_similarity", value.OriginalVector.ScoreKind);
        Assert.AreEqual(5, value.OriginalVector.Hits.Count); Assert.IsTrue(value.OriginalVector.HasMore);
        if (mode != "rerank") { Assert.IsNotNull(value.RewrittenVector); CollectionAssert.AreEqual(new[] { "x", "x x" }, f.Factory.EmbeddingTexts.ToArray()); }
        else Assert.IsNull(value.RewrittenVector);
        if (mode != "rewrite")
        {
            Assert.AreEqual(8, value.RerankCandidateCount); Assert.AreEqual("ordinal_priority", value.Reranked!.ScoreKind);
            CollectionAssert.AreEqual(new[] { 8d, 7d, 6d, 5d, 4d }, value.Reranked.Hits.Select(row => row.Score).ToArray());
            CollectionAssert.AreEqual(new[] { 7, 6, 5, 4, 3 }, value.Reranked.Hits.Select(row => row.Sequence).ToArray());
        }
        else { Assert.IsNull(value.Reranked); Assert.IsNull(value.RerankCandidateCount); }
    }
    [TestMethod] [DataRow("rewrite")] [DataRow("rerank")]
    public async Task Refinement_explicit_refusal_stops_only_remaining_steps_and_keeps_unknown_usage(string stage)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); f.Factory.Usage = null;
        f.Factory.RefinementReply = input => input.GetProperty("task").GetString() == (stage == "rewrite" ? "knowledge_query_rewrite_v1" : "knowledge_rerank_v1")
            ? stage == "rewrite" ? "{\"query\":\"\",\"refusal\":true}" : "{\"order\":[],\"refusal\":true}" : RefinementReply(input);
        var result = await RefinementService(f).RefineAsync(RefinementRequest(f), f.R.Actor, default); Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(stage, result.Value!.RefusedStage); Assert.IsNull(result.Value.Reranked);
        Assert.AreEqual(stage == "rewrite" ? 2 : 4, result.Value.Operations.Count); Assert.IsTrue(result.Value.Operations.All(row => row.Status == "unknown"));
        Assert.IsTrue(f.Budget.Settlements.All(row => row.Usage.InputTokens is null && row.Outcome == "succeeded"));
        Assert.AreEqual(stage == "rewrite" ? "x" : "x x", result.Value.EffectiveQuery);
    }
    [TestMethod] [DataRow("refine")] [DataRow("compare")] [DataRow("read")] [DataRow("diagnostics")]
    [DataRow("policy")] [DataRow("source")] [DataRow("version")] [DataRow("model")]
    public async Task Refinement_rejects_unapproved_input_before_payload_or_any_reservation(string fault)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false, fault == "policy" ? new AiDataProcessingPolicy(new Dictionary<string, IReadOnlyList<string>> { ["rerank"] = [] }) : null);
        var request = RefinementRequest(f); var before = f.R.F.PayloadReads;
        if (fault is "refine" or "compare" or "read" or "diagnostics") f.R.DeniedPermission = "ai.knowledge_search." + fault;
        if (fault == "diagnostics") request = request with { IncludeDiagnostics = true };
        if (fault == "version") request = request with { ProcessingModelVersion = 2 };
        if (fault == "source") request = request with { SearchScope = request.SearchScope with { Sources = [request.SearchScope.Sources[0] with { PublicationId = Guid.CreateVersion7() }] } };
        if (fault == "model") f.R.Query.Rows = [f.R.Query.Rows[0] with { GenerationModelConfigId = Guid.CreateVersion7() }];
        var result = await RefinementService(f).RefineAsync(request, f.R.Actor, default);
        Assert.IsFalse(result.IsSuccess); Assert.IsNull(result.Value); Assert.AreEqual(0, f.Budget.Requests.Count);
        Assert.AreEqual(0, f.Factory.ChatCalls); Assert.AreEqual(0, f.Factory.EmbeddingCalls); Assert.AreEqual(before, f.R.F.PayloadReads);
    }
    [TestMethod] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    public async Task Refinement_late_withdrawal_after_each_settlement_blocks_next_step_or_delivery(int step)
    {
        using var f = await PaidAnswerFixture.CreateAsync(true); f.Factory.RefinementReply = RefinementReply; var tenant = f.R.F.Tenant.Id;
        f.Budget.BeforeSettle = _ => { if (f.Budget.Settlements.Count + 1 == step) f.R.DeniedPermission = AiKnowledgeSearchPermissions.Refine; };
        var result = await RefinementService(f).RefineAsync(RefinementRequest(f), f.R.Actor, default);
        Assert.IsFalse(result.IsSuccess); Assert.IsNull(result.Value); Assert.AreEqual(step, f.Budget.Requests.Count); Assert.AreEqual(step, f.Budget.Settlements.Count);
        Assert.IsTrue(f.Budget.SettlementTenants.All(id => id == tenant));
    }
    [TestMethod] [DataRow("source")] [DataRow("scope")] [DataRow("model")]
    public async Task Refinement_chat_settlement_rechecks_complete_origin_and_uses_original_cleanup_scope(string fault)
    {
        using var f = await PaidAnswerFixture.CreateAsync(true); f.Factory.RefinementReply = RefinementReply; var tenant = f.R.F.Tenant.Id;
        f.Budget.BeforeSettle = operation => { if (operation.Kind != "chat") return;
            if (fault == "source") f.R.Query.Rows = [f.R.Query.Rows[0] with { PublicationId = Guid.CreateVersion7() }];
            if (fault == "scope") f.R.F.Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "另一租户"));
            if (fault == "model") f.R.Query.GenerationModel = PaidAnswerFixture.Model(f.Generation.Id, "https://changed.example/v1"); };
        var result = await RefinementService(f).RefineAsync(RefinementRequest(f), f.R.Actor, default);
        Assert.IsFalse(result.IsSuccess); Assert.IsNull(result.Value); Assert.AreEqual(2, f.Budget.Requests.Count); Assert.AreEqual(2, f.Budget.Settlements.Count);
        Assert.IsTrue(f.Budget.SettlementTenants.All(id => id == tenant));
    }
    [TestMethod] [DataRow("malformed")] [DataRow("dispatch")] [DataRow("duplicate")] [DataRow("settlement")]
    public async Task Refinement_chat_failure_does_not_retry_or_erase_preceding_cost(string fault)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); f.Factory.RefinementReply = _ => "{\"query\":\"x\",\"refusal\":false,\"sources\":[]}";
        if (fault == "dispatch") f.Factory.ThrowKind = "chat";
        if (fault == "duplicate") f.Budget.DuplicateKind = "chat";
        if (fault == "settlement") { f.Factory.RefinementReply = RefinementReply; f.Budget.SettleFailureKind = "chat"; }
        var result = await RefinementService(f).RefineAsync(RefinementRequest(f), f.R.Actor, default);
        Assert.IsFalse(result.IsSuccess); Assert.IsNull(result.Value); Assert.AreEqual(2, f.Budget.Requests.Count); Assert.AreEqual(1, f.Factory.EmbeddingCalls);
        Assert.AreEqual(fault == "duplicate" ? 0 : 1, f.Factory.ChatCalls);
        Assert.AreEqual(fault is "duplicate" or "settlement" ? 1 : 2, f.Budget.Settlements.Count);
        if (fault == "malformed") { Assert.AreEqual(7, f.Budget.Settlements[^1].Usage.InputTokens); Assert.AreEqual("failed", f.Budget.Settlements[^1].Outcome); }
        if (fault == "dispatch") Assert.IsNull(f.Budget.Settlements[^1].Usage.InputTokens);
    }
}

/// <summary>纯协议不信任模型身份、字段或预算，候选正文绝不由模型补写。</summary>
[TestClass]
public sealed class AiKnowledgeSearchRefinementProtocolTests
{
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static AiKnowledgeRankedChunk[] Candidates(int count = 17, string text = "明确补写") => Enumerable.Range(0, count).Select(index =>
        new AiKnowledgeRankedChunk(new AiKnowledgeRetrievalChunk(new AiKnowledgeIndexStatusRecord { DocumentId = Guid.CreateVersion7() }, index, null, 0, text.Length, text.Length, text, new float[] { 1 }), 1)).ToArray();
    [TestMethod]
    public void Refinement_protocol_keeps_exact_query_and_untrusted_json_without_sources()
    {
        var history = AiKnowledgeSearchRefinementProtocol.RewriteHistory("事实 \"范围\"", default);
        using var json = JsonDocument.Parse(history[1].Content); Assert.AreEqual("事实 \"范围\"", json.RootElement.GetProperty("query").GetString());
        Assert.AreEqual(2, json.RootElement.EnumerateObject().Count());
        Assert.AreEqual((false, "事实 范围"), AiKnowledgeSearchRefinementProtocol.ReadRewrite(Bytes("{\"query\":\"事实 范围\",\"refusal\":false}"), default));
        Assert.AreEqual((true, ""), AiKnowledgeSearchRefinementProtocol.ReadRewrite(Bytes("{\"query\":\"\",\"refusal\":true}"), default));
    }
    [TestMethod] [DataRow("{}")] [DataRow("[]")] [DataRow("{\"query\":\"x\",\"query\":\"y\",\"refusal\":false}")]
    [DataRow("{\"query\":\"x\",\"refusal\":false,\"tool\":\"delete\"}")] [DataRow("{\"query\":\"x\",\"refusal\":true}")]
    [DataRow("{\"query\":\"\",\"refusal\":false}")] [DataRow("{\"query\":123,\"refusal\":false}")] [DataRow("{\"query\":\"x\",\"refusal\":\"false\"}")]
    public void Refinement_protocol_rejects_malformed_ambiguous_or_extended_rewrite(string json) =>
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeSearchRefinementProtocol.ReadRewrite(Bytes(json), default));
    [TestMethod] [DataRow(513, 1)] [DataRow(65, 1)] [DataRow(1, 17)]
    public void Refinement_protocol_rewrite_limits_are_not_truncated(int length, int terms) =>
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeSearchRefinementProtocol.ReadRewrite(JsonSerializer.SerializeToUtf8Bytes(new { query = string.Join(' ', Enumerable.Repeat(new string('x', length), terms)), refusal = false }), default));
    [TestMethod]
    public void Refinement_protocol_server_prefix_and_ordinal_order_keep_exact_chunks()
    {
        var rows = Candidates(); var plan = AiKnowledgeSearchRefinementProtocol.Rerank("明确", rows, Guid.CreateVersion7, default);
        Assert.AreEqual(8, plan.Ids.Count); Assert.IsTrue(plan.HasMore); Assert.IsTrue(plan.Chunks.All(row => row.Vector.IsEmpty));
        using var json = JsonDocument.Parse(plan.History[1].Content); Assert.AreEqual(8, json.RootElement.GetProperty("candidates").GetArrayLength());
        Assert.IsTrue(json.RootElement.GetProperty("candidates").EnumerateArray().All(row => row.EnumerateObject().Count() == 2));
        var reply = AiKnowledgeSearchRefinementProtocol.ReadRerank(JsonSerializer.SerializeToUtf8Bytes(new { order = plan.Ids.Reverse(), refusal = false }), plan, default);
        CollectionAssert.AreEqual(Enumerable.Range(0, 8).Reverse().ToArray(), reply.Ranked.Select(row => row.Chunk.Sequence).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(1, 8).Reverse().Select(value => (double)value).ToArray(), reply.Ranked.Select(row => row.Score).ToArray());
        Assert.AreEqual(rows[7].Chunk.Text, reply.Ranked[0].Chunk.Text);
    }
    [TestMethod] [DataRow("missing")] [DataRow("unknown")] [DataRow("duplicate")] [DataRow("refusal")] [DataRow("extra")]
    public void Refinement_protocol_rerank_accepts_only_exact_permutation_or_empty_refusal(string fault)
    {
        var plan = AiKnowledgeSearchRefinementProtocol.Rerank("明确", Candidates(3), Guid.CreateVersion7, default); var ids = plan.Ids.ToArray();
        if (fault == "missing") ids = ids.Take(2).ToArray(); if (fault == "unknown") ids[0] = Guid.CreateVersion7(); if (fault == "duplicate") ids[0] = ids[1];
        var bytes = fault == "extra" ? JsonSerializer.SerializeToUtf8Bytes(new { order = ids, refusal = false, score = 1 }) : JsonSerializer.SerializeToUtf8Bytes(new { order = ids, refusal = fault == "refusal" });
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeSearchRefinementProtocol.ReadRerank(bytes, plan, default));
        var refused = AiKnowledgeSearchRefinementProtocol.ReadRerank(Bytes("{\"order\":[],\"refusal\":true}"), plan, default); Assert.IsTrue(refused.Refused); Assert.AreEqual(0, refused.Ranked.Count);
    }
    [TestMethod] [DataRow("body")] [DataRow("total")] [DataRow("tokens")] [DataRow("id")] [DataRow("duplicate")]
    public void Refinement_protocol_independent_candidate_budgets_fail_closed(string fault)
    {
        var rows = fault switch { "body" => Candidates(1, new string('x', 8001)), "total" => Candidates(8, new string('x', 5000)), "tokens" => Candidates(8, string.Join(' ', Enumerable.Repeat("复杂", 300))), _ => Candidates(3) };
        var id = Guid.CreateVersion7();
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeSearchRefinementProtocol.Rerank("明确", rows, fault == "id" ? () => Guid.Empty : fault == "duplicate" ? () => id : Guid.CreateVersion7, default));
    }
    [TestMethod]
    public void Refinement_protocol_cancellation_never_delivers_buffered_reply()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeSearchRefinementProtocol.ReadRewrite(Bytes("{}"), stop.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeSearchRefinementProtocol.Rerank("明确", Candidates(), Guid.CreateVersion7, stop.Token));
    }
}
