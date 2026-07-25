using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class RealtimeApiMySqlTests
{
    [TestMethod]
    public async Task Realtime_hub_and_probe_follow_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await Realtime.RealtimeApiAssertions.VerifyAsync(factory);
    }
}
