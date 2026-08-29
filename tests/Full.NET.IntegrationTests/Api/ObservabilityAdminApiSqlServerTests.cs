using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class ObservabilityAdminApiSqlServerTests
{
    [TestMethod]
    public async Task Log_control_plane_follows_contract_with_sql_server()
    {
        var logRoot = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-observability-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(logRoot);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(logRoot, "api.log"),
                "first\nsecond\nthird\n");
            using var factory = new FullNetApiFactory(
                DatabaseProvider.SqlServer,
                await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
                new Dictionary<string, string?>
                {
                    ["FullNet:ObservabilityAdmin:LogRootPath"] = logRoot,
                });

            await ObservabilityAdminApiAssertions.VerifyAsync(factory);
        }
        finally
        {
            Directory.Delete(logRoot, recursive: true);
        }
    }
}
