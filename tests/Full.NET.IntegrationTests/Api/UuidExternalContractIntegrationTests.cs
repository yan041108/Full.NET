using Full.NET.Data.Abstractions;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class UuidExternalContractIntegrationTests
{
    [TestMethod]
    public async Task SqlServer_public_api_json_uses_canonical_uuid_strings()
    {
        await using var container = new MsSqlBuilder(
                "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            container.GetConnectionString());

        await UuidExternalContractAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task MySql_binary16_public_api_json_uses_canonical_uuid_strings()
    {
        await using var container = new MySqlBuilder("mysql:8.0")
            .WithCommand("--log-bin-trust-function-creators=1")
            .WithDatabase("fullnet")
            .WithUsername("fullnet")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            container.GetConnectionString());

        await UuidExternalContractAssertions.VerifyAsync(factory);
    }
}
