using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Files;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class FilesApiMySqlTests
{
    [TestMethod]
    public async Task Host_file_management_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await FilesHostFileManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_resource_upload_blocked_when_storage_quota_exhausted()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenantResourceFileStorageQuotaAssertions.VerifyUploadBlockedWhenStorageQuotaExhaustedAsync(
            factory);
    }
}
