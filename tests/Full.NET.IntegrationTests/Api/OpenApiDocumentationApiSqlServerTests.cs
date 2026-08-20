using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class OpenApiDocumentationApiSqlServerTests
{
    [TestMethod]
    public async Task OpenApi_documentation_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        await OpenApiDocumentationAssertions.VerifyAsync(client);
        await OpenApiClientSnapshotContractAssertions.VerifyAsync(
            client,
            DatabaseProvider.SqlServer);
    }
}
