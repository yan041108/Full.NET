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
}
