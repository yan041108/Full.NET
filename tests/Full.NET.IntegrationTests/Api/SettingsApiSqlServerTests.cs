using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Settings;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class SettingsApiSqlServerTests
{
    [TestMethod]
    public async Task Host_dict_type_management_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await SettingsDictTypeManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_config_entry_management_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await SettingsConfigEntryManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_enum_catalog_query_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await SettingsEnumCatalogAssertions.VerifyAsync(factory);
    }
}
