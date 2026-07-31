using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Settings;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class SettingsApiMySqlTests
{
    [TestMethod]
    public async Task Host_dict_type_management_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await SettingsDictTypeManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_dict_type_management_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await SettingsTenantDictTypeManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_config_entry_management_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await SettingsConfigEntryManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_enum_catalog_query_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await SettingsEnumCatalogAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Current_user_grid_preferences_follow_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await SettingsGridPreferenceAssertions.VerifyAsync(factory);
    }
}
