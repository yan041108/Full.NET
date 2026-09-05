using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;

namespace Full.NET.UnitTests.DataApproval;

[TestClass]
public sealed class DataApprovalRecoveryRulesTests
{
    [TestMethod]
    public void CanRecoverWorkflowLink_allows_pending_without_workflow()
    {
        Assert.IsTrue(DataApprovalRecoveryRules.CanRecoverWorkflowLink(
            DataApprovalStatusKeys.Pending,
            null,
            DataApprovalRecoveryStatusKeys.PendingLink));
        Assert.IsTrue(DataApprovalRecoveryRules.CanRecoverWorkflowLink(
            DataApprovalStatusKeys.Pending,
            null,
            DataApprovalRecoveryStatusKeys.FailedRetryable));
    }

    [TestMethod]
    public void CanRecoverWorkflowLink_rejects_linked_or_terminal_status()
    {
        Assert.IsFalse(DataApprovalRecoveryRules.CanRecoverWorkflowLink(
            DataApprovalStatusKeys.InReview,
            Guid.NewGuid(),
            DataApprovalRecoveryStatusKeys.None));
        Assert.IsFalse(DataApprovalRecoveryRules.CanRecoverWorkflowLink(
            DataApprovalStatusKeys.Pending,
            null,
            DataApprovalRecoveryStatusKeys.FailedTerminal));
    }

    [TestMethod]
    public void CanManualRetry_requires_recoverable_non_none_state()
    {
        Assert.IsTrue(DataApprovalRecoveryRules.CanManualRetry(
            DataApprovalStatusKeys.Pending,
            null,
            DataApprovalRecoveryStatusKeys.FailedRetryable));
        Assert.IsFalse(DataApprovalRecoveryRules.CanManualRetry(
            DataApprovalStatusKeys.Pending,
            null,
            DataApprovalRecoveryStatusKeys.None));
    }
}
