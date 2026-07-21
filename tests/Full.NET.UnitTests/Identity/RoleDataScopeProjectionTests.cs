using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.DataScope;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class RoleDataScopeProjectionTests
{
    [TestMethod]
    public void All_scope_does_not_append_filter()
    {
        var fragment = RoleDataScopeProjection.BuildOrganizationUnitFilter(
            RoleDataScopeKinds.All,
            "unitObject.Id");
        Assert.IsNull(fragment);
    }

    [TestMethod]
    public void Self_scope_references_current_user_assignment()
    {
        var userId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf294");
        var fragment = RoleDataScopeProjection.BuildOrganizationUnitFilter(
            RoleDataScopeKinds.Self,
            "unitObject.Id",
            userId);
        Assert.IsNotNull(fragment);
        StringAssert.Contains(fragment.Sql, "assignment.UserId = @DataScopeUserId");
        Assert.AreEqual(userId, fragment.Parameters!.GetType().GetProperty("DataScopeUserId")!.GetValue(fragment.Parameters));
    }

    [TestMethod]
    public void Custom_scope_references_role_unit_table()
    {
        var fragment = RoleDataScopeProjection.BuildOrganizationUnitFilter(
            RoleDataScopeKinds.Custom,
            "unitObject.Id");
        Assert.IsNotNull(fragment);
        StringAssert.Contains(fragment.Sql, "fn_identity_role_data_scope_unit");
    }

    [TestMethod]
    public void Unknown_scope_kind_is_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            RoleDataScopeProjection.BuildOrganizationUnitFilter(
                "identity.data_scope.unknown",
                "unitObject.Id"));
    }
}
