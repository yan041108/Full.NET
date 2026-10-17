using System.Diagnostics.CodeAnalysis;
using System.Net;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.HostFileReferences;
using Full.NET.Modules.Document.Features.ManageHostDocumentItems;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Full.NET.IntegrationTests.Document.DocumentLifecycleAssertions;

namespace Full.NET.IntegrationTests.Document;

/// <summary>真实数据库核对三种删除的原子事实、失败回滚与重复投递；只读取本场景拥有的行。</summary>
internal static class DocumentFileReleaseAssertions
{
    private const string Items = "/api/v1/document/host/items";

    internal static async Task VerifyAsync(FullNetApiFactory factory, HttpClient client, string token, CancellationToken ct)
    {
        var item = await ReadAsync<HostDocumentItemResponse>(client, token, HttpMethod.Post, Items,
            new CreateHostDocumentItemRequest("release-" + Guid.NewGuid().ToString("N"), null), HttpStatusCode.Created, ct);
        var versions = new List<HostDocumentVersionResponse>();
        for (var n = 0; n < 4; n++)
        {
            item = await UploadAsync(client, token, item.Id, n, ct);
            versions.Add(item.CurrentVersion!);
        }
        item = await ReadAsync<HostDocumentItemResponse>(client, token, HttpMethod.Post,
            Items + $"/{item.Id}/versions/{versions[0].Id}/delete", new DeleteHostDocumentVersionRequest(item.Version), HttpStatusCode.OK, ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var host = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>(); host.SetHost();
        await using var db = DocumentReleaseTestConnections.Create(factory.Provider, factory.ConnectionString);
        await db.OpenAsync(ct);
        try
        {
            var p = scope.ServiceProvider;
            var fault = new FailAfterWrite(p.GetRequiredService<IOutboxWriter>());
            var deletion = new DocumentVersionDeletionService(p.GetRequiredService<IQueryExecutor>(),
                p.GetRequiredService<ICommandExecutor>(), p.GetRequiredService<ICommandTransaction>(), fault,
                p.GetRequiredService<IOptionsMonitor<DocumentVersionRetentionOptions>>(), p.GetRequiredService<IClock>(),
                p.GetRequiredService<IIdGenerator>(), p.GetRequiredService<DocumentItemWriteGuard>());
            await Assert.ThrowsExactlyAsync<IOException>(() => deletion.TryDeleteVersionForRetentionAsync(item.Id, versions[1].Id, ct));
            Assert.AreEqual(1, await db.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM fn_document_version WHERE Id = @Id", new { Id = versions[1].Id }, cancellationToken: ct)));
            Assert.AreEqual(0, await db.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM fn_document_version_deletion_audit WHERE VersionId = @Id", new { Id = versions[1].Id }, cancellationToken: ct)));
            Assert.AreEqual(1, (await FactsAsync(db, item.Id, p.GetRequiredService<IIntegrationEventSerializer>(), ct)).Count);
            Assert.IsTrue(await p.GetRequiredService<DocumentVersionDeletionService>().TryDeleteVersionForRetentionAsync(item.Id, versions[1].Id, ct));
            using (var deleted = await SendAsync(client, token, HttpMethod.Post, Items + $"/{item.Id}/delete",
                new DeleteHostDocumentItemRequest(item.Version), ct)) Assert.AreEqual(HttpStatusCode.OK, deleted.StatusCode);
            using (var purged = await SendAsync(client, token, HttpMethod.Post, $"/api/v1/document/host/recycle-bin/{item.Id}/purge", null, ct))
                Assert.AreEqual(HttpStatusCode.OK, purged.StatusCode, await purged.Content.ReadAsStringAsync(ct));
            var facts = await FactsAsync(db, item.Id, p.GetRequiredService<IIntegrationEventSerializer>(), ct);
            Assert.AreEqual(4, facts.Count);
            Assert.AreEqual(4, await CountStateAsync(db, versions.Select(v => v.Id).ToArray(), "active", ct));
            var handler = p.GetServices<IIntegrationEventHandler>().Single(h => h.EventType == DocumentVersionDeletedOutbox.EventType);
            foreach (var fact in facts)
            {
                var context = new IntegrationEventContext(fact.Id, handler.EventType, 1, null, null, DateTimeOffset.UtcNow);
                await handler.HandleAsync(context, fact.Payload, ct);
                await handler.HandleAsync(context, fact.Payload, ct);
            }
            Assert.AreEqual(4, await CountStateAsync(db, versions.Select(v => v.Id).ToArray(), "released", ct));
            await VerifyPagedPurgeAsync(factory, client, token, db, p.GetRequiredService<IIntegrationEventSerializer>(), ct);
        }
        finally { host.Clear(); }
    }

    /// <summary>同一隔离数据库的自有文档跨过第二页，验证数据库 UUID 排序游标不会漏掉或重复事实。</summary>
    private static async Task VerifyPagedPurgeAsync(FullNetApiFactory factory, HttpClient client, string token,
        System.Data.Common.DbConnection db, IIntegrationEventSerializer serializer, CancellationToken ct)
    {
        var item = await ReadAsync<HostDocumentItemResponse>(client, token, HttpMethod.Post, Items,
            new CreateHostDocumentItemRequest("paged-purge-" + Guid.NewGuid().ToString("N"), null), HttpStatusCode.Created, ct);
        item = await UploadAsync(client, token, item.Id, 0, ct);
        var ids = new List<Guid> { item.CurrentVersion!.Id };
        var file = item.CurrentVersion.FileId;
        object now = factory.Provider == DatabaseProvider.MySql ? DateTime.UtcNow : DateTimeOffset.UtcNow;
        for (var n = 2; n <= 135; n++)
        {
            var id = Guid.CreateVersion7(); ids.Add(id);
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO fn_document_version
                    (Id, DocumentItemId, FileId, VersionNumber, ContentHash, SizeBytes, ChangeDescription, UploadedByUserId, CreatedAtUtc)
                VALUES (@Id, @Item, @File, @Number, NULL, 1, NULL, @Actor, @Now)
                """, new { Id = id, Item = item.Id, File = file, Number = n, Actor = item.CreatedByUserId, Now = now }, cancellationToken: ct));
        }
        using (var deleted = await SendAsync(client, token, HttpMethod.Post, Items + $"/{item.Id}/delete",
            new DeleteHostDocumentItemRequest(item.Version), ct)) Assert.AreEqual(HttpStatusCode.OK, deleted.StatusCode);
        using (var purged = await SendAsync(client, token, HttpMethod.Post, $"/api/v1/document/host/recycle-bin/{item.Id}/purge", null, ct))
            Assert.AreEqual(HttpStatusCode.OK, purged.StatusCode, await purged.Content.ReadAsStringAsync(ct));
        var facts = await FactsAsync(db, item.Id, serializer, ct);
        Assert.AreEqual(135, facts.Count);
        CollectionAssert.AreEquivalent(ids.ToArray(), facts.Select(f => serializer.Deserialize<DocumentVersionDeletedIntegrationEvent>(f.Payload).VersionId).ToArray());
        Assert.AreEqual(0, await db.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM fn_document_version WHERE DocumentItemId = @Id", new { Id = item.Id }, cancellationToken: ct)));
    }

    internal static async Task<List<Fact>> FactsAsync(System.Data.Common.DbConnection db, Guid item,
        IIntegrationEventSerializer serializer, CancellationToken ct)
    {
        var rows = await db.QueryAsync<Fact>(new CommandDefinition(
            "SELECT Id, Payload FROM fn_outbox_message WHERE MessageType = @Type", new { Type = DocumentVersionDeletedOutbox.EventType }, cancellationToken: ct));
        return rows.Where(f => serializer.Deserialize<DocumentVersionDeletedIntegrationEvent>(f.Payload).DocumentItemId == item).ToList();
    }

    private static Task<int> CountStateAsync(System.Data.Common.DbConnection db, Guid[] references, string state, CancellationToken ct) =>
        db.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE ConsumerModule = 'document' AND ConsumerReferenceId IN @References AND State = @State",
            new { References = references, State = state }, cancellationToken: ct));

    internal sealed class Fact { public Guid Id { get; set; } public byte[] Payload { get; set; } = []; }

    private sealed class FailAfterWrite(IOutboxWriter inner) : IOutboxWriter
    {
        public async Task AddAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string eventType, int schemaVersion,
            T payload, CancellationToken cancellationToken = default)
        { await inner.AddAsync(eventType, schemaVersion, payload, cancellationToken); throw new IOException("owned test outbox write failure"); }
        public async Task AddAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string eventType, int schemaVersion,
            T payload, IntegrationEventMetadata metadata, CancellationToken cancellationToken = default)
        { await inner.AddAsync(eventType, schemaVersion, payload, metadata, cancellationToken); throw new IOException("owned test outbox write failure"); }
    }
}

/// <summary>隔离数据库夹具的显式提供程序连接，不向应用运行时提供直连能力。</summary>
internal static class DocumentReleaseTestConnections
{
    internal static System.Data.Common.DbConnection Create(DatabaseProvider provider, string connectionString) => provider == DatabaseProvider.SqlServer
        ? new Microsoft.Data.SqlClient.SqlConnection(connectionString)
        : new MySqlConnector.MySqlConnection(Full.NET.Data.MySql.MySqlConnectionStringPolicy.Create(connectionString,
            MySqlGuidStorageMode.Binary16, allowUserVariables: false));
}
