using System.Security.Claims;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Tenancy;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class FullNetPermissionHandlerTests
{
    [TestMethod]
    public async Task Policy_provider_only_builds_policies_for_known_exact_codes()
    {
        var catalog = AuthorizationCatalog.Create(
            [new IdentityAuthorizationContributor(), new TenancyAuthorizationContributor()]);
        var provider = new FullNetPermissionPolicyProvider(
            Options.Create(new AuthorizationOptions()),
            catalog);

        var known = await provider.GetPolicyAsync(
            FullNetPermissionPolicyProvider.CreatePolicyName(
                "tenancy.tenants.read"));
        var unknown = await provider.GetPolicyAsync(
            FullNetPermissionPolicyProvider.CreatePolicyName(
                "tenancy.tenants.unknown"));

        Assert.IsNotNull(known);
        Assert.IsNull(unknown);
    }

    [TestMethod]
    public async Task Exact_permission_claim_succeeds()
    {
        var requirement = new FullNetPermissionRequirement("tenancy.tenants.read");
        var principal = CreatePrincipal("tenancy.tenants.read");
        var context = new AuthorizationHandlerContext(
            [requirement],
            principal,
            null);

        await new FullNetPermissionHandler().HandleAsync(context);

        Assert.IsTrue(context.HasSucceeded);
    }

    [TestMethod]
    [DataRow("TENANCY.TENANTS.READ")]
    [DataRow("tenancy.tenants")]
    [DataRow("tenancy.tenants.read.extra")]
    public async Task Case_or_prefix_variants_do_not_succeed(string claimValue)
    {
        var requirement = new FullNetPermissionRequirement("tenancy.tenants.read");
        var context = new AuthorizationHandlerContext(
            [requirement],
            CreatePrincipal(claimValue),
            null);

        await new FullNetPermissionHandler().HandleAsync(context);

        Assert.IsFalse(context.HasSucceeded);
    }

    private static ClaimsPrincipal CreatePrincipal(string permission)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(IdentityClaimTypes.Permission, permission)],
            "unit-test"));
    }
}
