using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;

namespace Full.NET.UnitTests.DataApproval;

[TestClass]
public sealed class DataApprovalStatusTransitionTests
{
    [TestMethod]
    public void CanCancel_allows_pending_and_in_review_only()
    {
        Assert.IsTrue(DataApprovalStatusTransition.CanCancel(DataApprovalStatusKeys.Pending));
        Assert.IsTrue(DataApprovalStatusTransition.CanCancel(DataApprovalStatusKeys.InReview));
        Assert.IsFalse(DataApprovalStatusTransition.CanCancel(DataApprovalStatusKeys.Approved));
        Assert.IsFalse(DataApprovalStatusTransition.CanCancel(DataApprovalStatusKeys.Rejected));
        Assert.IsFalse(DataApprovalStatusTransition.CanCancel(DataApprovalStatusKeys.Cancelled));
    }

    [TestMethod]
    public void CanResolveFromWorkflow_requires_in_review()
    {
        Assert.IsTrue(DataApprovalStatusTransition.CanResolveFromWorkflow(DataApprovalStatusKeys.InReview));
        Assert.IsFalse(DataApprovalStatusTransition.CanResolveFromWorkflow(DataApprovalStatusKeys.Pending));
        Assert.IsFalse(DataApprovalStatusTransition.CanResolveFromWorkflow(DataApprovalStatusKeys.Approved));
    }

    [TestMethod]
    public void MapWorkflowTerminalStatus_maps_known_workflow_states()
    {
        Assert.AreEqual(
            DataApprovalStatusKeys.Approved,
            DataApprovalStatusTransition.MapWorkflowTerminalStatus("completed"));
        Assert.AreEqual(
            DataApprovalStatusKeys.Rejected,
            DataApprovalStatusTransition.MapWorkflowTerminalStatus("rejected"));
        Assert.AreEqual(
            DataApprovalStatusKeys.Cancelled,
            DataApprovalStatusTransition.MapWorkflowTerminalStatus("cancelled"));
        Assert.IsNull(DataApprovalStatusTransition.MapWorkflowTerminalStatus("active"));
    }
}

[TestClass]
public sealed class DataApprovalScenarioValidatorTests
{
    [TestMethod]
    public void IsSupportedScenario_accepts_serial_rule_host_update()
    {
        Assert.IsTrue(DataApprovalScenarioValidator.IsSupportedScenario(
            DataApprovalScenarioKeys.SerialRuleHostUpdate));
        Assert.IsTrue(DataApprovalScenarioValidator.IsSupportedScenario(
            "  serial_numbers.host_rule.update  "));
    }

    [TestMethod]
    public void IsSupportedScenario_rejects_unknown_scenarios()
    {
        Assert.IsFalse(DataApprovalScenarioValidator.IsSupportedScenario(null));
        Assert.IsFalse(DataApprovalScenarioValidator.IsSupportedScenario(string.Empty));
        Assert.IsFalse(DataApprovalScenarioValidator.IsSupportedScenario("other.scenario"));
    }
}
