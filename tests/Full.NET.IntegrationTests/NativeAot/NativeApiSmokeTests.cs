using System.Net;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.NativeAot;

[TestClass]
[DoNotParallelize]
public sealed class NativeApiSmokeTests
{
    [TestMethod]
    public async Task Native_artifact_starts_live_ready_and_stops_cleanly()
    {
        if (!NativeApiArtifactLocator.TryResolve(out var artifact, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native AOT artifact unavailable.");
        }

        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await NativeApiDatabaseBootstrap.BootstrapAsync(
            DatabaseProvider.SqlServer,
            connectionString);

        await using var host = await NativeApiProcessHost.StartAsync(
            artifact,
            DatabaseProvider.SqlServer,
            connectionString,
            new Dictionary<string, string?>(),
            TimeSpan.FromMinutes(2));

        using var client = host.CreateClient();
        using (var live = await client.GetAsync("/health/live"))
        {
            Assert.AreEqual(HttpStatusCode.OK, live.StatusCode);
        }

        using (var ready = await client.GetAsync("/health/ready"))
        {
            Assert.AreEqual(HttpStatusCode.OK, ready.StatusCode);
        }

        await host.StopGracefullyAsync();
        host.AssertNoFatalMarkersInLogs();
    }
}
