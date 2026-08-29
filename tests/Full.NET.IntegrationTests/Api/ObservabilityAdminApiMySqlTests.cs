using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class ObservabilityAdminApiMySqlTests
{
    [TestMethod]
    public async Task Log_control_plane_follows_contract_with_mysql()
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
                DatabaseProvider.MySql,
                await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
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
