using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.DataScope;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class RoleDataScopeProjectionTests
{
    [TestMethod]
    public void All_scope_does_not_append_filter()
    {
        var projection = CreateProjection(out var organizationProjection);

        var fragment = projection.BuildOrganizationUnitFilter(
            RoleDataScopeKinds.All,
            "unitObject.Id");

        Assert.IsNull(fragment);
        Assert.AreEqual(0, organizationProjection.CallCount);
    }

    [TestMethod]
    public void Self_scope_is_delegated_to_organization_projection()
    {
        var userId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf294");
        var projection = CreateProjection(out var organizationProjection);

        var fragment = projection.BuildOrganizationUnitFilter(
            RoleDataScopeKinds.Self,
            "unitObject.Id",
            userId);

        Assert.IsNotNull(fragment);
        Assert.AreEqual("organization-filter", fragment.Sql);
        Assert.AreEqual(userId, fragment.Parameters!.GetType().GetProperty("DataScopeUserId")!.GetValue(fragment.Parameters));
        Assert.AreEqual(1, organizationProjection.CallCount);
        Assert.AreEqual(RoleDataScopeKinds.Self, organizationProjection.DataScopeKind);
        Assert.AreEqual("unitObject.Id", organizationProjection.UnitIdColumn);
        Assert.AreEqual(userId, organizationProjection.CurrentUserId);
    }

    [TestMethod]
    public void Custom_scope_references_role_unit_table()
    {
        var projection = CreateProjection(out var organizationProjection);

        var fragment = projection.BuildOrganizationUnitFilter(
            RoleDataScopeKinds.Custom,
            "unitObject.Id");

        Assert.IsNotNull(fragment);
        StringAssert.Contains(fragment.Sql, "fn_identity_role_data_scope_unit");
        Assert.AreEqual(0, organizationProjection.CallCount);
    }

    [TestMethod]
    public void Unknown_scope_kind_is_rejected()
    {
        var projection = CreateProjection(out var organizationProjection);

        Assert.ThrowsExactly<ArgumentException>(() =>
            projection.BuildOrganizationUnitFilter(
                "identity.data_scope.unknown",
                "unitObject.Id"));
        Assert.AreEqual(0, organizationProjection.CallCount);
    }

    [TestMethod]
    public void Union_without_roles_denies_all_rows()
    {
        var projection = CreateProjection(out _);

        var fragment = projection.BuildUnionOrganizationUnitFilter(
            [],
            "unitObject.Id",
            Guid.NewGuid());

        Assert.IsNotNull(fragment);
        StringAssert.Contains(fragment.Sql, "1 = 0");
    }

    [TestMethod]
    public void Union_with_all_role_returns_no_filter()
    {
        var projection = CreateProjection(out _);

        var fragment = projection.BuildUnionOrganizationUnitFilter(
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
        var projection = CreateProjection(out _);

        var fragment = projection.BuildUnionOrganizationUnitFilter(
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

    private static RoleDataScopeProjection CreateProjection(
        out RecordingOrganizationDataScopeSqlProjection organizationProjection)
    {
        organizationProjection = new RecordingOrganizationDataScopeSqlProjection();
        return new RoleDataScopeProjection([organizationProjection]);
    }

    private sealed class RecordingOrganizationDataScopeSqlProjection
        : IIdentityOrganizationDataScopeSqlProjection
    {
        public int CallCount { get; private set; }

        public string? DataScopeKind { get; private set; }

        public string? UnitIdColumn { get; private set; }

        public Guid CurrentUserId { get; private set; }

        public DataScopeSqlFilter BuildOrganizationUnitFilter(
            string dataScopeKind,
            string unitIdColumn,
            Guid currentUserId)
        {
            CallCount++;
            DataScopeKind = dataScopeKind;
            UnitIdColumn = unitIdColumn;
            CurrentUserId = currentUserId;
            return new DataScopeSqlFilter(
                "organization-filter",
                new { DataScopeUserId = currentUserId });
        }
    }
}
