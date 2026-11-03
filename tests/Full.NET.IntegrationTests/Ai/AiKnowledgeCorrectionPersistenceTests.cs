using System.Security.Cryptography;
using System.Data.Common;
using System.Text;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>仅验证 AI 本地持久协议；来源与身份由明确夹具准备，不冒充 HTTP/Files/模型验收。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionPersistenceTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Corrections_preserve_originals_replay_cas_rollback_and_reparse_heads(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync();
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        foreach (var scoped in new[] { false, true })
        {
            var seed = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            var binding = new AiKnowledgeCorrectionStore.Binding((Guid)seed["KnowledgeBaseId"]!, (Guid)seed["DocumentId"]!,
                (Guid)seed["VersionId"]!, (Guid)seed["OwnerUserId"]!, 1);
            var tenantId = (Guid?)seed["TenantId"];
            const string text = "原始识别正文。"; var bytes = Encoding.UTF8.GetBytes(text); var fileHash = Hash(bytes);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size WHERE Id=@Id", new { Hash = fileHash, Size = bytes.Length, Id = binding.VersionId });
            var original = AiKnowledgeParsedDocument.FromText(new(text, fileHash, bytes.Length), AiKnowledgeTextFormat.PlainText, default); Assert.IsNotNull(original);
            await using var scope = Scope(); var store = scope.ServiceProvider.GetRequiredService<AiKnowledgeCorrectionStore>();
            var key = Guid.CreateVersion7(); var request = Hash("parse");
            var created = await store.CreateSourceAsync(binding, key, request, original, "managed:utf8-text-v1", default);
            Assert.IsTrue(created.IsSuccess, created.Error?.Code); var source = created.Value!;
            var replay = await store.CreateSourceAsync(binding, key, request, original, "managed:utf8-text-v1", default);
            Assert.IsTrue(replay.IsSuccess); Assert.AreEqual(source.Id, replay.Value!.Id); Assert.AreEqual(source.CreatedAtUtc, replay.Value.CreatedAtUtc);
            Assert.IsFalse((await store.CreateSourceAsync(binding, key, Hash("different"), original, "managed:utf8-text-v1", default)).IsSuccess);
            var initial = AiKnowledgeCorrectionWireCodec.Decode(source.InitialPayload, original, source.ParserRuntimeId, default); Assert.IsNotNull(initial);
            var initialSnapshot = await Current(); Assert.IsNotNull(initialSnapshot); Assert.IsNull(initialSnapshot.Identity.RevisionId);
            var part = initial.Blocks[0].Parts[0]; var corrected = initial.Replace(initial.Blocks[0].Id, part.Id, "人工校对后的正文。", default);
            var editKey = Guid.CreateVersion7();
            var saved = await store.SaveAsync(binding, source.Id, 0, editKey, Hash("edit"), "修正识别", corrected, source.ParserRuntimeId, default);
            Assert.IsTrue(saved.IsSuccess, saved.Error?.Code); var revision = saved.Value!;
            Assert.AreEqual(2, revision.DocumentVersion); Assert.AreEqual(1, revision.HeadVersion); Assert.IsNull(revision.ParentId);
            var frozen = await Current(); Assert.IsNotNull(frozen); Assert.AreEqual(revision.Id, frozen.Identity.RevisionId);
            var replayEdit = await store.SaveAsync(binding, source.Id, 0, editKey, Hash("edit"), "修正识别", corrected, source.ParserRuntimeId, default);
            Assert.IsTrue(replayEdit.IsSuccess); Assert.AreEqual(revision.Id, replayEdit.Value!.Id); Assert.AreEqual(revision.CreatedAtUtc, replayEdit.Value.CreatedAtUtc);
            Assert.IsFalse((await store.SaveAsync(binding, source.Id, 0, editKey, Hash("changed"), "修正识别", corrected, source.ParserRuntimeId, default)).IsSuccess);
            CollectionAssert.AreEqual(source.OriginalPayload, (await store.ReadSourceAsync(binding, source.Id, default))!.OriginalPayload);

            async Task<bool> Race(string reason)
            {
                await using var competing = Scope();
                return (await competing.ServiceProvider.GetRequiredService<AiKnowledgeCorrectionStore>().SaveAsync(binding with { DocumentVersion = 2 },
                    source.Id, 1, Guid.CreateVersion7(), Hash(reason), reason, corrected, source.ParserRuntimeId, default)).IsSuccess;
            }
            var raced = await Task.WhenAll(Race("并发甲"), Race("并发乙")); Assert.AreEqual(1, raced.Count(success => success));
            var head = (await store.ReadHeadAsync(binding, default))!; Assert.AreEqual(2, head.Version);
            var older = await store.ReadFrozenAsync(binding, frozen.Identity, original.Format, fileHash, bytes.Length, original.Policy, default);
            Assert.IsNotNull(older); Assert.AreEqual(frozen.Identity, older.Identity, "冻结输入不能追随较新的修订头。");
            Assert.IsNull(await store.ReadFrozenAsync(binding, frozen.Identity with { PayloadHash = Hash("invalid") }, original.Format, fileHash, bytes.Length, original.Policy, default));
            Assert.AreEqual(3, await db.QuerySingleAsync<int>("SELECT Version FROM fn_ai_knowledge_document WHERE Id=@Id", new { Id = binding.DocumentId }));
            Assert.AreEqual(2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_correction_revision WHERE VersionId=@Id", new { Id = binding.VersionId }));

            // 在插入修订、切换头以后让文档 CAS 失败，真实事务必须撤销前两次写入。
            var sp = scope.ServiceProvider;
            var failing = new AiKnowledgeCorrectionStore(sp.GetRequiredService<IQueryExecutor>(), new FailTouch(sp.GetRequiredService<ICommandExecutor>()),
                sp.GetRequiredService<ICommandTransaction>(), sp.GetRequiredService<IDataTransactionState>(), sp.GetRequiredService<ICurrentTenant>(),
                sp.GetRequiredService<IClock>(), sp.GetRequiredService<IIdGenerator>(), sp.GetRequiredService<IOptions<DatabaseOptions>>());
            Assert.IsFalse((await failing.SaveAsync(binding with { DocumentVersion = 3 }, source.Id, 2, Guid.CreateVersion7(), Hash("rollback"), "回滚验证", corrected, source.ParserRuntimeId, default)).IsSuccess);
            Assert.AreEqual(head, await store.ReadHeadAsync(binding, default));
            Assert.AreEqual(2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_correction_revision WHERE VersionId=@Id", new { Id = binding.VersionId }));
            Assert.AreEqual(3, await db.QuerySingleAsync<int>("SELECT Version FROM fn_ai_knowledge_document WHERE Id=@Id", new { Id = binding.DocumentId }));

            var reparse = await store.CreateSourceAsync(binding with { DocumentVersion = 3 }, Guid.CreateVersion7(), Hash("reparse"), original, "managed:utf8-text-v2", default);
            Assert.IsTrue(reparse.IsSuccess); Assert.AreNotEqual(source.SourceHash, reparse.Value!.SourceHash);
            Assert.AreEqual(head, await store.ReadHeadAsync(binding, default), "重解析不能覆盖人工修订。");
            var restored = await store.SaveAsync(binding with { DocumentVersion = 3 }, source.Id, 2, Guid.CreateVersion7(), Hash("restore"), "恢复原文", initial, source.ParserRuntimeId, default);
            Assert.IsTrue(restored.IsSuccess, restored.Error?.Code); Assert.AreEqual(head.RevisionId, restored.Value!.ParentId);
            Assert.AreEqual(3, restored.Value.HeadVersion); Assert.AreEqual(4, restored.Value.DocumentVersion); Assert.AreNotEqual(revision.Id, restored.Value.Id);
            var read = await store.ReadRevisionAsync(binding, restored.Value.Id, default); Assert.IsNotNull(read);
            Assert.IsNotNull(AiKnowledgeCorrectionWireCodec.Decode(read.Payload, original, source.ParserRuntimeId, default));
            var restoredSnapshot = await Current(); Assert.IsNotNull(restoredSnapshot); Assert.AreEqual(3, restoredSnapshot.Identity.HeadVersion);
            // 持久载荷损坏不得回退原解析或其他修订；修复夹具后才能继续读取。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_revision SET Payload=@Payload WHERE Id=@Id", new { Payload = Encoding.UTF8.GetBytes("{}"), Id = read.Id });
            Assert.IsNull(await Current());
            Assert.IsNull(await store.ReadFrozenAsync(binding, restoredSnapshot.Identity, original.Format, fileHash, bytes.Length, original.Policy, default));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_revision SET Payload=@Payload WHERE Id=@Id", new { read.Payload, read.Id });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_source SET OriginalPayload=@Payload WHERE Id=@Id", new { Payload = Encoding.UTF8.GetBytes("{}"), source.Id });
            Assert.IsNull(await Current());
            Assert.IsFalse((await store.SaveAsync(binding with { DocumentVersion = 4 }, source.Id, 3, Guid.CreateVersion7(), Hash("corrupt"), "损坏来源不可保存", initial, source.ParserRuntimeId, default)).IsSuccess);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_source SET OriginalPayload=@Payload WHERE Id=@Id", new { Payload = source.OriginalPayload, source.Id });
            Assert.IsNotNull(await Current());
            await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_source SET CleanupState=3 WHERE Id=@Id", new { source.Id }));
            await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_revision SET HeadVersion=0 WHERE Id=@Id", new { read.Id }));
            await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_head SET Version=-1 WHERE Id=@Id", new { Id = binding.VersionId }));
            if (!scoped) await RecoverMigration(db, provider, connection, source.Id, read.Id, binding.VersionId);

            await using (var other = factory.Services.CreateAsyncScope())
            {
                other.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new(Guid.CreateVersion7(), "other", "其他租户"));
                Assert.IsNull(await other.ServiceProvider.GetRequiredService<AiKnowledgeCorrectionStore>().ReadSourceAsync(binding, source.Id, default));
            }
            var ownWriter = sp.GetRequiredService<ICurrentTenantContextWriter>();
            if (tenantId is { } ownTenant) ownWriter.SetTenant(new(ownTenant, "correction", "校对租户")); else ownWriter.SetHost();
            Assert.IsNotNull(await store.ReadRevisionAsync(binding, read.Id, default));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@Id", new { Id = binding.DocumentId });
            Assert.IsNull(await store.ReadRevisionAsync(binding, read.Id, default));
            Assert.IsNull(await Current());
            Assert.IsFalse((await store.SaveAsync(binding with { DocumentVersion = 4 }, source.Id, 3, Guid.CreateVersion7(), Hash("deleted"), "删除后不可编辑", initial, source.ParserRuntimeId, default)).IsSuccess);

            AsyncServiceScope Scope()
            {
                var result = factory.Services.CreateAsyncScope(); var writer = result.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                if (tenantId is { } id) writer.SetTenant(new(id, "correction", "校对租户")); else writer.SetHost(); return result;
            }
            Task<AiKnowledgeCorrectionSnapshot?> Current() => store.ReadCurrentAsync(binding, original.Format, fileHash, bytes.Length, original.Policy, default);
        }
    }
    // 表已创建、索引只完成一部分且 DbUp 未记账时，恢复缺失结构必须保留已有正文和 CAS 头。
    private static async Task RecoverMigration(DbConnection db, DatabaseProvider provider, string connection, Guid sourceId, Guid revisionId, Guid versionId)
    {
        var sqlServer = provider == DatabaseProvider.SqlServer;
        var indexes = new[]
        {
            (Table: "fn_ai_knowledge_correction_source", Index: "UX_fn_ai_knowledge_correction_source_VersionId_RequestKey"),
            (Table: "fn_ai_knowledge_correction_source", Index: "IX_fn_ai_knowledge_correction_source_VersionId_CreatedAtUtc_Id"),
            (Table: "fn_ai_knowledge_correction_revision", Index: "UX_fn_ai_knowledge_correction_revision_VersionId_RequestKey"),
            (Table: "fn_ai_knowledge_correction_revision", Index: "UX_fn_ai_knowledge_correction_revision_VersionId_HeadVersion")
        };
        var original = await db.QuerySingleAsync<byte[]>("SELECT OriginalPayload FROM fn_ai_knowledge_correction_source WHERE Id=@Id", new { Id = sourceId });
        var payload = await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_correction_revision WHERE Id=@Id", new { Id = revisionId });
        foreach (var (table, index) in indexes) await db.ExecuteAsync($"DROP INDEX {index} ON {(sqlServer ? "dbo." : "")}{table}");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "331_AiKnowledgeCorrections.sql", sqlServer);
            await ReviewFixMigrationRecoverySupport.CreateRunner(provider, connection).MigrateAsync();
            foreach (var (table, index) in indexes) Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(db, table, index, sqlServer));
            CollectionAssert.AreEqual(original, await db.QuerySingleAsync<byte[]>("SELECT OriginalPayload FROM fn_ai_knowledge_correction_source WHERE Id=@Id", new { Id = sourceId }));
            CollectionAssert.AreEqual(payload, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_correction_revision WHERE Id=@Id", new { Id = revisionId }));
            Assert.AreEqual(revisionId, await db.QuerySingleAsync<Guid>("SELECT RevisionId FROM fn_ai_knowledge_correction_head WHERE Id=@Id AND Version=3", new { Id = versionId }));
        }
    }
    private sealed class FailTouch(ICommandExecutor inner) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters, CancellationToken cancellationToken) =>
            statement.Name.StartsWith("ai.knowledge_correction.touch_", StringComparison.Ordinal) ? Task.FromResult(0) : inner.ExecuteAsync(statement, parameters, cancellationToken);
    }
    private static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}
