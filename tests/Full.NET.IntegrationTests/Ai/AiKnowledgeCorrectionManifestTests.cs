using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库任务、修订及完整计划持久化；Identity/Files 为明确 Port，未执行模型、向量或发布。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionManifestTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Frozen_human_manifest_is_complete_replayable_authorized_and_never_reparsed(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await RecoverBeforeWrite(db, provider, connection);
        var operations = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
        foreach (var scoped in new[] { false, true })
        {
            var seed = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            var binding = new AiKnowledgeIndexTaskBinding((Guid)seed["KnowledgeBaseId"]!, (Guid)seed["DocumentId"]!, (Guid)seed["VersionId"]!, 1, 1, (Guid)seed["OwnerUserId"]!);
            var session = new SessionBindingSnapshot(binding.OwnerUserId, (Guid?)seed["TenantId"], Guid.CreateVersion7(), "manifest-stamp", scoped ? "tenant" : "host", scoped ? "tenant" : "host");
            var originalText = "原识别内容 😀"; var bytes = Encoding.UTF8.GetBytes(originalText); var hash = Hash(bytes);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size WHERE Id=@Id; UPDATE fn_ai_knowledge_import SET ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@Id", new { Hash = hash, Size = bytes.Length, Id = binding.VersionId });
            await using var scope = factory.Services.CreateAsyncScope(); var sp = scope.ServiceProvider; var writer = sp.GetRequiredService<ICurrentTenantContextWriter>(); SetScope();
            var tenant = sp.GetRequiredService<ICurrentTenant>(); var transactions = sp.GetRequiredService<IDataTransactionState>();
            var query = sp.GetRequiredService<IQueryExecutor>(); var command = sp.GetRequiredService<ICommandExecutor>(); var transaction = sp.GetRequiredService<ICommandTransaction>();
            var clock = sp.GetRequiredService<IClock>(); var ids = sp.GetRequiredService<IIdGenerator>(); var database = sp.GetRequiredService<IOptions<DatabaseOptions>>();
            var ports = new Ports(transactions, tenant, session, (Guid)seed["FileId"]!, binding.VersionId, bytes.Length, hash);
            var tasks = new AiKnowledgeIndexTaskStore(query, command, tenant, clock, ids);
            var sessions = new AiKnowledgeIndexSessionService(query, command, transaction, transactions, tenant, clock, ids, tasks, ports);
            var authority = new AiKnowledgeCorrectionAuthority(query, transactions, tenant, ports, ports, ports); var store = sp.GetRequiredService<AiKnowledgeCorrectionStore>();
            var service = new AiKnowledgeCorrectionIndexService(query, command, transaction, transactions, tenant, clock, database, tasks, sessions, ports, authority, store);
            var correction = new AiKnowledgeCorrectionStore.Binding(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, binding.OwnerUserId, 1);
            var original = AiKnowledgeParsedDocument.FromText(new(originalText, hash, bytes.Length), AiKnowledgeTextFormat.PlainText, default)!;
            var source = await store.CreateSourceAsync(correction, Guid.CreateVersion7(), Hash("source"u8.ToArray()), original, "managed:utf8-text-v1", default); Assert.IsTrue(source.IsSuccess);
            var draft = (await store.ReadCurrentAsync(correction, original.Format, hash, bytes.Length, original.Policy, default))!.Document; var block = draft.Blocks[0];
            draft = draft.Replace(block.Id, block.Parts[0].Id, "人工校对后的识别文字", default).Append(block.Id, "没有原文的补充内容", Guid.CreateVersion7, default);
            draft = draft.Merge(block.Id, draft.Blocks[1].Id, default);
            var revision = await store.SaveAsync(correction, source.Value!.Id, 0, Guid.CreateVersion7(), Hash("revision"u8.ToArray()), "核对并补充", draft, source.Value.ParserRuntimeId, default); Assert.IsTrue(revision.IsSuccess);
            binding = binding with { DocumentVersion = 2 }; correction = correction with { DocumentVersion = 2 };
            var task = await sessions.EnqueueAsync(binding, session, default); Assert.IsNotNull(task);
            var held = await tasks.AcquireAsync(binding, task.Value, default); Assert.IsNotNull(held?.LeaseToken);
            var lease = held.LeaseToken.Value;
            var prepared = await service.PrepareAsync(binding, task.Value, lease, default); Assert.IsNotNull(prepared);
            Assert.AreEqual(revision.Value!.Id, prepared.Plan.Snapshot.Identity.RevisionId);
            var chunk = prepared.Plan.Chunks.Single(); Assert.AreEqual(2, chunk.References.Count); Assert.IsNull(chunk.References[1].Position);
            Assert.AreEqual(originalText.Length, chunk.References[0].Position!.Length); Assert.AreEqual(AiKnowledgeCorrectionOrigin.Corrected, chunk.References[0].Origin);
            var replay = await service.PrepareAsync(binding, task.Value, lease, default); Assert.IsNotNull(replay); Assert.AreEqual(prepared.Plan.PlanHash, replay.Plan.PlanHash);
            Assert.IsNotNull(await service.ReadCurrentModelAsync(binding, task.Value, lease, prepared, default));
            var payload = await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_document_manifest WHERE Id=@Id", new { Id = task.Value });
            var payloadHash = Convert.ToHexString(SHA256.HashData(payload));
            if (!scoped) await RecoverAfterWrite(db, provider, connection, task.Value, payloadHash);
            Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_document_manifest WHERE Id=@Id", new { Id = task.Value }));
            Assert.AreEqual("running", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = task.Value }));
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id", new { Id = task.Value }));
            foreach (var denied in new[] { AiKnowledgeCorrectionPermissions.Read, AiKnowledgeDocumentPermissions.IndexEnqueue })
            { ports.Denied = denied; Assert.IsNull(await service.ReadAsync(binding, task.Value, lease, default)); Assert.IsNull(await service.PrepareAsync(binding, task.Value, lease, default)); }
            ports.Denied = null; ports.Ready = false; Assert.IsNull(await service.ReadAsync(binding, task.Value, lease, default)); ports.Ready = true;
            ports.Session = session with { SessionId = Guid.CreateVersion7() }; Assert.IsNull(await service.ReadAsync(binding, task.Value, lease, default)); ports.Session = session;
            await db.ExecuteAsync("UPDATE fn_ai_model_config SET OrganizationId='changed' WHERE Id=@Id", new { Id = seed["ModelConfigId"] });
            Assert.IsNull(await service.ReadAsync(binding, task.Value, lease, default)); Assert.IsNull(await service.ReadCurrentModelAsync(binding, task.Value, lease, prepared, default));
            await db.ExecuteAsync("UPDATE fn_ai_model_config SET OrganizationId=NULL WHERE Id=@Id", new { Id = seed["ModelConfigId"] });
            var corrupted = payload.ToArray(); corrupted[^1] ^= 1;
            await SetPayload(corrupted, payloadHash); Assert.IsNull(await service.ReadAsync(binding, task.Value, lease, default));
            Assert.IsNull(await service.ReadCurrentModelAsync(binding, task.Value, lease, prepared, default));
            await SetPayload(corrupted, Convert.ToHexString(SHA256.HashData(corrupted))); Assert.IsNull(await service.ReadAsync(binding, task.Value, lease, default));
            await SetPayload(payload, payloadHash);
            var revisionPayload = await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_correction_revision WHERE Id=@Id", new { Id = revision.Value.Id });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_revision SET Payload=@Payload WHERE Id=@Id", new { Payload = "{}"u8.ToArray(), Id = revision.Value.Id });
            Assert.IsNull(await service.ReadAsync(binding, task.Value, lease, default), "清单存在也不能掩盖冻结修订损坏。");
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_revision SET Payload=@Payload WHERE Id=@Id", new { Payload = revisionPayload, Id = revision.Value.Id });
            Assert.IsNotNull(await service.ReadAsync(binding, task.Value, lease, default));
            if (scoped) { writer.SetHost(); Assert.IsNull(await service.ReadAsync(binding, task.Value, lease, default)); SetScope(); }
            using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => service.ReadAsync(binding, task.Value, lease, cancelled.Token)); }
            var next = await store.SaveAsync(correction, source.Value.Id, 1, Guid.CreateVersion7(), Hash("next"u8.ToArray()), "后续修改", draft, source.Value.ParserRuntimeId, default); Assert.IsTrue(next.IsSuccess);
            Assert.IsNull(await service.ReadAsync(binding, task.Value, lease, default)); Assert.IsNull(await service.PrepareAsync(binding, task.Value, lease, default));
            CollectionAssert.AreEqual(payload, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_document_manifest WHERE Id=@Id", new { Id = task.Value }));
            Task SetPayload(byte[] value, string digest) => db.ExecuteAsync("UPDATE fn_ai_knowledge_document_manifest SET Payload=@Payload,PayloadHash=@Hash WHERE Id=@Id", new { Payload = value, Hash = digest, Id = task.Value });
            void SetScope() { if (session.TenantId is { } id) writer.SetTenant(new(id, "manifest", "人工清单")); else writer.SetHost(); }
        }
        Assert.AreEqual(operations, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
    }

    private static async Task RecoverBeforeWrite(DbConnection db, DatabaseProvider provider, string connection)
    {
        var sql = provider == DatabaseProvider.SqlServer;
        await db.ExecuteAsync(sql ? "ALTER TABLE fn_ai_knowledge_document_manifest DROP CONSTRAINT CK_fn_ai_knowledge_document_manifest_Corrections"
            : "ALTER TABLE fn_ai_knowledge_document_manifest DROP CHECK CK_fn_ai_knowledge_document_manifest_Corrections");
        await db.ExecuteAsync("ALTER TABLE fn_ai_knowledge_document_manifest ADD CONSTRAINT CK_fn_ai_knowledge_document_manifest_Bounds CHECK (FormatKey='knowledge-document-manifest-v1')");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "334_AiKnowledgeCorrectionManifest.sql", sql);
        await ReviewFixMigrationRecoverySupport.CreateRunner(provider, connection).MigrateAsync();
    }
    private static async Task RecoverAfterWrite(DbConnection db, DatabaseProvider provider, string connection, Guid task, string hash)
    {
        var sql = provider == DatabaseProvider.SqlServer;
        await db.ExecuteAsync(sql ? "ALTER TABLE fn_ai_knowledge_document_manifest NOCHECK CONSTRAINT CK_fn_ai_knowledge_document_manifest_Corrections"
            : "ALTER TABLE fn_ai_knowledge_document_manifest ALTER CHECK CK_fn_ai_knowledge_document_manifest_Corrections NOT ENFORCED");
        for (var i = 0; i < 2; i++)
        {
            await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "334_AiKnowledgeCorrectionManifest.sql", sql);
            await ReviewFixMigrationRecoverySupport.CreateRunner(provider, connection).MigrateAsync();
            Assert.AreEqual(hash, await db.QuerySingleAsync<string>("SELECT PayloadHash FROM fn_ai_knowledge_document_manifest WHERE Id=@Id", new { Id = task }));
            await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync("UPDATE fn_ai_knowledge_document_manifest SET FormatKey='unrecognized' WHERE Id=@Id", new { Id = task }));
        }
    }
    internal sealed class Ports(IDataTransactionState transactions, ICurrentTenant tenant, SessionBindingSnapshot original,
        Guid file, Guid version, long size, string hash) : IBackgroundSessionAuthorization, IHostFileReferenceReader, ITenantResourceFileDescriptorReader
    {
        internal SessionBindingSnapshot Session = original; internal string? Denied; internal bool Ready = true;
        internal Func<CancellationToken, Task>? BeforeFileRead;
        public Task<AuthorizedSessionActor?> AuthorizeAsync(SessionBindingSnapshot binding, string permissionCode, CancellationToken cancellationToken = default) => AuthorizeAllAsync(binding, [permissionCode], cancellationToken);
        public Task<AuthorizedSessionActor?> AuthorizeAllAsync(SessionBindingSnapshot binding, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken = default)
        { Check(cancellationToken); return Task.FromResult<AuthorizedSessionActor?>(binding == Session && !permissionCodes.Contains(Denied!) ? new(binding.UserId, binding.TenantId, binding.SessionId) : null); }
        public async Task<HostFileReference?> GetReadyReferenceAsync(Guid fileId, CancellationToken cancellationToken = default)
        { Check(cancellationToken); if (BeforeFileRead is not null) await BeforeFileRead(cancellationToken); Check(cancellationToken); Assert.IsTrue(tenant.IsHost); Assert.AreEqual(file, fileId); return Ready ? new(file, size, hash) : null; }
        public async Task<TenantResourceFileDescriptor?> GetReadyDescriptorAsync(string module, Guid resource, Guid fileId, CancellationToken cancellationToken = default)
        { Check(cancellationToken); if (BeforeFileRead is not null) await BeforeFileRead(cancellationToken); Check(cancellationToken); Assert.IsFalse(tenant.IsHost); Assert.AreEqual("ai", module); Assert.AreEqual(version, resource); Assert.AreEqual(file, fileId); return Ready ? new(file, "a.txt", "text/plain", size, hash, Session.UserId) : null; }
        private void Check(CancellationToken token) { token.ThrowIfCancellationRequested(); Assert.IsFalse(transactions.HasTransaction); Assert.AreEqual(Session.TenantId, tenant.Id); }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
