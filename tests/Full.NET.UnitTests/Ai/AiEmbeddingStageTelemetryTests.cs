using Full.NET.AI.Abstractions.Budgets;

namespace Full.NET.UnitTests.Ai;

[DoNotParallelize]
public sealed partial class AiEmbeddingTestBoundaryTests
{
    [TestMethod]
    public async Task Stage_legacy_embedding_quota_admission_is_budget_rejected()
    {
        using var capture = new AgentTelemetryCapture(); using var f = new Fixture(); f.ReservationFault = "quota";
        Assert.IsFalse((await f.Service.TestAsync(f.Model.Id, new("secret-text", null))).IsSuccess);
        StageTelemetryAssertions.Verify(capture, "embedding_test", "budget_rejected");
        Assert.AreEqual(0, f.Dispatches); Assert.IsEmpty(f.Settlements);
    }

    [TestMethod][DataRow("success")][DataRow("invalid")][DataRow("duplicate")][DataRow("mismatch")]
    [DataRow("factory")][DataRow("provider")][DataRow("disposed")][DataRow("cancelled")][DataRow("settle")]
    public async Task Stage_boundary_embedding_records_result_only_after_original_settlement(string fault)
    {
        using var capture = new AgentTelemetryCapture(); using var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        if (fault is "duplicate" or "mismatch") f.ReservationFault = fault;
        if (fault == "factory") f.CreateFault = new IOException("secret-factory");
        if (fault == "provider") f.DispatchFault = new IOException("secret-provider");
        if (fault == "disposed") f.DisposeFault = new IOException("secret-dispose");
        if (fault == "cancelled") f.OnGenerate = cancellation.Cancel;
        // 结算故障发生在派发后，不能冒充准入预算拒绝。
        if (fault == "settle") f.SettleFault = new AiBudgetException("ai.budget.operation_conflict");
        var request = new Full.NET.Modules.Ai.Contracts.TestAiModelEmbeddingRequest(fault == "invalid" ? " " : "secret-text", null);
        if (fault == "cancelled") await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.TestAsync(f.Model.Id, request, cancellation.Token));
        else if (fault == "settle") await Assert.ThrowsExactlyAsync<AiBudgetException>(() => f.Service.TestAsync(f.Model.Id, request));
        else Assert.AreEqual(fault == "success", (await f.Service.TestAsync(f.Model.Id, request)).IsSuccess);
        var outcome = fault == "success" ? "completed" : fault == "invalid" ? "denied"
            : fault is "duplicate" or "mismatch" ? "budget_rejected" : fault == "cancelled" ? "cancelled" : "failed";
        StageTelemetryAssertions.Verify(capture, "embedding_test", outcome,
            fault is "invalid" or "duplicate" or "mismatch" ? null : f.Requests.Single().OperationId);
        if (fault == "invalid") f.AssertNoIo();
        if (fault is "duplicate" or "mismatch") { Assert.AreEqual(0, f.Dispatches); Assert.IsEmpty(f.Settlements); }
    }

    [TestMethod]
    public async Task Stage_boundary_embedding_precancel_has_no_observation_or_io()
    {
        using var capture = new AgentTelemetryCapture(); using var f = new Fixture(); using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.TestAsync(f.Model.Id, new("secret-text", null), stop.Token));
        f.AssertNoIo(); Assert.IsEmpty(capture.Spans); Assert.IsEmpty(capture.Measurements);
    }

    [TestMethod][DataRow("sample")][DataRow("started")][DataRow("stopped")][DataRow("attempts")][DataRow("duration")][DataRow("none")]
    public async Task Stage_boundary_embedding_listener_fault_preserves_success_and_settlement(string fault)
    {
        using var capture = StageTelemetryAssertions.FaultCapture(fault); using var f = new Fixture();
        Assert.IsTrue((await f.Service.TestAsync(f.Model.Id, new("secret-text", null))).IsSuccess);
        f.AssertSettlement(7, 0, "succeeded"); Assert.AreSame(capture.Root, System.Diagnostics.Activity.Current);
    }
}
