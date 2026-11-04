using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Document;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Features.HostFileReferences;
using Full.NET.Modules.Tenancy.Features.HostFileReferences;
using Full.NET.Serialization.MemoryPack;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>API 已停止后由新原生 Worker 进程释放旧代际，重复事实不能释放同文件的新绑定。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerMediaReleaseTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task NativeWorker_releases_retired_media_generations_without_touching_rebound_file(DatabaseProvider provider)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason))
            Assert.Inconclusive(reason ?? "Native Worker artifact unavailable.");
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, cs);
        await using var db = DocumentReleaseTestConnections.Create(provider, cs); await db.OpenAsync();
        object now = provider == DatabaseProvider.MySql ? DateTime.UtcNow : DateTimeOffset.UtcNow;
        var serializer = new MemoryPackIntegrationEventSerializer(); var ids = new List<Guid>();
        var retired = new List<string>(); var active = new List<string>(); var failed = new List<string>();
        var orphans = new List<string>(); var historical = new List<string>();
        var user = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_identity_user WHERE Username = 'admin' AND TenantId IS NULL");
        await db.ExecuteAsync("DELETE FROM fn_identity_user_profile WHERE UserId = @User", new { User = user });
        await db.ExecuteAsync("INSERT INTO fn_identity_user_profile (UserId, Version) VALUES (@User, 1)", new { User = user });
        foreach (var kind in new[] { "avatar", "signature", "logo" })
        {
            var subject = kind == "logo" ? Guid.CreateVersion7() : user; var file = Guid.CreateVersion7();
            var old = Key(kind, subject, file, Guid.CreateVersion7()); var current = Key(kind, subject, file, Guid.CreateVersion7());
            retired.Add(old); active.Add(current); var rejected = Key(kind, subject, file, Guid.CreateVersion7()); failed.Add(rejected);
            var orphan = Key(kind, subject, file, Guid.CreateVersion7()); orphans.Add(orphan);
            var legacy = kind switch { "avatar" => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(subject, file),
                "signature" => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(subject, file),
                _ => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(subject, file) }; historical.Add(legacy);
            await db.ExecuteAsync("""
                INSERT INTO fn_files_file
                    (Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, StorageKey, StorageState, CreatedAtUtc, CreatedByUserId)
                VALUES (@File, NULL, 'media-generation.png', 'image/png', 1, 'local', @Key, 'ready', @Now, @Actor)
                """, new { File = file, Key = "media-release/" + file.ToString("N"), Now = now, Actor = subject });
            foreach (var key in new[] { old, current, rejected, orphan, legacy })
                await db.ExecuteAsync("""
                    INSERT INTO fn_files_file_reference_claim
                        (Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId, State, ContentHash, SizeBytes, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (@Id, @Key, @File, @Owner, @Subject, @State, NULL, 1, @Created, @Created)
                    """, new { Id = Guid.CreateVersion7(), Key = key, File = file, Owner = kind == "logo" ? "tenancy" : "identity", Subject = subject, State = key == current || key == rejected ? "pending" : "active", Created = provider == DatabaseProvider.MySql ? (object)DateTime.UtcNow.AddHours(-2) : DateTimeOffset.UtcNow.AddHours(-2) });
            // 当前业务绑定只拥有 current；同文件 rejected 模拟 CAS 失败且快速 Release 失败。
            if (kind == "logo")
                await db.ExecuteAsync("""
                    INSERT INTO fn_tenancy_tenant (Id, Identifier, Name, Domain, IsActive, CreatedAtUtc, Version,
                        DefaultLocale, LifecycleStatus, ProvisioningStatus, ProvisioningStep, LogoFileId, LogoClaimKey)
                    VALUES (@Id, @Slug, 'native media', @Domain, 1, @Now, 1, 'zh-CN', 'active', 'completed', 'completed', @File, @Key)
                    """, new { Id = subject, Slug = "native-media-" + subject.ToString("N"), Domain = subject.ToString("N") + ".test", Now = now, File = file, Key = current });
            else
            {
                var prefix = kind == "avatar" ? "Avatar" : "Signature";
                await db.ExecuteAsync($"UPDATE fn_identity_user_profile SET {prefix}FileId = @File, {prefix}ClaimKey = @Key WHERE UserId = @User",
                    new { File = file, Key = current, User = user });
            }
            var type = kind == "logo" ? TenantLogoUnboundOutbox.EventType : IdentityProfileMediaUnboundOutbox.EventType;
            var payload = Payload(old);
            await InsertAsync(payload); await InsertAsync(payload);
            await InsertAsync(Payload(Key(kind, subject, file, Guid.CreateVersion7())));

            byte[] Payload(string key) => kind == "logo" ? serializer.Serialize(new TenantLogoUnboundIntegrationEvent(subject, file, key))
                : serializer.Serialize(new IdentityProfileMediaUnboundIntegrationEvent(subject, file, kind, key));
            async Task InsertAsync(byte[] bytes)
            {
                var id = Guid.CreateVersion7(); ids.Add(id);
                await db.ExecuteAsync("""
                    INSERT INTO fn_outbox_message (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAtUtc)
                    VALUES (@Id, @Type, 1, 'application/x-memorypack', NULL, NULL, @Payload, @Now)
                    """, new { Id = id, Type = type, Payload = bytes, Now = now });
            }
        }
        await using var host = await NativeWorkerProcessHost.StartAsync(artifact, provider, cs, NativeAotTestTimeouts.ProcessStartup, default, enableFilesReferenceClaimReconciliation: true,
            additionalSettings: new Dictionary<string, string?> {
                ["Files__ActiveReferenceClaimReconciliation__Enabled"] = "true",
                ["Files__ActiveReferenceClaimReconciliation__PollSeconds"] = "5",
                ["Files__ActiveReferenceClaimReconciliation__BatchSize"] = "2",
                ["Files__ActiveReferenceClaimReconciliation__MaxBatchesPerRun"] = "1",
                ["Files__ActiveReferenceClaimReconciliation__MinimumAgeSeconds"] = "900",
            });
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30); var processed = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            processed = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE Id IN @Ids AND ProcessedAtUtc IS NOT NULL", new { Ids = ids });
            var reconciled = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE IdempotencyKey IN @Keys AND State = 'released'", new { Keys = failed });
            var confirmed = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE IdempotencyKey IN @Keys AND State = 'active'", new { Keys = active });
            var compensated = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE IdempotencyKey IN @Keys AND State = 'released'", new { Keys = orphans });
            if (processed == ids.Count && reconciled == 3 && confirmed == 3 && compensated == 3) break;
            await Task.Delay(100);
        }
        Assert.AreEqual(9, processed, "重复及历史缺失事实应完成消费。");
        Assert.AreEqual(3, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE IdempotencyKey IN @Keys AND State = 'released'", new { Keys = retired }));
        Assert.AreEqual(3, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE IdempotencyKey IN @Keys AND State = 'active'", new { Keys = active }));
        Assert.AreEqual(3, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE IdempotencyKey IN @Keys AND State = 'released'", new { Keys = failed }), "失败绑定不得因另一代际占用同文件而晋升。");
        Assert.AreEqual(3, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE IdempotencyKey IN @Keys AND State = 'released'", new { Keys = orphans }), "没有解绑事实的永久退休代际应由 Active 扫描补偿。");
        Assert.AreEqual(3, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE IdempotencyKey IN @Keys AND State = 'active'", new { Keys = historical }), "历史可复用旧键必须保留。");
        await host.StopGracefullyAsync(TimeSpan.FromSeconds(30), default); host.AssertNoFatalMarkersInLogs();
    }

    private static string Key(string kind, Guid subject, Guid file, Guid binding) => kind switch
    {
        "avatar" => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(subject, file, binding),
        "signature" => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(subject, file, binding),
        _ => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(subject, file, binding),
    };
}
