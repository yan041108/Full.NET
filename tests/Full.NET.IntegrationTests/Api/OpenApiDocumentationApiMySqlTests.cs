using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：OpenApiDocumentationApiMySql。</summary>
[TestClass]
public sealed class OpenApiDocumentationApiMySqlTests
{
    [TestMethod]
    public async Task OpenApi_documentation_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            // 与 SQL Server 导出相同模块集合，不能静默删掉 manifest 中的操作。
            includeEnterpriseRequest: true);
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        await OpenApiDocumentationAssertions.VerifyAsync(client);
        await OpenApiClientSnapshotContractAssertions.VerifyAsync(
            client,
            DatabaseProvider.MySql);
    }
}
