using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Tenancy;

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

    [TestMethod]
    public async Task Host_tenant_management_returns_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await TenancyHostTenantManagementAssertions.VerifyAsync(factory);
    }
}
