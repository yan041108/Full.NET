using Full.NET.Modules.EnterpriseRequest.Contracts;

namespace Full.NET.UnitTests.EnterpriseRequest;

[TestClass]
public sealed class EnterpriseRequestStatusTransitionTests
{
    [TestMethod]
    public void IsTerminal_recognizes_approved_rejected_cancelled_only()
    {
        Assert.IsTrue(EnterpriseRequestStatusTransition.IsTerminal(EnterpriseRequestStatusKeys.Approved));
        Assert.IsTrue(EnterpriseRequestStatusTransition.IsTerminal(EnterpriseRequestStatusKeys.Rejected));
        Assert.IsTrue(EnterpriseRequestStatusTransition.IsTerminal(EnterpriseRequestStatusKeys.Cancelled));
        Assert.IsFalse(EnterpriseRequestStatusTransition.IsTerminal(EnterpriseRequestStatusKeys.Draft));
        Assert.IsFalse(EnterpriseRequestStatusTransition.IsTerminal(EnterpriseRequestStatusKeys.Submitted));
    }
}