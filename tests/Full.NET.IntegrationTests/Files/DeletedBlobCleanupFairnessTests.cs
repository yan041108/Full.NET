using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Document;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Files.Cleanup;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Files;

/// <summary>真实双库与独立 Worker 作用域验证首批失败后仍能回收后序本地对象。</summary>
[TestClass]
[DoNotParallelize]
public sealed class DeletedBlobCleanupFairnessTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Bounded_cleanup_keeps_failed_tombstone_and_progresses_across_scopes(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, cs);
        var scenario = await NativeWorkerFilesProbe.PrepareCleanupAsync(provider, cs);
        try
        {
            var clock = new MutableClock { UtcNow = DateTimeOffset.UtcNow };
            var futureId = Guid.CreateVersion7();
            await using var db = DocumentReleaseTestConnections.Create(provider, cs); await db.OpenAsync();
            object future = provider == DatabaseProvider.MySql ? clock.UtcNow.AddHours(1).UtcDateTime : clock.UtcNow.AddHours(1);
            // 本周期开始后才进入扫描上界的墓碑必须等下一周期，不能延长失败记录的重试等待。
            await db.ExecuteAsync("""
                INSERT INTO fn_files_file
                    (Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, StorageKey, StorageState, CreatedAtUtc, CreatedByUserId, DeletedAtUtc)
                SELECT @Id, TenantId, OriginalFileName, ContentType, SizeBytes, 'local', @StorageKey, StorageState, CreatedAtUtc, CreatedByUserId, @Future
                FROM fn_files_file WHERE Id = @Source
                """, new { Id = futureId, StorageKey = $"native-aot/{futureId:N}.bin", Future = future, Source = scenario.UnavailableProviderId });
            using var factory = new FullNetApiFactory(provider, cs, new Dictionary<string, string?>
                { ["Files:Local:RootPath"] = scenario.StorageRoot }, configureTestServices: services =>
                {
                    services.AddSingleton<IClock>(clock);
                    // API 工厂只装配主动调用的 Worker 组件，避免启动后台循环干扰断言。
                    services.AddSingleton<DeletedHostFileBlobCleanupCursor>();
                    services.AddScoped<DeletedHostFileBlobCleanupRunner>();
                });
            await factory.InitializeAsync();
            var options = new DeletedHostFileBlobCleanupOptions { Enabled = true, BatchSize = 1, MaxBatchesPerRun = 1 };
            var first = await RunAsync(); Assert.AreEqual(1, first.Scanned); Assert.AreEqual(1, first.BlobFailures);
            Assert.IsTrue(File.Exists(scenario.LocalBlobPath));
            clock.UtcNow = clock.UtcNow.AddHours(2);
            var second = await RunAsync(); Assert.AreEqual(1, second.Purged, "新作用域不能从失败墓碑重置扫描位置。");
            Assert.IsFalse(File.Exists(scenario.LocalBlobPath));
            Assert.AreEqual(0, (await RunAsync()).Scanned, "空页结束固定扫描周期。");
            Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file WHERE Id = @Id", new { Id = futureId }));
            Assert.AreEqual(1, (await RunAsync()).BlobFailures, "保留的未知 Provider 墓碑仍需在下一周期重试。");
            Assert.AreEqual(1, (await RunAsync()).Purged, "新周期才能处理此前上界以外的墓碑。");
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file WHERE Id = @Id", new { Id = futureId }));
            var states = await NativeWorkerFilesProbe.WaitForCleanupTerminalStatesAsync(provider, cs, scenario,
                TimeSpan.FromSeconds(5), "direct-runner");
            Assert.AreEqual(0, states.IsLocalPresent); Assert.AreEqual(1, states.IsUnavailableProviderPresent);

            async Task<DeletedHostFileBlobCleanupResult> RunAsync()
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>(); tenant.SetHost();
                try { return await scope.ServiceProvider.GetRequiredService<DeletedHostFileBlobCleanupRunner>().RunOnceAsync(options, default); }
                finally { tenant.Clear(); }
            }
        }
        finally { NativeWorkerFilesProbe.DeleteOwnedStorageRoot(scenario.StorageRoot); }
    }

    private sealed class MutableClock : IClock { public DateTimeOffset UtcNow { get; set; } }
}
