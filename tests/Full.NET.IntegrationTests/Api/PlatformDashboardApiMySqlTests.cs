using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class PlatformDashboardApiMySqlTests
{
    [TestMethod]
    public async Task Host_dashboard_summary_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await PlatformHostDashboardAssertions.VerifyAsync(factory);
    }
}
