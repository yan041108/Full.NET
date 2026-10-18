using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Document;
using Full.NET.Modules.Document.Features.HostFileReferences;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Serialization.MemoryPack;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实原生 Worker 消费可靠删除事实；重复、历史缺失及毒载荷不得产生错误释放。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerDocumentFileReleaseTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task NativeWorker_releases_document_claim_from_durable_deletion_fact(DatabaseProvider provider)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason))
            Assert.Inconclusive(reason ?? "Native Worker artifact unavailable.");
        var ct = CancellationToken.None;
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, cs, ct);
        await using var db = DocumentReleaseTestConnections.Create(provider, cs); await db.OpenAsync(ct);
        var file = Guid.CreateVersion7(); var version = Guid.CreateVersion7(); var protectedVersion = Guid.CreateVersion7();
        object now = provider == DatabaseProvider.MySql ? DateTime.UtcNow : DateTimeOffset.UtcNow;
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_files_file
                (Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, StorageKey, StorageState, CreatedAtUtc, CreatedByUserId)
            VALUES (@File, NULL, 'native-document-release.bin', 'application/octet-stream', 1, 'local', @Key, 'ready', @Now, @Actor)
            """, new { File = file, Key = "document-release/" + file.ToString("N"), Now = now, Actor = Guid.CreateVersion7() }, cancellationToken: ct));
        foreach (var reference in new[] { version, protectedVersion })
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO fn_files_file_reference_claim
                    (Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId, State, ContentHash, SizeBytes, CreatedAtUtc, UpdatedAtUtc)
                VALUES (@Id, @Key, @File, 'document', @Reference, 'active', NULL, 1, @Now, @Now)
                """, new { Id = Guid.CreateVersion7(), Key = "document-version:" + reference.ToString("D"),
                    File = file, Reference = reference, Now = now }, cancellationToken: ct));
        var serializer = new MemoryPackIntegrationEventSerializer(); var item = Guid.CreateVersion7();
        var payload = serializer.Serialize(new DocumentVersionDeletedIntegrationEvent(item, version, file));
        var ids = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() }; var poison = Guid.CreateVersion7();
        await InsertAsync(ids[0], payload); await InsertAsync(ids[1], payload);
        await InsertAsync(ids[2], serializer.Serialize(new DocumentVersionDeletedIntegrationEvent(item, Guid.CreateVersion7(), file)));
        await InsertAsync(poison, serializer.Serialize<DocumentVersionDeletedIntegrationEvent>(null!));
        // API/生产者停止之后持久化消息仍在；Worker 从新进程开始领取，不依赖提交后的进程内回调。
        await using var host = await NativeWorkerProcessHost.StartAsync(artifact, provider, cs, NativeAotTestTimeouts.ProcessStartup, ct);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30); var processed = 0; var dead = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            processed = await db.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM fn_outbox_message WHERE Id IN @Ids AND ProcessedAtUtc IS NOT NULL", new { Ids = ids }, cancellationToken: ct));
            dead = await db.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM fn_outbox_message WHERE Id = @Id AND DeadLetteredAtUtc IS NOT NULL", new { Id = poison }, cancellationToken: ct));
            if (processed == 3 && dead == 1) break;
            await Task.Delay(100, ct);
        }
        Assert.AreEqual(3, processed, "Duplicate and missing claims must complete successfully.");
        Assert.AreEqual(1, dead, "Null payload must be retained as a dead letter.");
        Assert.AreEqual("released", await StateAsync(version));
        Assert.AreEqual("active", await StateAsync(protectedVersion));
        await host.StopGracefullyAsync(TimeSpan.FromSeconds(30), ct); host.AssertNoFatalMarkersInLogs();

        Task<string> StateAsync(Guid reference) => db.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT State FROM fn_files_file_reference_claim WHERE ConsumerReferenceId = @Reference AND ConsumerModule = 'document'",
            new { Reference = reference }, cancellationToken: ct));
        Task<int> InsertAsync(Guid id, byte[] bytes) => db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_outbox_message (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAtUtc)
            VALUES (@Id, @Type, 1, 'application/x-memorypack', NULL, NULL, @Payload, @Now)
            """, new { Id = id, Type = DocumentVersionDeletedOutbox.EventType, Payload = bytes, Now = now }, cancellationToken: ct));
    }
}
