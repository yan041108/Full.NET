using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class PlatformDashboardApiSqlServerTests
{
    [TestMethod]
    public async Task Host_dashboard_summary_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await PlatformHostDashboardAssertions.VerifyAsync(factory);
    }
}
