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
    public async Task TenantQuota_metric_id_reconciliation_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantQuotaMetricIdReconciliationAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task TenantQuota_seat_usage_baseline_dry_run_and_apply()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantQuotaAssertions.VerifySeatUsageBaselineDryRunAndApplyAsync(factory);
    }

    [TestMethod]
    public async Task TenantQuota_storage_usage_baseline_after_upload()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantQuotaAssertions.VerifyStorageUsageBaselineAfterUploadAsync(factory);
    }

    [TestMethod]
    public async Task TenantQuota_missing_storage_metric_provisioned_via_usage_baseline()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantQuotaAssertions.VerifyMissingStorageMetricProvisionedViaUsageBaselineAsync(factory);
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
    public async Task TenantEntitlement_enforcement_phase_compatibility_shadow_enforced()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantEntitlementAssertions.VerifyEnforcementPhaseProgressionAsync(factory);
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
    public async Task TenantCommercial_reactivate_succeeds_with_subscription_in_enforced()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            new Dictionary<string, string?>
            {
                ["Tenancy:Commercial:RequirePackageOrSubscriptionOnReactivate"] = "true",
            });

        await TenantEntitlementAssertions.VerifyCommercialReactivateSucceedsWithActiveSubscriptionAsync(
            factory);
    }

    [TestMethod]
    public async Task TenantEntitlement_backfill_dry_run_reports_candidates()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantEntitlementAssertions.VerifyEntitlementBackfillDryRunAsync(factory);
    }

    [TestMethod]
    public async Task TenantEntitlement_backfill_apply_is_idempotent()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantEntitlementAssertions.VerifyEntitlementBackfillApplyIsIdempotentAsync(factory);
    }

    [TestMethod]
    public async Task TenantEntitlement_backfill_then_enforcement_progression()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantEntitlementAssertions.VerifyEntitlementBackfillThenEnforcementProgressionAsync(factory);
    }

    [TestMethod]
    public async Task TenantEntitlement_enforced_unbound_tenant_blocks_reactivate()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantEntitlementAssertions.VerifyEnforcedUnboundTenantHasEmptyBindingsAndBlocksReactivateAsync(
            factory);
    }

    [TestMethod]
    public async Task Tenant_membership_invitation_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantMembershipAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_member_provision_blocked_when_identity_seats_at_capacity()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantMemberSeatQuotaAssertions.VerifyProvisionBlockedWhenSeatsAtCapacityAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_invitation_accept_blocked_when_identity_seats_at_capacity()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantMemberSeatQuotaAssertions.VerifyAcceptInvitationBlockedWhenSeatsAtCapacityAsync(factory);
    }
}
