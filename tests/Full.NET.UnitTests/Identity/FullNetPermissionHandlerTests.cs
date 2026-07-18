using System.Security.Claims;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
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

        await CreateHandler().HandleAsync(context);

        Assert.IsTrue(context.HasSucceeded);
    }

    [TestMethod]
    public async Task Permission_claim_without_effective_scope_is_rejected()
    {
        var requirement = new FullNetPermissionRequirement("tenancy.tenants.read");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(IdentityClaimTypes.Permission, "tenancy.tenants.read")],
            "unit-test"));
        var context = new AuthorizationHandlerContext(
            [requirement],
            principal,
            null);

        await CreateHandler().HandleAsync(context);

        Assert.IsFalse(context.HasSucceeded);
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

        await CreateHandler().HandleAsync(context);

        Assert.IsFalse(context.HasSucceeded);
    }

    [TestMethod]
    public async Task Super_administrator_claim_succeeds_only_for_matching_catalog_scope()
    {
        var hostRequirement = new FullNetPermissionRequirement("host.only");
        var tenantRequirement = new FullNetPermissionRequirement("platform.dashboard.read");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(IdentityClaimTypes.SuperAdministrator, "true"),
                new Claim(IdentityClaimTypes.Scope, "tenant:01981a3f00c070008000000000000001"),
            ],
            "unit-test"));
        var context = new AuthorizationHandlerContext(
            [hostRequirement, tenantRequirement],
            principal,
            null);

        await CreateHandler().HandleAsync(context);

        Assert.IsFalse(context.HasSucceeded);
        Assert.IsTrue(context.PendingRequirements.Contains(hostRequirement));
        Assert.IsFalse(context.PendingRequirements.Contains(tenantRequirement));
    }

    private static ClaimsPrincipal CreatePrincipal(string permission)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(IdentityClaimTypes.Permission, permission),
                new Claim(IdentityClaimTypes.Scope, "host"),
            ],
            "unit-test"));
    }

    private static FullNetPermissionHandler CreateHandler() => new(
        new PermissionClaimEvaluator(AuthorizationCatalog.Create(
            [
                new IdentityAuthorizationContributor(),
                new TenancyAuthorizationContributor(),
                new HostOnlyContributor(),
            ])));

    private sealed class HostOnlyContributor : IAuthorizationCatalogContributor
    {
        public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
        [new PermissionDefinition("host.only", "仅 Host", AuthorizationScope.Host)];

        public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = [];
    }
}
