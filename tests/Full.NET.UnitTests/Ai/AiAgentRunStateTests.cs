using Full.NET.Agents.Runtime;

namespace Full.NET.UnitTests.Ai;

/// <summary>终态不可重开；未知执行意图不能自动恢复派发。</summary>
[TestClass]
public sealed class AiAgentRunStateTests
{
    [TestMethod]
    [DataRow("queued", "running", true)]
    [DataRow("running", "completed", true)]
    [DataRow("running", "awaiting_approval", true)]
    [DataRow("running", "authorization_required", true)]
    [DataRow("running", "retry_scheduled", true)]
    [DataRow("running", "reconciliation_required", true)]
    [DataRow("running", "failed", true)]
    [DataRow("running", "cancelled", true)]
    [DataRow("running", "expired", true)]
    [DataRow("completed", "queued", false)]
    [DataRow("failed", "running", false)]
    [DataRow("cancelled", "queued", false)]
    [DataRow("awaiting_approval", "queued", true)]
    [DataRow("reconciliation_required", "running", false)]
    public void Normal_transitions_are_explicit(string from, string to, bool allowed) =>
        Assert.AreEqual(allowed, AgentRunState.CanTransition(from, to));

    [TestMethod]
    public void Recovery_uses_committed_checkpoint_and_never_replays_unknown_dispatch()
    {
        Assert.AreEqual("complete", AgentRunState.RecoveryAction("committed"));
        Assert.AreEqual("reconcile", AgentRunState.RecoveryAction("started"));
        Assert.AreEqual("dispatch", AgentRunState.RecoveryAction(null));
        Assert.Throws<InvalidOperationException>(() => AgentRunState.RecoveryAction("unrecognized"));
    }

    [TestMethod]
    [DataRow("completed", true)]
    [DataRow("failed", true)]
    [DataRow("running", false)]
    [DataRow("queued", false)]
    public void Terminal_statuses_do_not_reopen(string status, bool terminal) =>
        Assert.AreEqual(terminal, AgentRunState.IsTerminal(status));

    [TestMethod]
    public void Progress_commit_rejects_unknown_status() =>
        Assert.IsFalse(AgentRunState.CanTransitionToCommitted("queued"));
}
