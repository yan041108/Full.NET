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
        var retired = new List<string>(); var active = new List<string>();
        foreach (var kind in new[] { "avatar", "signature", "logo" })
        {
            var subject = Guid.CreateVersion7(); var file = Guid.CreateVersion7();
            var old = Key(kind, subject, file, Guid.CreateVersion7()); var current = Key(kind, subject, file, Guid.CreateVersion7());
            retired.Add(old); active.Add(current);
            await db.ExecuteAsync("""
                INSERT INTO fn_files_file
                    (Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, StorageKey, StorageState, CreatedAtUtc, CreatedByUserId)
                VALUES (@File, NULL, 'media-generation.png', 'image/png', 1, 'local', @Key, 'ready', @Now, @Actor)
                """, new { File = file, Key = "media-release/" + file.ToString("N"), Now = now, Actor = subject });
            foreach (var key in new[] { old, current })
                await db.ExecuteAsync("""
                    INSERT INTO fn_files_file_reference_claim
                        (Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId, State, ContentHash, SizeBytes, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (@Id, @Key, @File, @Owner, @Subject, 'active', NULL, 1, @Now, @Now)
                    """, new { Id = Guid.CreateVersion7(), Key = key, File = file, Owner = kind == "logo" ? "tenancy" : "identity", Subject = subject, Now = now });
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
        await using var host = await NativeWorkerProcessHost.StartAsync(artifact, provider, cs, NativeAotTestTimeouts.ProcessStartup, default);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30); var processed = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            processed = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE Id IN @Ids AND ProcessedAtUtc IS NOT NULL", new { Ids = ids });
            if (processed == ids.Count) break;
            await Task.Delay(100);
        }
        Assert.AreEqual(9, processed, "重复及历史缺失事实应完成消费。");
        Assert.AreEqual(3, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE IdempotencyKey IN @Keys AND State = 'released'", new { Keys = retired }));
        Assert.AreEqual(3, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE IdempotencyKey IN @Keys AND State = 'active'", new { Keys = active }));
        await host.StopGracefullyAsync(TimeSpan.FromSeconds(30), default); host.AssertNoFatalMarkersInLogs();
    }

    private static string Key(string kind, Guid subject, Guid file, Guid binding) => kind switch
    {
        "avatar" => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(subject, file, binding),
        "signature" => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(subject, file, binding),
        _ => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(subject, file, binding),
    };
}
