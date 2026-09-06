using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Ocr;
using Full.NET.Modules.Ocr.Contracts;

namespace Full.NET.UnitTests.Ocr;

[TestClass]
public sealed class OcrAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_publishes_ocr_permissions_and_navigation()
    {
        var catalog = AuthorizationCatalog.Create([new OcrAuthorizationContributor()]);

        CollectionAssert.AreEquivalent(
            new[]
            {
                OcrProviderPermissions.Read,
                OcrProviderPermissions.Update,
                OcrProviderPermissions.Test,
                OcrIdCardTaskPermissions.Read,
                OcrIdCardTaskPermissions.Create,
                OcrIdCardTaskPermissions.Confirm,
                OcrIdCardTaskPermissions.Reject,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var tasks = catalog.Navigation.Single(item => item.Id == "ocr-id-card-tasks");
        Assert.AreEqual(OcrIdCardTaskPermissions.Read, tasks.RequiredPermission);
        Assert.AreEqual("/ocr/id-card-tasks", tasks.Path);
    }
}
