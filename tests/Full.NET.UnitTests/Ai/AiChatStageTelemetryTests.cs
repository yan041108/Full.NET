namespace Full.NET.UnitTests.Ai;

/// <summary>普通聊天观测真实收尾结果，已发送错误事件不能被归类为完成。</summary>
[DoNotParallelize]
public sealed partial class AiChatPaidBoundaryTests
{
    [TestMethod][DataRow(false)][DataRow(true)]
    public async Task Stage_legacy_chat_quota_admission_is_budget_rejected(bool scoped)
    {
        using var capture = new AgentTelemetryCapture(); await RunPaidBoundaryAsync(scoped, "quota");
        StageTelemetryAssertions.Verify(capture, "chat", "budget_rejected");
    }

    [TestMethod]
    [DataRow(false, "success")][DataRow(false, "duplicate")][DataRow(false, "identity")][DataRow(false, "state")]
    [DataRow(false, "factory")][DataRow(false, "null_factory")][DataRow(false, "factory_cancel")][DataRow(false, "start")]
    [DataRow(false, "callback")][DataRow(false, "provider")][DataRow(false, "unknown")][DataRow(false, "disposed")]
    [DataRow(false, "disposed_cancel")][DataRow(false, "null_source")]
    [DataRow(true, "success")][DataRow(true, "duplicate")][DataRow(true, "identity")][DataRow(true, "state")]
    [DataRow(true, "factory")][DataRow(true, "null_factory")][DataRow(true, "factory_cancel")][DataRow(true, "start")]
    [DataRow(true, "callback")][DataRow(true, "provider")][DataRow(true, "unknown")][DataRow(true, "disposed")]
    [DataRow(true, "disposed_cancel")][DataRow(true, "null_source")]
    public async Task Stage_boundary_chat_keeps_receipt_ownership_and_semantic_outcome(bool scoped, string fault)
    {
        using var capture = new AgentTelemetryCapture();
        var id = await RunPaidBoundaryAsync(scoped, fault);
        var outcome = fault is "success" or "unknown" ? "completed" : fault is "duplicate" or "identity" or "state" ? "budget_rejected"
            : fault is "factory_cancel" or "disposed_cancel" ? "cancelled" : "failed";
        StageTelemetryAssertions.Verify(capture, "chat", outcome, fault is "duplicate" or "identity" or "state" ? null : id);
    }

    [TestMethod][DataRow("sample")][DataRow("started")][DataRow("stopped")][DataRow("attempts")][DataRow("duration")][DataRow("none")]
    public async Task Stage_boundary_chat_listener_fault_cannot_change_paid_execution(string fault)
    {
        using var capture = StageTelemetryAssertions.FaultCapture(fault);
        await RunPaidBoundaryAsync(true, "success");
        Assert.AreSame(capture.Root, System.Diagnostics.Activity.Current);
    }

    [TestMethod][DataRow(false, "settle")][DataRow(false, "done")][DataRow(false, "error")]
    [DataRow(true, "settle")][DataRow(true, "done")][DataRow(true, "error")]
    public async Task Stage_boundary_chat_settlement_or_notification_failure_cannot_record_completed(bool scoped, string fault)
    {
        using var capture = new AgentTelemetryCapture(); var id = await RunPaidBoundaryAsync(scoped, fault);
        StageTelemetryAssertions.Verify(capture, "chat", "failed", id);
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public async Task Stage_boundary_chat_precancel_has_no_observation_or_dispatch(bool scoped)
    {
        using var capture = new AgentTelemetryCapture(); await RunPaidBoundaryAsync(scoped, "precancel");
        Assert.IsEmpty(capture.Spans); Assert.IsEmpty(capture.Measurements);
    }
}

/// <summary>诊断身份只进入 Trace；指标只允许固定阶段和结果，不携带正文、用户或模型。</summary>
internal static class StageTelemetryAssertions
{
    internal static AgentTelemetryCapture FaultCapture(string fault) => new(sampleNone: fault == "none",
        throwMeasurement: fault is "attempts" or "duration" ? fault : null, throwTrace: fault is "sample" or "started" or "stopped" ? fault : null);
    internal static void Verify(AgentTelemetryCapture capture, string stage, string outcome, Guid? operation = null)
    {
        Assert.HasCount(1, capture.Spans); Assert.HasCount(2, capture.Measurements);
        var span = capture.Spans.Single(); Assert.AreEqual(stage, span.GetTagItem("ai.stage"));
        Assert.AreEqual(outcome, span.GetTagItem("ai.outcome")); Assert.AreEqual(capture.Root.SpanId, span.ParentSpanId);
        Assert.AreEqual(operation?.ToString("D"), span.GetTagItem("ai.operation.id")); Assert.IsNull(span.GetTagItem("ai.run.id"));
        CollectionAssert.AreEquivalent(operation.HasValue ? new[] { "ai.stage", "ai.outcome", "ai.operation.id" } : new[] { "ai.stage", "ai.outcome" }, span.TagObjects.Select(t => t.Key).ToArray());
        Assert.IsNull(span.StatusDescription); Assert.IsFalse(span.Events.Any());
        foreach (var measurement in capture.Measurements)
        {
            CollectionAssert.AreEquivalent(new[] { "ai.stage", "ai.outcome" }, measurement.Tags.Select(t => t.Key).ToArray());
            Assert.AreEqual(stage, measurement.Tags.Single(t => t.Key == "ai.stage").Value);
            Assert.AreEqual(outcome, measurement.Tags.Single(t => t.Key == "ai.outcome").Value);
            Assert.IsTrue(measurement.Name.EndsWith("attempts", StringComparison.Ordinal) ? measurement.Value == 1 : measurement.Value >= 0);
        }
        Assert.AreSame(capture.Root, System.Diagnostics.Activity.Current);
    }
}
