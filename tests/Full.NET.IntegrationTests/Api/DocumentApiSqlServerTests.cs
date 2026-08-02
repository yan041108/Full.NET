using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Document;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class DocumentApiSqlServerTests
{
    [TestMethod]
    public async Task Host_document_items_follow_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await DocumentHostItemAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_document_categories_and_tags_follow_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await DocumentHostCategoryTagAssertions.VerifyAsync(factory);
    }
}
