using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Files;

/// <summary>SQL Server 上的租户资源文件所有权、ListReady 范围与 pending 对账边界回归。</summary>
[TestClass]
public sealed class TenantResourceFilePersistenceSqlServerTests
{
    [TestMethod]
    public Task Owned_access_requires_tenant_module_and_resource() =>
        TenantResourceFilePersistenceAssertions.Owned_access_requires_tenant_module_and_resource_async(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task List_ready_is_scoped_to_owner_resource() =>
        TenantResourceFilePersistenceAssertions.List_ready_is_scoped_to_owner_resource_async(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task Pending_promote_and_purge_stay_owner_bound() =>
        TenantResourceFilePersistenceAssertions.Pending_promote_and_purge_stay_owner_bound_async(DatabaseProvider.SqlServer);
}
