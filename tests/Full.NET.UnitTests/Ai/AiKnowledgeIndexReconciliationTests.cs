using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiKnowledgeIndexReconciliationTests
{
    [TestMethod]
    [DataRow("ready", "completed", true, true, true, 1, 1, "ready", "none")]
    [DataRow("ready", "completed", true, true, false, 1, 1, "ready", "none")]
    [DataRow("failed", "completed", true, true, true, 1, 1, "generation_unavailable", "rebuild")]
    [DataRow("failed", "completed", true, false, true, 1, 1, "binding_stale", "enqueue")]
    [DataRow("failed", "failed", true, true, true, 3, 1, "task_failed", "retry")]
    [DataRow("failed", "cancelled", true, true, true, 1, 1, "task_cancelled", "rebuild")]
    [DataRow("processing", "running", true, true, true, 1, 1, "processing", "wait")]
    [DataRow("processing", "running", true, true, true, 1, -1, "lease_expired", "wait")]
    [DataRow("processing", "running", true, true, true, 1, 0, "lease_expired", "wait")]
    [DataRow("processing", "running", true, true, true, 3, -1, "lease_exhausted", "retry")]
    [DataRow("queued", "queued", true, true, true, 0, 1, "queued", "wait")]
    [DataRow("pending_processing", null, true, false, true, 0, 1, "not_indexed", "enqueue")]
    [DataRow("failed", "completed", false, true, true, 1, 1, "source_unavailable", "none")]
    [DataRow("failed", "completed", true, true, false, 1, 1, "format_unsupported", "none")]
    [DataRow("deleting", "completed", false, false, true, 1, 1, "cleanup_pending", "wait")]
    [DataRow("deleted", "completed", false, false, true, 1, 1, "deleted", "none")]
    public void Recovery_hints_preserve_authority_and_never_turn_failure_into_ready(string state, string? taskState,
        bool available, bool current, bool supported, int attempts, int leaseOffset, string reason, string action)
    {
        var now = DateTimeOffset.Parse("2026-10-07T00:00:00Z");
        var result = AiKnowledgeIndexReconciliationPolicy.Decide(state, taskState, available, current, supported,
            attempts, now.AddMinutes(leaseOffset), now);
        Assert.AreEqual(reason, result.Reason);
        Assert.AreEqual(action, result.Action);
    }
}
