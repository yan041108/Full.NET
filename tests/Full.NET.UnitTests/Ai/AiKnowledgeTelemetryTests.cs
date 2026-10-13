using System.Diagnostics;
using Full.NET.AI.Abstractions.Budgets;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实收费问答的阶段观测必须脱敏，且不能改变派发、账本和最终来源复核。</summary>
public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod, DoNotParallelize]
    [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public async Task Knowledge_telemetry_correlates_stages_without_payload_or_identity(bool tenant, bool refusal)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant);
        if (refusal) f.Factory.Fault = "refusal";
        using var capture = new AgentTelemetryCapture();
        var result = await f.ExecuteAsync();
        await f.Service.RevalidateDeliveryAsync(result, CancellationToken.None);
        Assert.AreEqual(refusal, result.Answer.Refused);
        Assert.AreEqual(2, f.Budget.Requests.Count); Assert.AreEqual(2, f.Budget.Settlements.Count);
        Assert.AreEqual(1, f.Factory.EmbeddingCalls); Assert.AreEqual(1, f.Factory.ChatCalls);
        Assert.HasCount(8, capture.Spans);
        VerifyKnowledgeTelemetry(capture);
        VerifyKnowledgeStage(capture, "knowledge_answer", refusal ? "refused" : "completed");
        VerifyKnowledgeStage(capture, "knowledge_retrieval", "completed");
        VerifyKnowledgeStage(capture, "knowledge_rerank", "completed");
        VerifyKnowledgeStage(capture, "knowledge_context", "completed");
        VerifyKnowledgeStage(capture, "knowledge_validation", refusal ? "refused" : "completed");
        VerifyKnowledgeStage(capture, "knowledge_delivery", "completed");
        Assert.AreEqual(result.EmbeddingOperationId.ToString("D"), VerifyKnowledgeStage(capture, "knowledge_embedding", "completed").GetTagItem("ai.operation.id"));
        Assert.AreEqual(result.GenerationOperationId.ToString("D"), VerifyKnowledgeStage(capture, "knowledge_generation", refusal ? "refused" : "completed").GetTagItem("ai.operation.id"));
        var answer = capture.Spans.Single(s => Equals(s.GetTagItem("ai.stage"), "knowledge_answer"));
        foreach (var span in capture.Spans.Where(s => s != answer && !Equals(s.GetTagItem("ai.stage"), "knowledge_delivery")))
            Assert.IsTrue(span.ParentSpanId == answer.SpanId || capture.Spans.Any(p => p.SpanId == span.ParentSpanId));
    }

    [TestMethod, DoNotParallelize]
    [DataRow(false, "embedding")] [DataRow(true, "embedding")] [DataRow(false, "chat")] [DataRow(true, "chat")]
    public async Task Knowledge_telemetry_budget_rejection_keeps_zero_dispatch_and_existing_settlement(bool tenant, string stage)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); f.Budget.RejectKind = stage;
        using var capture = new AgentTelemetryCapture();
        await Assert.ThrowsExactlyAsync<AiBudgetException>(() => f.ExecuteAsync());
        VerifyKnowledgeStage(capture, "knowledge_answer", "budget_rejected");
        VerifyKnowledgeStage(capture, stage == "embedding" ? "knowledge_embedding" : "knowledge_generation", "budget_rejected");
        Assert.AreEqual(stage == "embedding" ? 0 : 1, f.Factory.EmbeddingCalls);
        Assert.AreEqual(0, f.Factory.ChatCalls); Assert.AreEqual(stage == "embedding" ? 0 : 1, f.Budget.Settlements.Count);
        VerifyKnowledgeTelemetry(capture);
    }

    [TestMethod, DoNotParallelize]
    [DataRow(false, "embedding")] [DataRow(true, "embedding")] [DataRow(false, "chat")] [DataRow(true, "chat")]
    public async Task Knowledge_telemetry_revocation_after_reservation_keeps_zero_charge_receipt(bool tenant, string stage)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant);
        f.Budget.BeforeReserve = request => { if (request.Kind == stage) f.R.Denied = true; };
        using var capture = new AgentTelemetryCapture();
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => f.ExecuteAsync());
        VerifyKnowledgeStage(capture, "knowledge_answer", "authorization_required");
        VerifyKnowledgeStage(capture, stage == "embedding" ? "knowledge_embedding" : "knowledge_generation", "authorization_required");
        Assert.AreEqual(0, f.Factory.ChatCalls);
        Assert.AreEqual(stage == "embedding" ? 0 : 1, f.Factory.EmbeddingCalls);
        Assert.AreEqual(stage == "embedding" ? 1 : 2, f.Budget.Settlements.Count);
        Assert.AreEqual(new AiOperationUsage(0, 0), f.Budget.Settlements[^1].Usage);
        VerifyKnowledgeTelemetry(capture);
    }

    [TestMethod, DoNotParallelize]
    [DataRow(false, "embedding")] [DataRow(true, "embedding")] [DataRow(false, "chat")] [DataRow(true, "chat")]
    public async Task Knowledge_telemetry_provider_failure_preserves_unknown_usage(bool tenant, string stage)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); f.Factory.ThrowKind = stage;
        using var capture = new AgentTelemetryCapture();
        await Assert.ThrowsExactlyAsync<IOException>(() => f.ExecuteAsync());
        VerifyKnowledgeStage(capture, "knowledge_answer", "failed");
        VerifyKnowledgeStage(capture, stage == "embedding" ? "knowledge_embedding" : "knowledge_generation", "failed");
        Assert.AreEqual(new AiOperationUsage(null, null), f.Budget.Settlements[^1].Usage);
        Assert.AreEqual(stage == "embedding" ? 1 : 2, f.Budget.Settlements.Count);
        VerifyKnowledgeTelemetry(capture);
    }

    [TestMethod, DoNotParallelize]
    [DataRow(false)] [DataRow(true)]
    public async Task Knowledge_telemetry_cancellation_after_reservation_is_not_success(bool tenant)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); using var stop = new CancellationTokenSource();
        f.Budget.BeforeReserve = _ => stop.Cancel();
        using var capture = new AgentTelemetryCapture();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.ExecuteAsync(token: stop.Token));
        VerifyKnowledgeStage(capture, "knowledge_answer", "cancelled");
        VerifyKnowledgeStage(capture, "knowledge_embedding", "cancelled");
        Assert.AreEqual(0, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls);
        Assert.AreEqual(new AiOperationUsage(0, 0), f.Budget.Settlements.Single().Usage);
        VerifyKnowledgeTelemetry(capture);
    }

    [TestMethod, DoNotParallelize]
    [DataRow(false)] [DataRow(true)]
    public async Task Knowledge_telemetry_final_delivery_revoke_withholds_body_without_new_charge(bool tenant)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant);
        using var capture = new AgentTelemetryCapture(); var result = await f.ExecuteAsync(); f.R.Denied = true;
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => f.Service.RevalidateDeliveryAsync(result, CancellationToken.None));
        VerifyKnowledgeStage(capture, "knowledge_delivery", "authorization_required");
        Assert.AreEqual(2, f.Budget.Requests.Count); Assert.AreEqual(2, f.Budget.Settlements.Count);
        VerifyKnowledgeTelemetry(capture);
    }

    [TestMethod, DoNotParallelize]
    [DataRow("attempts", null)] [DataRow("duration", null)] [DataRow(null, "sample")]
    [DataRow(null, "started")] [DataRow(null, "stopped")]
    public async Task Knowledge_telemetry_listener_fault_does_not_change_paid_result(string? measurement, string? trace)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false);
        using var capture = new AgentTelemetryCapture(throwMeasurement: measurement, throwTrace: trace);
        var result = await f.ExecuteAsync(); await f.Service.RevalidateDeliveryAsync(result, CancellationToken.None);
        Assert.AreEqual("有依据的答案", result.Answer.Text);
        Assert.AreEqual(2, f.Budget.Settlements.Count); Assert.AreEqual(1, f.Factory.EmbeddingCalls); Assert.AreEqual(1, f.Factory.ChatCalls);
        Assert.AreSame(capture.Root, Activity.Current);
    }

    private static Activity VerifyKnowledgeStage(AgentTelemetryCapture capture, string stage, string outcome)
    {
        var span = capture.Spans.Single(s => Equals(s.GetTagItem("ai.stage"), stage));
        Assert.AreEqual(outcome, span.GetTagItem("ai.outcome")); return span;
    }
    private static void VerifyKnowledgeTelemetry(AgentTelemetryCapture capture)
    {
        Assert.HasCount(capture.Spans.Count * 2, capture.Measurements);
        foreach (var span in capture.Spans)
        {
            Assert.AreEqual(capture.Root.TraceId, span.TraceId);
            Assert.IsNull(span.StatusDescription); Assert.IsFalse(span.Events.Any());
            Assert.IsTrue(span.TagObjects.All(t => t.Key is "ai.stage" or "ai.outcome" or "ai.operation.id"));
        }
        foreach (var m in capture.Measurements)
        {
            CollectionAssert.AreEquivalent(new[] { "ai.stage", "ai.outcome" }, m.Tags.Select(t => t.Key).ToArray());
            Assert.IsTrue(m.Name == "fullnet.ai.execution.attempts" ? m.Value == 1 : m.Value >= 0);
        }
        Assert.AreSame(capture.Root, Activity.Current);
    }
}
