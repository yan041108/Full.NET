using System.Net;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.NativeAot;

[TestClass]
[DoNotParallelize]
public sealed class NativeApiSmokeTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Native_artifact_starts_live_ready_and_stops_cleanly(DatabaseProvider provider)
    {
        if (!NativeApiArtifactLocator.TryResolve(out var artifact, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native AOT artifact unavailable.");
        }

        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NativeApiDatabaseBootstrap.BootstrapAsync(
            provider,
            connectionString);

        await using var host = await NativeApiProcessHost.StartAsync(
            artifact,
            provider,
            connectionString,
            new Dictionary<string, string?>(),
            NativeAotTestTimeouts.ProcessStartup);

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
        Assert.AreEqual(0, host.ExitCode);
        var logs = await File.ReadAllTextAsync(host.LogFilePath);
        StringAssert.Contains(logs, "Application is shutting down...");
        Assert.IsFalse(logs.Contains("B1 micro-batch loop failed", StringComparison.Ordinal), logs);
        Assert.IsFalse(logs.Contains("B1 micro-batch shutdown drain failed open", StringComparison.Ordinal), logs);
    }
}
