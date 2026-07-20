using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class UuidExternalContractIntegrationTests
{
    [TestMethod]
    public async Task SqlServer_public_api_json_uses_canonical_uuid_strings()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await UuidExternalContractAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task MySql_binary16_public_api_json_uses_canonical_uuid_strings()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await UuidExternalContractAssertions.VerifyAsync(factory);
    }
}
