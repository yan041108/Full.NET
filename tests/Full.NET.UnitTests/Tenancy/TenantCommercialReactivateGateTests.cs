using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;
using Full.NET.Modules.Tenancy.Features.ManageTenantLifecycle;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantCommercialReactivateGateTests
{
    [TestMethod]
    public async Task ValidateAsync_skips_when_commercial_reactivate_gate_disabled()
    {
        var gate = CreateGate(
            requirePackageOrSubscription: false,
            TenantEntitlementEnforcementPhases.Enforced,
            subscriptions: []);

        var result = await gate.ValidateAsync(SuspendedTenant(), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task ValidateAsync_allows_reactivate_in_compatibility_without_package()
    {
        var gate = CreateGate(
            requirePackageOrSubscription: true,
            TenantEntitlementEnforcementPhases.Compatibility,
            subscriptions: []);

        var result = await gate.ValidateAsync(SuspendedTenant(), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task ValidateAsync_blocks_reactivate_in_enforced_without_commercial_binding()
    {
        var gate = CreateGate(
            requirePackageOrSubscription: true,
            TenantEntitlementEnforcementPhases.Enforced,
            subscriptions: []);

        var result = await gate.ValidateAsync(SuspendedTenant(), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.EntitlementCommercialBindingRequired, result.Error!.Code);
    }

    [TestMethod]
    public async Task ValidateAsync_allows_reactivate_in_enforced_when_tenant_has_package()
    {
        var gate = CreateGate(
            requirePackageOrSubscription: true,
            TenantEntitlementEnforcementPhases.Enforced,
            subscriptions: []);

        var result = await gate.ValidateAsync(
            SuspendedTenant(tenantPackageId: Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task ValidateAsync_allows_reactivate_in_enforced_with_active_subscription()
    {
        var tenantId = Guid.CreateVersion7();
        var gate = CreateGate(
            requirePackageOrSubscription: true,
            TenantEntitlementEnforcementPhases.Enforced,
            subscriptions:
            [
                new TenantSubscriptionRecord(
                    Guid.CreateVersion7(),
                    tenantId,
                    Guid.CreateVersion7(),
                    TenantSubscriptionStatuses.Active,
                    null,
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddDays(29),
                    null,
                    1),
            ]);

        var result = await gate.ValidateAsync(SuspendedTenant(tenantId), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
    }

    private static TenantCommercialReactivateGate CreateGate(
        bool requirePackageOrSubscription,
        string phase,
        IReadOnlyList<TenantSubscriptionRecord> subscriptions)
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor
            .QuerySingleOrDefaultAsync<TenantEntitlementEnforcementRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new TenantEntitlementEnforcementRecord(phase, 1));
        queryExecutor
            .QueryAsync<TenantSubscriptionRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(subscriptions);

        var queries = new TenantEntitlementQueryService(queryExecutor);
        return new TenantCommercialReactivateGate(
            Options.Create(new TenancyCommercialOptions
            {
                RequirePackageOrSubscriptionOnReactivate = requirePackageOrSubscription,
            }),
            queries,
            queryExecutor);
    }

    private static TenantSummary SuspendedTenant(
        Guid? tenantId = null,
        Guid? tenantPackageId = null) =>
        new(
            tenantId ?? Guid.CreateVersion7(),
            "probe",
            "Probe Tenant",
            "probe.localhost",
            IsActive: false,
            Version: 1,
            TenantPackageId: tenantPackageId,
            LifecycleStatus: TenantLifecycleStatuses.Suspended);
}
