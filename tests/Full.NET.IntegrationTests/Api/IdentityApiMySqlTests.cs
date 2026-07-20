using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class IdentityApiMySqlTests
{
    [TestMethod]
    public async Task Login_and_current_user_follow_secure_http_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await IdentityApiAssertions.VerifyLoginAsync(factory);
    }

    [TestMethod]
    public async Task Locale_preference_is_persisted_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await LocalePreferenceTests.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Last_super_administrator_is_protected_under_mysql_concurrency()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await SuperAdministratorConcurrencyAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Session_refresh_and_context_switch_races_are_linearized()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await SessionRaceAssertions.VerifyAsync(factory);
    }
}
