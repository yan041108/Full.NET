using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing;
using Full.NET.Modules.Printing.Contracts;

namespace Full.NET.UnitTests.Printing;

[TestClass]
public sealed class PrintingAuthorizationContributorTests
{
    [TestMethod]
    public void Published_printing_permissions_keep_host_management_and_tenant_rendering_separate()
    {
        var catalog = AuthorizationCatalog.Create([new PrintingAuthorizationContributor()]);
        var grants = catalog.Permissions.SingleOrDefault(p => p.Code == "printing.templates.grant_tenants");
        var read = catalog.Permissions.SingleOrDefault(p => p.Code == "printing.published_templates.read");
        var preview = catalog.Permissions.SingleOrDefault(p => p.Code == "printing.published_templates.preview");
        Assert.IsNotNull(grants, "Published printing versions require an explicit Host grant permission.");
        Assert.IsNotNull(read, "Tenant catalog reading must have its own permission.");
        Assert.IsNotNull(preview, "Tenant preview must have its own permission.");
        Assert.AreEqual(AuthorizationScope.Host, grants.Scope);
        Assert.AreEqual(AuthorizationScope.Tenant, read.Scope);
        Assert.AreEqual(AuthorizationScope.Tenant, preview.Scope);
        foreach (var permission in new[] { PrintingTemplatePermissions.Read, PrintingTemplatePermissions.Create,
            PrintingTemplatePermissions.Update, PrintingTemplatePermissions.Publish, PrintingTemplatePermissions.Preview })
            Assert.AreEqual(AuthorizationScope.Host, catalog.Permissions.Single(p => p.Code == permission).Scope);
    }

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
                PrintingTemplatePermissions.GrantTenants,
                PrintingPublishedTemplatePermissions.Read,
                PrintingPublishedTemplatePermissions.Preview,
                PrintingFormSchemaPermissions.Read,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var preview = catalog.Navigation.Single(item => item.Id == "printing-preview");
        Assert.AreEqual(PrintingTemplatePermissions.Preview, preview.RequiredPermission);
        Assert.AreEqual("/printing/preview", preview.Path);
    }
}
