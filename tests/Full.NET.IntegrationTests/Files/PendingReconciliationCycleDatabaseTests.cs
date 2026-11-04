using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Document;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Files.Reconciliation;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Files;

/// <summary>真实双库集中验证三条有限扫描跨作用域推进与固定周期上界。</summary>
[TestClass]
[DoNotParallelize]
public sealed class PendingReconciliationCycleDatabaseTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Three_pending_cycles_progress_across_scopes_without_extending_the_cycle(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, cs);
        var scenario = await PendingReconciliationScenario.PrepareAsync(provider, cs);
        try
        {
            var clock = new MutableClock { UtcNow = DateTimeOffset.UtcNow };
            var upload = Guid.CreateVersion7(); var claim = Guid.CreateVersion7(); var resource = Guid.CreateVersion7();
            await using var db = DocumentReleaseTestConnections.Create(provider, cs); await db.OpenAsync();
            object future = provider == DatabaseProvider.MySql ? clock.UtcNow.AddHours(1).UtcDateTime : clock.UtcNow.AddHours(1);
            var uploadKey = $"native-aot/{upload:N}.bin"; var resourceKey = $"native-aot/{resource:N}.bin";
            await db.ExecuteAsync("""
                INSERT INTO fn_files_file
                    (Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, StorageKey, StorageState, CreatedAtUtc, CreatedByUserId)
                SELECT @Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, @Key, 'pending', @Future, CreatedByUserId
                FROM fn_files_file WHERE Id = @Source
                """, new { Id = upload, Key = uploadKey, Future = future, Source = scenario.Upload.ExistingId });
            await db.ExecuteAsync("""
                INSERT INTO fn_files_file_reference_claim
                    (Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId, State, SizeBytes, CreatedAtUtc, UpdatedAtUtc)
                SELECT @Id, @Key, FileId, ConsumerModule, ConsumerReferenceId, 'pending', SizeBytes, @Future, @Future
                FROM fn_files_file_reference_claim WHERE Id = @Source
                """, new { Id = claim, Key = "pending-cycle-" + claim.ToString("N"), Future = future, Source = scenario.ClaimGood });
            await db.ExecuteAsync("""
                INSERT INTO fn_files_tenant_resource_file
                    (Id, TenantId, OwnerModuleKey, ResourceId, OriginalFileName, ContentType, SizeBytes, ContentHash,
                     ProviderKey, StorageKey, StatusKey, CreatedByUserId, CreatedAtUtc)
                SELECT @Id, TenantId, OwnerModuleKey, ResourceId, OriginalFileName, ContentType, SizeBytes, ContentHash,
                       ProviderKey, @Key, 'pending', CreatedByUserId, @Future
                FROM fn_files_tenant_resource_file WHERE Id = @Source
                """, new { Id = resource, Key = resourceKey, Future = future, Source = scenario.TenantGood });
            foreach (var key in new[] { uploadKey, resourceKey })
                await File.WriteAllBytesAsync(Path.Combine(scenario.Upload.StorageRoot, key.Replace('/', Path.DirectorySeparatorChar)), [1, 2, 3]);
            using var factory = new FullNetApiFactory(provider, cs, new Dictionary<string, string?>
                { ["Files:Local:RootPath"] = scenario.Upload.StorageRoot }, configureTestServices: services =>
                {
                    services.AddSingleton<IClock>(clock);
                    // 主动调用后台组件，不启动循环，避免验收断言被并发扫描改变。
                    services.AddSingleton<PendingHostFileReconciliationCursor>();
                    services.AddSingleton<PendingHostFileReferenceClaimReconciliationCursor>();
                    services.AddSingleton<TenantResourceFileReconciliationCursor>();
                    services.AddScoped<PendingHostFileReconciliationRunner>();
                    services.AddScoped<PendingHostFileReferenceClaimReconciliationRunner>();
                    services.AddScoped<PendingTenantResourceFileReconciliationRunner>();
                });
            await factory.InitializeAsync();
            foreach (var kind in new[] { "upload", "claim", "tenant" }) Assert.AreEqual(1, await RunAsync(kind));
            Assert.IsFalse(await scenario.IsCompleteAsync(provider, cs));
            clock.UtcNow = clock.UtcNow.AddHours(2);
            Assert.AreEqual(1, await RunAsync("upload")); Assert.AreEqual(1, await RunAsync("upload"));
            Assert.AreEqual(0, await RunAsync("upload"));
            foreach (var kind in new[] { "claim", "tenant" })
            { Assert.AreEqual(1, await RunAsync(kind)); Assert.AreEqual(0, await RunAsync(kind)); }
            Assert.IsTrue(await scenario.IsCompleteAsync(provider, cs));
            await scenario.AssertHeldAsync(provider, cs);
            Assert.AreEqual("pending", await StateAsync("upload"));
            Assert.AreEqual("pending", await StateAsync("claim"));
            Assert.AreEqual("pending", await StateAsync("tenant"));
            // 新周期包含后来进入年龄窗口的记录，旧的保留项也会重新探测。
            foreach (var kind in new[] { "upload", "claim", "tenant" })
                for (var i = 0; i < 3; i++) await RunAsync(kind);
            Assert.AreEqual("ready", await StateAsync("upload"));
            Assert.AreEqual("released", await StateAsync("claim"));
            Assert.AreEqual("ready", await StateAsync("tenant"));
            await scenario.AssertHeldAsync(provider, cs);

            async Task<int> RunAsync(string kind)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>(); tenant.SetHost();
                try
                {
                    return kind switch
                    {
                        "upload" => (await scope.ServiceProvider.GetRequiredService<PendingHostFileReconciliationRunner>()
                            .RunOnceAsync(new() { Enabled = true, BatchSize = 1, MaxBatchesPerRun = 1, MinimumAgeSeconds = 30 }, default)).Scanned,
                        "claim" => (await scope.ServiceProvider.GetRequiredService<PendingHostFileReferenceClaimReconciliationRunner>()
                            .RunOnceAsync(new() { Enabled = true, BatchSize = 1, MaxBatchesPerRun = 1, MinimumAgeSeconds = 30, ReleaseGraceSeconds = 60 }, default)).Scanned,
                        _ => (await scope.ServiceProvider.GetRequiredService<PendingTenantResourceFileReconciliationRunner>()
                            .RunOnceAsync(new() { Enabled = true, BatchSize = 1, MaxBatchesPerRun = 1, MinimumAgeSeconds = 30 }, default)).Scanned,
                    };
                }
                finally { tenant.Clear(); }
            }

            Task<string> StateAsync(string kind) => kind switch
            {
                "upload" => db.QuerySingleAsync<string>("SELECT StorageState FROM fn_files_file WHERE Id = @Id", new { Id = upload }),
                "claim" => db.QuerySingleAsync<string>("SELECT State FROM fn_files_file_reference_claim WHERE Id = @Id", new { Id = claim }),
                _ => db.QuerySingleAsync<string>("SELECT StatusKey FROM fn_files_tenant_resource_file WHERE Id = @Id AND TenantId = @Tenant", new { Id = resource, Tenant = scenario.TenantId }),
            };
        }
        finally { scenario.DeleteOwnedObjects(); }
    }

    private sealed class MutableClock : IClock { public DateTimeOffset UtcNow { get; set; } }
}
