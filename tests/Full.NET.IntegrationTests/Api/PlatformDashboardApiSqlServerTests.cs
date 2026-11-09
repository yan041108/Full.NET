using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：PlatformDashboardApiSqlServer。</summary>
[TestClass]
public sealed class PlatformDashboardApiSqlServerTests
{
    [TestMethod]
    public async Task Host_dashboard_summary_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?> { ["Auditing:AccessLogCapture:Enabled"] = "true" });

        await PlatformHostDashboardAssertions.VerifyAsync(factory);
    }
}
