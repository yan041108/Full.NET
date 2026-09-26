using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantFeatureEntitlementPortTests
{
    [TestMethod]
    public async Task IsFeatureGrantedAsync_allows_in_compatibility_phase_without_binding()
    {
        var tenantId = Guid.CreateVersion7();
        var port = CreatePort(
            TenantEntitlementEnforcementPhases.Compatibility,
            bindingCount: 0,
            catalogActive: true);

        var result = await port.IsFeatureGrantedAsync(
            tenantId,
            TenantEntitlementCatalogCodes.Workflow,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Value);
    }

    [TestMethod]
    public async Task IsFeatureGrantedAsync_denies_in_enforced_phase_without_binding()
    {
        var tenantId = Guid.CreateVersion7();
        var port = CreatePort(
            TenantEntitlementEnforcementPhases.Enforced,
            bindingCount: 0,
            catalogActive: true);

        var result = await port.IsFeatureGrantedAsync(
            tenantId,
            TenantEntitlementCatalogCodes.Workflow,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.EntitlementFeatureNotGranted, result.Error!.Code);
    }

    [TestMethod]
    public async Task IsFeatureGrantedAsync_allows_in_enforced_phase_with_active_binding()
    {
        var tenantId = Guid.CreateVersion7();
        var port = CreatePort(
            TenantEntitlementEnforcementPhases.Enforced,
            bindingCount: 1,
            catalogActive: true);

        var result = await port.IsFeatureGrantedAsync(
            tenantId,
            TenantEntitlementCatalogCodes.Workflow,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Value);
    }

    private static TenantFeatureEntitlementPort CreatePort(
        string phase,
        long bindingCount,
        bool catalogActive)
    {
        var queries = Substitute.For<IQueryExecutor>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        queries.QuerySingleOrDefaultAsync<TenantEntitlementCatalogRecord>(
                TenantEntitlementSql.FindCatalogByCode,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(new TenantEntitlementCatalogRecord(
                Guid.CreateVersion7(),
                TenantEntitlementCatalogCodes.Workflow,
                "Workflow",
                null,
                TenantEntitlementTypes.Feature,
                catalogActive,
                1));
        queries.QuerySingleOrDefaultAsync<long>(
                TenantEntitlementSql.CountActiveFeatureBindings,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(bindingCount);
        queries.QuerySingleOrDefaultAsync<TenantEntitlementEnforcementRecord>(
                TenantEntitlementSql.GetEnforcementPhase,
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(new TenantEntitlementEnforcementRecord(phase, 1));

        var entitlementQueries = new TenantEntitlementQueryService(queries);
        var tenantWriter = new Full.NET.Abstractions.Tenancy.CurrentTenantAccessor();
        tenantWriter.SetHost();
        return new TenantFeatureEntitlementPort(queries, entitlementQueries, tenantWriter, clock);
    }
}
