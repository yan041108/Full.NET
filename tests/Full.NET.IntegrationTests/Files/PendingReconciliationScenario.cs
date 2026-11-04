using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Document;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Files;

/// <summary>三条链路共享同一自有双库场景：首批保留，后序记录必须恢复。</summary>
internal sealed record PendingReconciliationScenario(NativeWorkerFilesScenario Upload, Guid UploadHeld,
    Guid ClaimHeld, Guid ClaimGood, Guid TenantId, Guid TenantHeld, Guid TenantGood)
{
    internal static async Task<PendingReconciliationScenario> PrepareAsync(DatabaseProvider provider, string cs)
    {
        var upload = await NativeWorkerFilesProbe.PrepareAsync(provider, cs);
        try
        {
            var uploadHeld = Guid.CreateVersion7(); var claimHeld = Guid.CreateVersion7(); var claimGood = Guid.CreateVersion7();
            var tenant = Guid.CreateVersion7(); var tenantHeld = Guid.CreateVersion7(); var tenantGood = Guid.CreateVersion7();
            var actor = Guid.CreateVersion7();
            object Stamp(int hours) => provider == DatabaseProvider.MySql ? (object)DateTime.UtcNow.AddHours(hours) : DateTimeOffset.UtcNow.AddHours(hours);
            var old = Stamp(-3); var later = Stamp(-1);
            await using var db = DocumentReleaseTestConnections.Create(provider, cs); await db.OpenAsync();
            await db.ExecuteAsync("""
                INSERT INTO fn_files_file
                    (Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, StorageKey, StorageState, CreatedAtUtc, CreatedByUserId)
                VALUES (@Id, NULL, 'slow.bin', 'application/octet-stream', 7, 'local', @Key, 'publishing', @Old, @Actor)
                """, new { Id = uploadHeld, Key = $"native-aot/{uploadHeld:N}.bin", Old = old, Actor = actor });
            foreach (var (id, module, time) in new[] { (claimHeld, "unavailable", old), (claimGood, "document", later) })
                await db.ExecuteAsync("""
                    INSERT INTO fn_files_file_reference_claim
                        (Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId, State, SizeBytes, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (@Id, @Key, @File, @Module, @Reference, 'pending', 7, @Time, @Time)
                    """, new { Id = id, Key = "pending-cycle-" + id.ToString("N"), File = upload.ExistingId,
                        Module = module, Reference = Guid.CreateVersion7(), Time = time });
            await db.ExecuteAsync("""
                INSERT INTO fn_tenancy_tenant
                    (Id, Identifier, Name, Domain, IsActive, CreatedAtUtc, Version, DefaultLocale, LifecycleStatus, ProvisioningStatus, ProvisioningStep)
                VALUES (@Id, @Slug, 'pending cycles', @Domain, 1, @Old, 1, 'zh-CN', 'active', 'completed', 'completed')
                """, new { Id = tenant, Slug = "cycle-" + tenant.ToString("N"), Domain = tenant.ToString("N") + ".test", Old = old });
            foreach (var (id, state, time) in new[] { (tenantHeld, "ready", old), (tenantGood, "pending", later) })
                await db.ExecuteAsync("""
                    INSERT INTO fn_files_tenant_resource_file
                        (Id, TenantId, OwnerModuleKey, ResourceId, OriginalFileName, ContentType, SizeBytes, ContentHash,
                         ProviderKey, StorageKey, StatusKey, CreatedByUserId, CreatedAtUtc)
                    VALUES (@Id, @Tenant, 'unavailable', @Resource, 'cycle.bin', 'application/octet-stream', 7, '',
                         'local', @Key, @State, @Actor, @Time)
                    """, new { Id = id, Tenant = tenant, Resource = Guid.CreateVersion7(), Key = $"native-aot/{id:N}.bin",
                        State = state, Actor = actor, Time = time });
            await File.WriteAllBytesAsync(Path.Combine(upload.StorageRoot, "native-aot", tenantGood.ToString("N") + ".bin"), [1, 2, 3, 4, 5, 6, 7]);
            return new(upload, uploadHeld, claimHeld, claimGood, tenant, tenantHeld, tenantGood);
        }
        catch { NativeWorkerFilesProbe.DeleteOwnedStorageRoot(upload.StorageRoot); throw; }
    }

    internal async Task<bool> IsCompleteAsync(DatabaseProvider provider, string cs)
    {
        await using var db = DocumentReleaseTestConnections.Create(provider, cs); await db.OpenAsync();
        var completed = await db.QuerySingleAsync<int>("""
            SELECT
                (SELECT COUNT(*) FROM fn_files_file WHERE Id = @Existing AND StorageState = 'ready') +
                (SELECT COUNT(*) FROM fn_files_file WHERE Id = @Missing) +
                (SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE Id = @Claim AND State = 'released') +
                (SELECT COUNT(*) FROM fn_files_tenant_resource_file WHERE Id = @TenantFile AND StatusKey = 'ready') AS Completed
            """, new { Existing = Upload.ExistingId, Missing = Upload.MissingId, Claim = ClaimGood, TenantFile = TenantGood });
        // 缺失上传行必须真正不存在，不能用其他成功计数抵消它。
        return completed == 3 && await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file WHERE Id = @Id", new { Id = Upload.MissingId }) == 0;
    }

    internal async Task AssertHeldAsync(DatabaseProvider provider, string cs)
    {
        await using var db = DocumentReleaseTestConnections.Create(provider, cs); await db.OpenAsync();
        Assert.AreEqual("publishing", await db.QuerySingleAsync<string>("SELECT StorageState FROM fn_files_file WHERE Id = @Id", new { Id = UploadHeld }));
        Assert.AreEqual("pending", await db.QuerySingleAsync<string>("SELECT State FROM fn_files_file_reference_claim WHERE Id = @Id", new { Id = ClaimHeld }));
        Assert.AreEqual("ready", await db.QuerySingleAsync<string>("SELECT StatusKey FROM fn_files_tenant_resource_file WHERE Id = @Id AND TenantId = @Tenant",
            new { Id = TenantHeld, Tenant = TenantId }));
    }

    internal void DeleteOwnedObjects() => NativeWorkerFilesProbe.DeleteOwnedStorageRoot(Upload.StorageRoot);
}
