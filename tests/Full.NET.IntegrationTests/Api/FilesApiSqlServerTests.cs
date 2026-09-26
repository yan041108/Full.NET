using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Files;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class FilesApiSqlServerTests
{
    [TestMethod]
    public async Task Host_file_management_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await FilesHostFileManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_resource_upload_blocked_when_storage_quota_exhausted()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await TenantResourceFileStorageQuotaAssertions.VerifyUploadBlockedWhenStorageQuotaExhaustedAsync(
            factory);
    }
}
