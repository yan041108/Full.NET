using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantEntitlementTests
{
    [TestMethod]
    public void ValidateCatalogCode_rejects_invalid_codes()
    {
        var result = TenantEntitlementManagementService.ValidateCatalogCode("Bad Code");
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.EntitlementCodeInvalid, result.Error!.Code);
    }

    [TestMethod]
    public void ValidateCatalogCode_normalizes_to_lowercase()
    {
        var result = TenantEntitlementManagementService.ValidateCatalogCode(" Workflow_Start ");
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("workflow_start", result.Value);
    }

    [TestMethod]
    public void ValidateCatalogCode_accepts_dotted_catalog_codes()
    {
        foreach (var code in new[]
                 {
                     TenantEntitlementCatalogCodes.CompatibilityBaseline,
                     TenantEntitlementCatalogCodes.Workflow,
                 })
        {
            var result = TenantEntitlementManagementService.ValidateCatalogCode(code);
            Assert.IsTrue(result.IsSuccess, code);
            Assert.AreEqual(code, result.Value);
        }
    }
}
