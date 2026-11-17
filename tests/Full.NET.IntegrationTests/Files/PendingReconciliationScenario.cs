using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.IntegrationTests.Document;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Files;

/// <summary>三条链路共享同一自有双库场景：首批保留，后序记录必须恢复。</summary>
internal sealed record PendingReconciliationScenario(NativeWorkerFilesScenario Upload, Guid UploadHeld,
    Guid ClaimHeld, Guid ClaimGood, Guid TenantId, Guid TenantHeld, Guid TenantGood, Guid QuotaMetricId)
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
                VALUES (@Id, @Slug, 'pending cycles', @Domain, 1, @Old, 1, 'zh-CN', @Lifecycle, @Provisioning, @Provisioning)
                """, new { Id = tenant, Slug = "cycle-" + tenant.ToString("N"), Domain = tenant.ToString("N") + ".test", Old = old,
                Lifecycle = TenantLifecycleStatuses.Active, Provisioning = TenantProvisioningStatuses.Completed });
            foreach (var (id, state, time) in new[] { (tenantHeld, "ready", old), (tenantGood, "pending", later) })
                await db.ExecuteAsync("""
                    INSERT INTO fn_files_tenant_resource_file
                        (Id, TenantId, OwnerModuleKey, ResourceId, OriginalFileName, ContentType, SizeBytes, ContentHash,
                         ProviderKey, StorageKey, StatusKey, CreatedByUserId, CreatedAtUtc)
                    VALUES (@Id, @Tenant, 'unavailable', @Resource, 'cycle.bin', 'application/octet-stream', 7, '',
                         'local', @Key, @State, @Actor, @Time)
                    """, new { Id = id, Tenant = tenant, Resource = Guid.CreateVersion7(), Key = $"native-aot/{id:N}.bin",
                        State = state, Actor = actor, Time = time });
            var metricId = Guid.CreateVersion7();
            await db.ExecuteAsync("""
                INSERT INTO fn_tenancy_quota_metric
                    (Id, TenantId, MetricCode, PeriodKey, LimitValue, UsedValue, ReservedValue, CreatedAtUtc, UpdatedAtUtc, Version)
                VALUES (@Id, @Tenant, @MetricCode, @PeriodKey, 1024, 0, 0, @Now, @Now, 1)
                """, new { Id = metricId, Tenant = tenant, Now = old,
                MetricCode = TenantQuotaMetricCodes.FilesStorageBytes, PeriodKey = TenantQuotaDefaults.PeriodKey });
            var scenario = new PendingReconciliationScenario(upload, uploadHeld, claimHeld, claimGood, tenant, tenantHeld, tenantGood, metricId);
            await scenario.ConfirmStorageQuotaAsync(db, tenantGood, later);
            await File.WriteAllBytesAsync(Path.Combine(upload.StorageRoot, "native-aot", tenantGood.ToString("N") + ".bin"), [1, 2, 3, 4, 5, 6, 7]);
            return scenario;
        }
        catch { NativeWorkerFilesProbe.DeleteOwnedStorageRoot(upload.StorageRoot); throw; }
    }

    // 模拟上传已确认原操作计量、但尚未发布文件时中断。
    internal async Task ConfirmStorageQuotaAsync(DbConnection db, Guid fileId, object timestamp)
    {
        await using var transaction = await db.BeginTransactionAsync();
        await db.ExecuteAsync("""
            INSERT INTO fn_tenancy_quota_reservation
                (Id, TenantId, MetricId, MetricCode, OperationId, Amount, Status, ExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES (@Id, @Tenant, @Metric, @MetricCode, @Operation, 7, @Status, @Now, @Now, @Now, 1)
            """, new { Id = Guid.CreateVersion7(), Tenant = TenantId, Metric = QuotaMetricId,
                Operation = fileId.ToString("N"), Now = timestamp, MetricCode = TenantQuotaMetricCodes.FilesStorageBytes,
                Status = TenantQuotaReservationStatuses.Confirmed }, transaction);
        Assert.AreEqual(1, await db.ExecuteAsync("""
            UPDATE fn_tenancy_quota_metric SET UsedValue = UsedValue + 7, Version = Version + 1, UpdatedAtUtc = @Now
            WHERE Id = @Metric AND TenantId = @Tenant
            """, new { Metric = QuotaMetricId, Tenant = TenantId, Now = timestamp }, transaction));
        await transaction.CommitAsync();
    }

    internal async Task<bool> IsCompleteAsync(DatabaseProvider provider, string cs) => (await ReadProgressAsync(provider, cs)).IsComplete;

    internal async Task<PendingReconciliationProgress> ReadProgressAsync(DatabaseProvider provider, string cs)
    {
        await using var db = DocumentReleaseTestConnections.Create(provider, cs); await db.OpenAsync();
        return await db.QuerySingleAsync<PendingReconciliationProgress>("""
            SELECT
                (SELECT StorageState FROM fn_files_file WHERE Id = @Existing) AS UploadState,
                (SELECT StorageState FROM fn_files_file WHERE Id = @Missing) AS MissingUploadState,
                (SELECT State FROM fn_files_file_reference_claim WHERE Id = @Claim) AS ClaimState,
                (SELECT StatusKey FROM fn_files_tenant_resource_file WHERE Id = @TenantFile) AS TenantFileState
            """, new { Existing = Upload.ExistingId, Missing = Upload.MissingId, Claim = ClaimGood, TenantFile = TenantGood });
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

internal sealed record PendingReconciliationProgress(string? UploadState, string? MissingUploadState, string? ClaimState, string? TenantFileState)
{
    internal bool IsComplete => UploadState == "ready" && MissingUploadState is null && ClaimState == "released" && TenantFileState == "ready";
}
