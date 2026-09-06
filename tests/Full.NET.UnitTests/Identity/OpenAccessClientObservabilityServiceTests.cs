using Full.NET.Modules.Identity.Features.ManageOpenAccessClients;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class OpenAccessClientObservabilityServiceTests
{
    [TestMethod]
    public void ValidateDailyRequestQuota_allows_null_and_positive_values()
    {
        Assert.IsTrue(OpenAccessClientObservabilityService.ValidateDailyRequestQuota(null).IsSuccess);
        Assert.IsTrue(OpenAccessClientObservabilityService.ValidateDailyRequestQuota(100).IsSuccess);
    }

    [TestMethod]
    public void ValidateDailyRequestQuota_rejects_out_of_range_values()
    {
        Assert.IsFalse(OpenAccessClientObservabilityService.ValidateDailyRequestQuota(0).IsSuccess);
        Assert.IsFalse(OpenAccessClientObservabilityService.ValidateDailyRequestQuota(10_000_001).IsSuccess);
    }
}
