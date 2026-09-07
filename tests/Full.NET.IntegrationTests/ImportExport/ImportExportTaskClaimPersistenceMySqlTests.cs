using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.ImportExport;

/// <summary>MySQL 上的导入任务租户领取、并发争抢与到期重领回归。</summary>
[TestClass]
public sealed class ImportExportTaskClaimPersistenceMySqlTests
{
    [TestMethod]
    public Task Claim_is_isolated_to_current_tenant() =>
        ImportExportTaskClaimPersistenceAssertions.Claim_is_isolated_to_current_tenant_async(DatabaseProvider.MySql);

    [TestMethod]
    public Task Concurrent_claim_admits_only_one_owner() =>
        ImportExportTaskClaimPersistenceAssertions.Concurrent_claim_admits_only_one_owner_async(DatabaseProvider.MySql);

    [TestMethod]
    public Task Expired_executing_lease_can_be_reclaimed() =>
        ImportExportTaskClaimPersistenceAssertions.Expired_executing_lease_can_be_reclaimed_async(DatabaseProvider.MySql);
}
