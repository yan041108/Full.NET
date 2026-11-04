using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>原生产物复用同一媒体 HTTP 与真实竞争矩阵，不能由非 Linux 发现替代。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiMediaBindingTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Native_media_bindings_preserve_concurrent_winners(DatabaseProvider provider)
    {
        if (!NativeApiArtifactLocator.TryResolve(out _, out var reason)) Assert.Inconclusive(reason ?? "Native API artifact unavailable.");
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, cs, CancellationToken.None);
        await using var host = await NativeApiProcessHost.StartAsync(NativeApiArtifactLocator.RequireArtifact(), provider, cs,
            new Dictionary<string, string?>(), NativeAotTestTimeouts.ProcessStartup);
        using var client = host.CreateClient();
        var token = await NativeApiE2EAssertions.LoginAsync(client, host.LogFilePath);
        await MediaBindingHttpAssertions.VerifyAsync(client, provider, cs, token, CancellationToken.None);
        await host.StopGracefullyAsync(); host.AssertNoFatalMarkersInLogs();
    }
}
