using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class TenancyApiSqlServerTests
{
    [TestMethod]
    public async Task Api_resolves_tenant_and_returns_standard_http_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await TenancyApiAssertions.VerifyAsync(factory);
    }
}
