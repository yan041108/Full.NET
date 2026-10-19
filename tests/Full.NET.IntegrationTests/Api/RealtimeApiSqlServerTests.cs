using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：RealtimeApiSqlServer。</summary>
[TestClass]
public sealed class RealtimeApiSqlServerTests
{
    [TestMethod]
    public async Task Realtime_hub_and_probe_follow_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await Realtime.RealtimeApiAssertions.VerifyAsync(factory);
    }
}
