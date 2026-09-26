using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class ExternalDatabaseAccessPolicyTests
{
    [TestMethod]
    public void Destination_is_denied_without_deployment_allowlist()
    {
        Assert.IsFalse(ExternalDatabaseAccessPolicy.IsAllowed(CreateRequest(), new ExternalDatabaseAccessOptions()));
    }

    [TestMethod]
    public void Destination_requires_exact_host_port_provider_and_certificate_permission()
    {
        var options = new ExternalDatabaseAccessOptions
        {
            AllowedDestinations =
            [
                new ExternalDatabaseDestination
                {
                    Provider = DatabaseProvider.SqlServer,
                    Host = "db.example.com",
                    Port = 1433,
                },
            ],
        };

        Assert.IsTrue(ExternalDatabaseAccessPolicy.IsAllowed(CreateRequest(), options));
        Assert.IsFalse(ExternalDatabaseAccessPolicy.IsAllowed(CreateRequest() with { Port = 3306 }, options));
        Assert.IsFalse(ExternalDatabaseAccessPolicy.IsAllowed(CreateRequest() with { ServerHost = "other.example.com" }, options));
        Assert.IsFalse(ExternalDatabaseAccessPolicy.IsAllowed(CreateRequest() with { TrustServerCertificate = true }, options));

        options.AllowedDestinations =
        [
            new ExternalDatabaseDestination
            {
                Provider = DatabaseProvider.MySql,
                Host = "db.example.com",
                Port = 3306,
            },
        ];
        Assert.IsTrue(ExternalDatabaseAccessPolicy.IsAllowed(
            CreateRequest() with { Provider = DatabaseProvider.MySql, Port = 3306, TrustServerCertificate = true },
            options));
    }

    [TestMethod]
    public async Task Connection_factory_rejects_unlisted_target_before_opening_socket()
    {
        var factory = new ExternalDatabaseConnectionFactory(Options.Create(new ExternalDatabaseAccessOptions()));

        var result = await factory.OpenAsync(CreateRequest());

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Session);
        Assert.AreEqual("External database destination is not allowed.", result.ErrorMessage);
    }

    [TestMethod]
    public void MySql_connection_requires_verified_tls()
    {
        var request = CreateRequest() with { Provider = DatabaseProvider.MySql, Port = 3306 };

        using var connection = ExternalDatabaseConnectionFactory.CreateMySqlConnection(request);
        var builder = new MySqlConnectionStringBuilder(connection.ConnectionString);

        Assert.AreEqual(MySqlSslMode.VerifyFull, builder.SslMode);
    }

    private static ExternalDatabaseConnectionRequest CreateRequest() =>
        new(DatabaseProvider.SqlServer, "db.example.com", 1433, "reports", "reader", "secret", false, 10, "tests");
}
