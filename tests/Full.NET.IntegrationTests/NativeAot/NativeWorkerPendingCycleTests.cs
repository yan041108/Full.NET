using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Files;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实原生 Worker 在新作用域中继续三个有限首批，保留慢上传、未知消费者及未知所有者。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerPendingCycleTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Native_worker_advances_three_pending_cycles_across_scopes(DatabaseProvider provider)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason)) Assert.Inconclusive(reason ?? "Native Worker artifact unavailable.");
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, cs);
        var scenario = await PendingReconciliationScenario.PrepareAsync(provider, cs);
        try
        {
            await using var worker = await NativeWorkerProcessHost.StartAsync(artifact, provider, cs, NativeAotTestTimeouts.ProcessStartup, default,
                scenario.Upload.StorageRoot, enableFilesUploadReconciliation: true, enableFilesReferenceClaimReconciliation: true,
                additionalSettings: new Dictionary<string, string?> {
                    ["Files__UploadReconciliation__BatchSize"] = "1",
                    ["Files__ReferenceClaimReconciliation__BatchSize"] = "1",
                    ["Files__TenantResourceReconciliation__Enabled"] = "true",
                    ["Files__TenantResourceReconciliation__MinimumAgeSeconds"] = "30",
                    ["Files__TenantResourceReconciliation__PollSeconds"] = "5",
                    ["Files__TenantResourceReconciliation__BatchSize"] = "1",
                    ["Files__TenantResourceReconciliation__MaxBatchesPerRun"] = "1",
                });
            var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
            var progress = await scenario.ReadProgressAsync(provider, cs);
            while (!progress.IsComplete && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100);
                progress = await scenario.ReadProgressAsync(provider, cs);
            }
            Assert.IsTrue(progress.IsComplete, $"原生 Worker 未恢复三个后序对象：{progress}。日志：{worker.LogFilePath}");
            await scenario.AssertHeldAsync(provider, cs);
            await worker.StopGracefullyAsync(TimeSpan.FromSeconds(30), default); worker.AssertNoFatalMarkersInLogs();
        }
        finally { scenario.DeleteOwnedObjects(); }
    }
}
