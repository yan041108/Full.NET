using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class WebhookDeliveryApiSqlServerTests
{
    [TestMethod]
    public async Task WebhookDelivery_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await WebhookDeliveryAssertions.VerifyAsync(factory);
    }
}