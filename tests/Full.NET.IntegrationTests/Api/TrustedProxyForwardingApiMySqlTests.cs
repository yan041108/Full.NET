using System.Net;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class TrustedProxyForwardingApiMySqlTests
{
    [TestMethod]
    public async Task Trusted_forwarding_updates_origin_and_audit_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            TrustedProxySettings(),
            IPAddress.Loopback);
        await TrustedProxyForwardingAssertions.VerifyAuthenticationAuditAsync(factory);
    }

    private static IReadOnlyDictionary<string, string?> TrustedProxySettings() =>
        new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:ForwardLimit"] = "1",
            ["TrustedProxy:KnownProxies:0"] = "127.0.0.1",
            ["Identity:AllowedOrigins:0"] = "https://unrelated.example",
        };
}
