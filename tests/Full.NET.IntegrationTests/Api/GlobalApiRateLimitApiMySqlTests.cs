using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class GlobalApiRateLimitApiMySqlTests
{
    [TestMethod]
    public async Task Global_api_rate_limit_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            new Dictionary<string, string?>
            {
                ["RateLimiting:EnableGlobalApiLimit"] = "true",
                ["RateLimiting:GlobalApiPermitLimitPerMinute"] = "3",
            });
        await GlobalApiRateLimitAssertions.VerifyAsync(factory);
    }
}
