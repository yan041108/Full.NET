using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证多人审批在赞成票达到门槛或剩余票不足时确定性收敛。</summary>
[TestClass]
public sealed class WorkflowApprovalDecisionTests
{
    /// <summary>赞成票达到法定票数时必须立即批准。</summary>
    [TestMethod]
    public void Required_approvals_reached_completes_as_approved()
    {
        var result = WorkflowApprovalDecision.Resolve(2, approvedCount: 2, pendingCount: 1);

        Assert.AreEqual(WorkflowApprovalOutcome.Approved, result);
    }

    /// <summary>即使全部剩余票赞成也无法达标时必须立即驳回。</summary>
    [TestMethod]
    public void Insufficient_remaining_votes_completes_as_rejected()
    {
        var result = WorkflowApprovalDecision.Resolve(3, approvedCount: 1, pendingCount: 1);

        Assert.AreEqual(WorkflowApprovalOutcome.Rejected, result);
    }

    /// <summary>尚未达到任一终态时必须保持等待。</summary>
    [TestMethod]
    public void Viable_incomplete_vote_remains_waiting()
    {
        var result = WorkflowApprovalDecision.Resolve(2, approvedCount: 1, pendingCount: 2);

        Assert.AreEqual(WorkflowApprovalOutcome.Waiting, result);
    }
}
