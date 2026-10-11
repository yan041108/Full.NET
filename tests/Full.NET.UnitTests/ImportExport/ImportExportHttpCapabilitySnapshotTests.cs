using System.Security.Claims;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Organization.Contracts;
using ImportEndpoint = Full.NET.Modules.ImportExport.Features.ManageImportTasks.Endpoint;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>导入入口必须按当前可信作用域冻结有效能力，不能把原始 Claim 当作授权目录。</summary>
[TestClass]
public sealed class ImportExportHttpCapabilitySnapshotTests
{
    [TestMethod]
    public void Super_administrator_without_permission_claims_retains_tenant_binding_capabilities()
    {
        var context = Build(Principal("tenant:" + Guid.NewGuid().ToString("N"), true));
        Assert.IsTrue(context.CapabilityFlags[OrganizationPositionManagementPermissions.AssignUnit]);
        Assert.IsTrue(context.CapabilityFlags[OrganizationPositionManagementPermissions.AssignPositionLevel]);
        Assert.IsFalse(context.CapabilityFlags.ContainsKey(IdentityUserManagementPermissions.Read));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Tenant_snapshot_excludes_host_only_unknown_and_case_variant_claims(bool superAdministrator)
    {
        var context = Build(Principal("tenant:" + Guid.NewGuid().ToString("N"), superAdministrator,
            OrganizationPositionManagementPermissions.AssignUnit, IdentityUserManagementPermissions.Read,
            "unknown.import", OrganizationPositionManagementPermissions.AssignUnit.ToUpperInvariant()));
        Assert.IsTrue(context.CapabilityFlags[OrganizationPositionManagementPermissions.AssignUnit]);
        Assert.IsFalse(context.CapabilityFlags.ContainsKey(IdentityUserManagementPermissions.Read));
        Assert.IsFalse(context.CapabilityFlags.ContainsKey("unknown.import"));
        Assert.IsFalse(context.CapabilityFlags.ContainsKey(OrganizationPositionManagementPermissions.AssignUnit.ToUpperInvariant()));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Missing_effective_scope_cannot_freeze_capabilities(bool superAdministrator)
    {
        var context = Build(Principal(null, superAdministrator, OrganizationPositionManagementPermissions.AssignUnit));
        Assert.AreEqual(0, context.CapabilityFlags.Count);
    }

    [TestMethod]
    public void Ordinary_tenant_retains_only_its_exact_permissions_and_request_actor()
    {
        var actor = Guid.NewGuid();
        var context = Build(Principal("tenant:" + Guid.NewGuid().ToString("N"), false,
            OrganizationPositionManagementPermissions.AssignUnit, OrganizationPositionManagementPermissions.AssignUnit), actor);
        Assert.AreEqual(actor, context.RequestedByUserId);
        CollectionAssert.AreEquivalent(new[] { OrganizationPositionManagementPermissions.AssignUnit }, context.CapabilityFlags.Keys.ToArray());
        Assert.IsTrue(context.CapabilityFlags.Values.Single());
    }

    private static ClaimsPrincipal Principal(string? scope, bool superAdministrator, params string[] permissions)
    {
        var claims = permissions.Select(permission => new Claim(FullNetIdentityClaimTypes.Permission, permission)).ToList();
        if (scope is not null) claims.Add(new(FullNetIdentityClaimTypes.Scope, scope));
        if (superAdministrator) claims.Add(new(FullNetIdentityClaimTypes.SuperAdministrator, "true"));
        return new(new ClaimsIdentity(claims, "test"));
    }

    private static StaticImportPreviewContext Build(ClaimsPrincipal principal, Guid? actor = null)
    {
        var evaluator = new PermissionClaimEvaluator(AuthorizationCatalog.Create([new OrganizationAuthorizationContributor(), new IdentityAuthorizationContributor()]));
        return ImportEndpoint.BuildPreviewContext(actor ?? Guid.NewGuid(), principal, evaluator);
    }
}
