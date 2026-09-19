using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;
using Full.NET.IntegrationTests.Tenancy;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class TenancyApiMySqlTests
{
    [TestMethod]
    public async Task Anonymous_current_tenant_endpoint_returns_minimal_standard_http_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenancyApiAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_tenant_management_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenancyHostTenantManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_tenant_package_management_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenancyHostTenantPackageManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_tenant_package_assignment_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenancyHostTenantPackageAssignmentAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Provision_tenant_with_optional_package_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenancyProvisionTenantWithPackageAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_lifecycle_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantLifecycleAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task TenantQuota_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantQuotaAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task TenantSubscription_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantSubscriptionAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task TenantEntitlement_enforcement_phase_round_trip()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantEntitlementAssertions.VerifyEnforcementPhaseRoundTripAsync(factory);
    }

    [TestMethod]
    public async Task TenantCommercial_reactivate_gate_blocks_without_package()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            new Dictionary<string, string?>
            {
                ["Tenancy:Commercial:RequirePackageOrSubscriptionOnReactivate"] = "true",
            });

        await TenantEntitlementAssertions.VerifyCommercialReactivateGateAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_membership_invitation_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantMembershipAssertions.VerifyAsync(factory);
    }
}
