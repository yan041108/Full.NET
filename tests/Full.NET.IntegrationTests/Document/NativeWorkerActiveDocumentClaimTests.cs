using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Document;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>原生 Worker 跨轮扫描超过保留的首批，补偿历史孤儿且保留回收站、可复用及新鲜引用。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerActiveDocumentClaimTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task NativeWorker_reconciles_only_immutable_active_document_orphans(DatabaseProvider provider)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason)) Assert.Inconclusive(reason ?? "Native Worker artifact unavailable.");
        var ct = CancellationToken.None;
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, cs, ct);
        await using var db = DocumentReleaseTestConnections.Create(provider, cs); await db.OpenAsync(ct);
        var seed = await DocumentActiveClaimSeed.CreateAsync(db, provider, ct);
        await using var host = await NativeWorkerProcessHost.StartAsync(artifact, provider, cs, NativeAotTestTimeouts.ProcessStartup, ct,
            additionalSettings: new Dictionary<string, string?>
            {
                ["Files__ActiveReferenceClaimReconciliation__Enabled"] = "true",
                ["Files__ActiveReferenceClaimReconciliation__PollSeconds"] = "5",
                ["Files__ActiveReferenceClaimReconciliation__BatchSize"] = "1",
                ["Files__ActiveReferenceClaimReconciliation__MaxBatchesPerRun"] = "1",
                ["Files__ActiveReferenceClaimReconciliation__MinimumAgeSeconds"] = "900",
            });
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30); var state = "active";
        while (DateTimeOffset.UtcNow < deadline)
        {
            state = await seed.StateAsync(db, seed.OrphanClaim, ct);
            if (state == "released") break;
            await Task.Delay(100, ct);
        }
        Assert.AreEqual("released", state);
        foreach (var id in new[] { seed.RetainedClaim, seed.MutableClaim, seed.FreshClaim })
            Assert.AreEqual("active", await seed.StateAsync(db, id, ct));
        await host.StopGracefullyAsync(TimeSpan.FromSeconds(30), ct); host.AssertNoFatalMarkersInLogs();
    }

}
