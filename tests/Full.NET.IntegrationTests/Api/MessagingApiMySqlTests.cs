using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Messaging;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class MessagingApiMySqlTests
{
    [TestMethod]
    public async Task Host_messaging_operations_follow_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            new Dictionary<string, string?>
            {
                ["Messaging:Outbox:Mode"] = "AppendOnlyV2",
                ["Database:MySqlGuidStorageMode"] = "Binary16",
            },
            configureTestServices: MessagingOperationsAssertions.ConfigureTestServices);

        await MessagingOperationsAssertions.VerifyAsync(factory);
    }
}
