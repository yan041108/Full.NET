using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Reporting;

/// <summary>MySQL 上的报表导出任务租户领取、并发争抢与到期重领回归。</summary>
[TestClass]
public sealed class ReportingExportTaskClaimPersistenceMySqlTests
{
    [TestMethod]
    public Task Cancellation_is_scoped_and_fenced() => ReportingExportTaskClaimPersistenceAssertions.Cancellation_is_scoped_and_fenced_async(DatabaseProvider.MySql);
    [TestMethod]
    public Task Cancellation_and_claim_have_one_winner() => ReportingExportTaskClaimPersistenceAssertions.Cancellation_and_claim_have_one_winner_async(DatabaseProvider.MySql);
    [TestMethod]
    public Task Cancellation_preserves_processing_lease() => ReportingExportTaskClaimPersistenceAssertions.Cancellation_preserves_processing_lease_async(DatabaseProvider.MySql);

    [TestMethod]
    public Task Claim_is_isolated_to_current_tenant() =>
        ReportingExportTaskClaimPersistenceAssertions.Claim_is_isolated_to_current_tenant_async(DatabaseProvider.MySql);

    [TestMethod]
    public Task Concurrent_claim_admits_only_one_owner() =>
        ReportingExportTaskClaimPersistenceAssertions.Concurrent_claim_admits_only_one_owner_async(DatabaseProvider.MySql);

    [TestMethod]
    public Task Expired_processing_lease_can_be_reclaimed() =>
        ReportingExportTaskClaimPersistenceAssertions.Expired_processing_lease_can_be_reclaimed_async(DatabaseProvider.MySql);
}
