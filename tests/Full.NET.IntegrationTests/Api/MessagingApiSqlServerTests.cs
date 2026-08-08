using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Messaging;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class MessagingApiSqlServerTests
{
    [TestMethod]
    public async Task Host_messaging_operations_follow_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?>
            {
                ["Messaging:Outbox:Mode"] = "AppendOnlyV2",
            },
            configureTestServices: MessagingOperationsAssertions.ConfigureTestServices);

        await MessagingOperationsAssertions.VerifyAsync(factory);
    }
}
