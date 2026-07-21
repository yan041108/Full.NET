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

    [TestMethod]
    public void Union_without_roles_denies_all_rows()
    {
        var fragment = RoleDataScopeProjection.BuildUnionOrganizationUnitFilter(
            [],
            "unitObject.Id",
            Guid.NewGuid());
        Assert.IsNotNull(fragment);
        StringAssert.Contains(fragment.Sql, "1 = 0");
    }

    [TestMethod]
    public void Union_with_all_role_returns_no_filter()
    {
        var fragment = RoleDataScopeProjection.BuildUnionOrganizationUnitFilter(
            [new RoleDataScopeEntry(Guid.NewGuid(), RoleDataScopeKinds.All)],
            "unitObject.Id",
            Guid.NewGuid());
        Assert.IsNull(fragment);
    }

    [TestMethod]
    public void Union_combines_self_and_custom_with_or()
    {
        var userId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf294");
        var customRoleId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf295");
        var fragment = RoleDataScopeProjection.BuildUnionOrganizationUnitFilter(
            [
                new RoleDataScopeEntry(Guid.NewGuid(), RoleDataScopeKinds.Self),
                new RoleDataScopeEntry(customRoleId, RoleDataScopeKinds.Custom),
            ],
            "unitObject.Id",
            userId);
        Assert.IsNotNull(fragment);
        StringAssert.Contains(fragment.Sql, " OR ");
        StringAssert.Contains(fragment.Sql, "fn_identity_role_data_scope_unit");
        StringAssert.Contains(fragment.Sql, "@DataScopeRoleId_0");
        var parameters = fragment.Parameters as IDictionary<string, object?>;
        Assert.IsNotNull(parameters);
        Assert.AreEqual(userId, parameters["DataScopeUserId"]);
        Assert.AreEqual(customRoleId, parameters["DataScopeRoleId_0"]);
    }
}
