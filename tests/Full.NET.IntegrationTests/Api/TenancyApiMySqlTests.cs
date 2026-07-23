using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class TenancyApiMySqlTests
{
    [TestMethod]
    public async Task Anonymous_current_tenant_endpoint_returns_minimal_standard_http_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await TenancyApiAssertions.VerifyAsync(factory);
    }
}
