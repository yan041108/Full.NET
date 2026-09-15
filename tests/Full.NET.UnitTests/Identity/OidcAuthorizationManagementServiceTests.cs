using Full.NET.Modules.Identity.Features.ManageOidcAuthorizations;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class OidcAuthorizationManagementServiceTests
{
    [TestMethod]
    public void NormalizeFilter_trims_and_nullifies_blank_values()
    {
        Assert.IsNull(OidcAuthorizationQueryService.NormalizeFilter(null));
        Assert.IsNull(OidcAuthorizationQueryService.NormalizeFilter("   "));
        Assert.AreEqual("valid", OidcAuthorizationQueryService.NormalizeFilter(" valid "));
    }
}