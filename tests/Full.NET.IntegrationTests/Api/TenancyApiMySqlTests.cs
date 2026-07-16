using Full.NET.Data.Abstractions;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class TenancyApiMySqlTests
{
    [TestMethod]
    public async Task Api_resolves_tenant_and_returns_standard_http_contract()
    {
        await using var container = new MySqlBuilder("mysql:8.0")
            .WithDatabase("fullnet")
            .WithUsername("fullnet")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            container.GetConnectionString());

        await TenancyApiAssertions.VerifyAsync(factory);
    }
}
