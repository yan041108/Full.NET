using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：OpenApiDocumentationApiSqlServer。</summary>
[TestClass]
public sealed class OpenApiDocumentationApiSqlServerTests
{
    [TestMethod]
    public async Task OpenApi_documentation_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            // 客户端 manifest 包含示例应用操作，导出工厂必须按相同模块集合组合。
            includeEnterpriseRequest: true);
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        await OpenApiDocumentationAssertions.VerifyAsync(client);
        await OpenApiClientSnapshotContractAssertions.VerifyAsync(
            client,
            DatabaseProvider.SqlServer);
    }
}
