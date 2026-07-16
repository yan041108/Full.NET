using Full.NET.Data.Abstractions;
using Testcontainers.MsSql;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class TenancyApiSqlServerTests
{
    [TestMethod]
    public async Task Api_resolves_tenant_and_returns_standard_http_contract()
    {
        await using var container = new MsSqlBuilder(
                "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            container.GetConnectionString());

        await TenancyApiAssertions.VerifyAsync(factory);
    }
}
