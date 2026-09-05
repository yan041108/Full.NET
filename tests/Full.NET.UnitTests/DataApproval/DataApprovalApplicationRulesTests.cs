using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;

namespace Full.NET.UnitTests.DataApproval;

[TestClass]
public sealed class DataApprovalApplicationRulesTests
{
    [TestMethod]
    public void CanApply_allows_in_review_with_pending_application_states()
    {
        Assert.IsTrue(DataApprovalApplicationRules.CanApply(
            DataApprovalStatusKeys.InReview,
            DataApprovalApplicationStatusKeys.None));
        Assert.IsTrue(DataApprovalApplicationRules.CanApply(
            DataApprovalStatusKeys.InReview,
            DataApprovalApplicationStatusKeys.PendingApply));
        Assert.IsTrue(DataApprovalApplicationRules.CanApply(
            DataApprovalStatusKeys.InReview,
            DataApprovalApplicationStatusKeys.FailedRetryable));
    }

    [TestMethod]
    public void CanApply_rejects_approved_or_terminal_application()
    {
        Assert.IsFalse(DataApprovalApplicationRules.CanApply(
            DataApprovalStatusKeys.Approved,
            DataApprovalApplicationStatusKeys.Applied));
        Assert.IsFalse(DataApprovalApplicationRules.CanApply(
            DataApprovalStatusKeys.InReview,
            DataApprovalApplicationStatusKeys.FailedTerminal));
    }

    [TestMethod]
    public void CanManualRetryApply_requires_retryable_application_state()
    {
        Assert.IsTrue(DataApprovalApplicationRules.CanManualRetryApply(
            DataApprovalStatusKeys.InReview,
            DataApprovalApplicationStatusKeys.FailedRetryable));
        Assert.IsFalse(DataApprovalApplicationRules.CanManualRetryApply(
            DataApprovalStatusKeys.InReview,
            DataApprovalApplicationStatusKeys.None));
    }
}
