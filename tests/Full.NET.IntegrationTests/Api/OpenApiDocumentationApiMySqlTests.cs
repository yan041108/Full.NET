using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class OpenApiDocumentationApiMySqlTests
{
    [TestMethod]
    public async Task OpenApi_documentation_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        await OpenApiDocumentationAssertions.VerifyAsync(client);
    }
}
