using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class GlobalApiRateLimitApiSqlServerTests
{
    [TestMethod]
    public async Task Global_api_rate_limit_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?>
            {
                ["RateLimiting:EnableGlobalApiLimit"] = "true",
                ["RateLimiting:GlobalApiPermitLimitPerMinute"] = "3",
            });
        await GlobalApiRateLimitAssertions.VerifyAsync(factory);
    }
}
