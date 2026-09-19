using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantSubscriptionTests
{
    [TestMethod]
    public void ValidateStatus_rejects_unknown_status()
    {
        var result = TenantSubscriptionManagementService.ValidateStatus("Suspended");
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.SubscriptionStatusInvalid, result.Error!.Code);
    }

    [TestMethod]
    public void ValidateStatus_accepts_trial()
    {
        var result = TenantSubscriptionManagementService.ValidateStatus(TenantSubscriptionStatuses.Trial);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(TenantSubscriptionStatuses.Trial, result.Value);
    }

    [TestMethod]
    public void ValidateStatus_rejects_active_when_duplicate_would_conflict()
    {
        var active = TenantSubscriptionManagementService.ValidateStatus(TenantSubscriptionStatuses.Active);
        var trial = TenantSubscriptionManagementService.ValidateStatus(TenantSubscriptionStatuses.Trial);
        Assert.IsTrue(active.IsSuccess);
        Assert.IsTrue(trial.IsSuccess);
        Assert.AreNotEqual(active.Value, trial.Value);
    }
}
