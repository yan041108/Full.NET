using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Document;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class DocumentApiMySqlTests
{
    [TestMethod]
    public async Task Host_document_items_follow_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await DocumentHostItemAssertions.VerifyAsync(factory);
    }
}
