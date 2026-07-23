using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class TenancyApiSqlServerTests
{
    [TestMethod]
    public async Task Anonymous_current_tenant_endpoint_returns_minimal_standard_http_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await TenancyApiAssertions.VerifyAsync(factory);
    }
}
