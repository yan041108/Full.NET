using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class IdentityApiMySqlTests
{
    [TestMethod]
    public async Task Login_and_current_user_follow_secure_http_contract()
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

        await IdentityApiAssertions.VerifyLoginAsync(factory);
    }

    [TestMethod]
    public async Task Locale_preference_is_persisted_with_mysql()
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

        await LocalePreferenceTests.VerifyAsync(factory);
    }
}
