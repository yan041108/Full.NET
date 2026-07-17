using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;
using Testcontainers.MsSql;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class IdentityApiSqlServerTests
{
    [TestMethod]
    public async Task Login_and_current_user_follow_secure_http_contract()
    {
        await using var container = new MsSqlBuilder(
                "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            container.GetConnectionString());

        await IdentityApiAssertions.VerifyLoginAsync(factory);
    }

    [TestMethod]
    public async Task Locale_preference_is_persisted_with_sql_server()
    {
        await using var container = new MsSqlBuilder(
                "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            container.GetConnectionString());

        await LocalePreferenceTests.VerifyAsync(factory);
    }
}
