using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Organization;

[TestClass]
public sealed class IdentityOrganizationDataScopeSqlProjectionTests
{
    [TestMethod]
    public void Organization_module_owns_self_organization_and_subtree_sql()
    {
        var services = new ServiceCollection();
        new OrganizationModule().AddServices(
            services,
            new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var projection = provider.GetRequiredService<
            IIdentityOrganizationDataScopeSqlProjection>();
        var currentUserId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf294");

        var self = projection.BuildOrganizationUnitFilter(
            RoleDataScopeKinds.Self,
            "unitObject.Id",
            currentUserId);
        var organization = projection.BuildOrganizationUnitFilter(
            RoleDataScopeKinds.Organization,
            "unitObject.Id",
            currentUserId);
        var subtree = projection.BuildOrganizationUnitFilter(
            RoleDataScopeKinds.OrganizationSubtree,
            "unitObject.Id",
            currentUserId);

        StringAssert.Contains(self.Sql, "fn_organization_user_unit");
        StringAssert.Contains(self.Sql, "assignment.UserId = @DataScopeUserId");
        StringAssert.Contains(self.Sql, "unitObject.Id IN (");
        StringAssert.Contains(organization.Sql, "assignment.IsPrimary = 1");
        StringAssert.Contains(subtree.Sql, "fn_organization_unit");
        StringAssert.Contains(subtree.Sql, "WITH primary_unit AS");
        Assert.AreEqual(
            currentUserId,
            self.Parameters!.GetType().GetProperty("DataScopeUserId")!.GetValue(self.Parameters));
        Assert.ThrowsExactly<ArgumentException>(() =>
            projection.BuildOrganizationUnitFilter(
                RoleDataScopeKinds.Custom,
                "unitObject.Id",
                currentUserId));
    }
}
