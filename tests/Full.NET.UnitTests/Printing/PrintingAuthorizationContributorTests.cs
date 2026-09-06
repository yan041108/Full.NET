using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Printing;
using Full.NET.Modules.Printing.Contracts;

namespace Full.NET.UnitTests.Printing;

[TestClass]
public sealed class PrintingAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_publishes_printing_permissions_and_navigation()
    {
        var catalog = AuthorizationCatalog.Create([new PrintingAuthorizationContributor()]);

        CollectionAssert.AreEquivalent(
            new[]
            {
                PrintingTemplatePermissions.Read,
                PrintingTemplatePermissions.Create,
                PrintingTemplatePermissions.Update,
                PrintingTemplatePermissions.Publish,
                PrintingTemplatePermissions.Preview,
                PrintingFormSchemaPermissions.Read,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var preview = catalog.Navigation.Single(item => item.Id == "printing-preview");
        Assert.AreEqual(PrintingTemplatePermissions.Preview, preview.RequiredPermission);
        Assert.AreEqual("/printing/preview", preview.Path);
    }
}
