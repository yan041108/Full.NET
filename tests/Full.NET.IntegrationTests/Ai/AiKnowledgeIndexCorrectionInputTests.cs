using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实索引入队、重建及租约冻结人工身份；旧原文执行器必须拒绝，不能据此宣布人工索引完成。</summary>
[TestClass]
public sealed class AiKnowledgeIndexCorrectionInputTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Index_tasks_and_rebuilds_freeze_revision_identity_and_reject_automatic_fallback(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        // CREATE TABLE LIKE 会重命名 MySQL CHECK；命名约束恢复须从正式 DDL 直接创建隔离库。
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        foreach (var scoped in new[] { false, true })
        {
            var seed = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            var binding = new AiKnowledgeIndexTaskBinding((Guid)seed["KnowledgeBaseId"]!, (Guid)seed["DocumentId"]!, (Guid)seed["VersionId"]!, 1, 1, (Guid)seed["OwnerUserId"]!);
            var correction = new AiKnowledgeCorrectionStore.Binding(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, binding.OwnerUserId, 1);
            var session = new SessionBindingSnapshot(binding.OwnerUserId, (Guid?)seed["TenantId"], Guid.CreateVersion7(), "stamp", scoped ? "tenant" : "host", scoped ? "tenant" : "host");
            var bytes = Encoding.UTF8.GetBytes("原始自动内容"); var hash = Hash(bytes);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size WHERE Id=@VersionId; UPDATE fn_ai_knowledge_import SET ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@VersionId", new { Hash = hash, Size = bytes.Length, binding.VersionId });
            await using var scope = factory.Services.CreateAsyncScope(); var sp = scope.ServiceProvider;
            var writer = sp.GetRequiredService<ICurrentTenantContextWriter>(); if (session.TenantId is { } tenantId) writer.SetTenant(new(tenantId, "input", "输入")); else writer.SetHost();
            var query = sp.GetRequiredService<IQueryExecutor>(); var command = sp.GetRequiredService<ICommandExecutor>(); var transaction = sp.GetRequiredService<ICommandTransaction>();
            var state = sp.GetRequiredService<IDataTransactionState>(); var tenant = sp.GetRequiredService<ICurrentTenant>(); var clock = sp.GetRequiredService<IClock>(); var ids = sp.GetRequiredService<IIdGenerator>();
            var options = sp.GetRequiredService<IOptions<DatabaseOptions>>(); var auth = new AiKnowledgeIndexEmbeddingAssertions.ControlledAuthorization(session);
            var tasks = new AiKnowledgeIndexTaskStore(query, command, tenant, clock, ids);
            var sessions = new AiKnowledgeIndexSessionService(query, command, transaction, state, tenant, clock, ids, tasks, auth);
            var store = sp.GetRequiredService<AiKnowledgeCorrectionStore>();
            var original = AiKnowledgeParsedDocument.FromText(new("原始自动内容", hash, bytes.Length), AiKnowledgeTextFormat.PlainText, default); Assert.IsNotNull(original);
            var source = await store.CreateSourceAsync(correction, Guid.CreateVersion7(), Hash("source"u8.ToArray()), original, "managed:utf8-text-v1", default); Assert.IsTrue(source.IsSuccess);
            var automatic = await sessions.EnqueueAsync(binding, session, default); Assert.IsNotNull(automatic);
            if (!scoped) await Recover(db, provider, connection, automatic.Value);
            var heldAuto = await tasks.AcquireAsync(binding, automatic.Value, default); Assert.IsNotNull(heldAuto?.LeaseToken);
            var autoInput = await tasks.ReadDispatchAsync(binding, automatic.Value, heldAuto.LeaseToken.Value, default); Assert.IsNotNull(autoInput); Assert.IsNull(AiKnowledgeIndexCorrectionInput.Identity(autoInput));
            var document = AiKnowledgeCorrectionWireCodec.Decode(source.Value!.InitialPayload, original, source.Value.ParserRuntimeId, default); Assert.IsNotNull(document);
            var first = await store.SaveAsync(correction, source.Value.Id, 0, Guid.CreateVersion7(), Hash("first"u8.ToArray()), "第一修订",
                document.Replace(document.Blocks[0].Id, document.Blocks[0].Parts[0].Id, "人工第一修订", default), source.Value.ParserRuntimeId, default); Assert.IsTrue(first.IsSuccess);
            Assert.IsNull(await tasks.ReadDispatchAsync(binding, automatic.Value, heldAuto.LeaseToken.Value, default));
            binding = binding with { DocumentVersion = 2 }; correction = correction with { DocumentVersion = 2 };
            var edited = await sessions.EnqueueAsync(binding, session, default); Assert.IsNotNull(edited);
            var held = await tasks.AcquireAsync(binding, edited.Value, default); Assert.IsNotNull(held?.LeaseToken);
            var frozen = await tasks.ReadDispatchAsync(binding, edited.Value, held.LeaseToken.Value, default); Assert.IsNotNull(frozen);
            var identity = AiKnowledgeIndexCorrectionInput.Identity(frozen); Assert.IsNotNull(identity);
            Assert.AreEqual(first.Value!.Id, identity.RevisionId); Assert.AreEqual(source.Value.Id, identity.SourceId); Assert.AreEqual(1, identity.HeadVersion);
            Assert.AreEqual(first.Value.PayloadHash, identity.PayloadHash); Assert.AreEqual(source.Value.SourceHash, identity.SourceHash);
            Assert.IsNotNull(await store.ReadFrozenAsync(correction, identity, original.Format, hash, bytes.Length, original.Policy, default));
            var files = new AiKnowledgeIndexEmbeddingAssertions.ControlledFiles((Guid)seed["FileId"]!, binding.VersionId, bytes, hash) { ThrowOnOpen = true };
            var model = new AiKnowledgeIndexEmbeddingAssertions.ControlledFactory(); var budget = sp.GetRequiredService<IAiOperationBudgetStore>();
            var admission = new AiKnowledgeIndexBudgetService(query, tasks, new(query, command, tasks, ids, options), budget);
            var text = new AiKnowledgeIndexEmbeddingService(query, tenant, state, tasks, sessions, admission, budget, auth, files, files, files, [model], sp.GetRequiredService<AiModelBindingScope>());
            Assert.IsNull(await text.ExecuteTextBatchAsync(binding, edited.Value, held.LeaseToken.Value, 0, default));
            var pdf = new AiKnowledgeIndexPdfPlanningService(query, tenant, state, tasks, sessions, auth, files, files, files, new("unused", "unused", "sha256:" + new string('a', 64)));
            var word = new AiKnowledgeDocumentPlanningService(query, tenant, state, tasks, sessions, auth, files, files, files, new("unused", "unused", "sha256:" + new string('a', 64)));
            // 仅替换格式元数据，确保各原文执行器真的到达修订围栏；不冒充合法 PDF/DOCX 来源。
            await Format("application/pdf");
            Assert.IsNull(await pdf.PlanAsync(binding, edited.Value, held.LeaseToken.Value, default));
            await Format("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            Assert.IsNull(await word.PlanAsync(binding, edited.Value, held.LeaseToken.Value, default));
            await Format("text/plain");
            Assert.AreEqual(0, files.Opens); Assert.AreEqual(0, model.Calls);
            Assert.IsTrue(await tasks.CancelAsync(binding, edited.Value, default));
            var rebuild = new AiKnowledgeIndexRebuildService(query, command, transaction, state, tenant, clock, ids, auth,
                Options.Create(new AiKnowledgeIndexRebuildOptions { Enabled = true }), options, Options.Create(new AiKnowledgePdfRuntimeOptions()), Options.Create(new AiKnowledgeDocxRuntimeOptions()));
            var rebuilt = await rebuild.RebuildAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, new(Guid.CreateVersion7(), edited.Value, 2, 1, true), session, default);
            Assert.IsTrue(rebuilt.IsSuccess, rebuilt.Error?.Code);
            var heldRebuild = await tasks.AcquireAsync(binding, rebuilt.Value!.TaskId, default); Assert.IsNotNull(heldRebuild?.LeaseToken);
            var rebuiltInput = await tasks.ReadDispatchAsync(binding, rebuilt.Value.TaskId, heldRebuild.LeaseToken.Value, default); Assert.IsNotNull(rebuiltInput);
            Assert.AreEqual(identity, AiKnowledgeIndexCorrectionInput.Identity(rebuiltInput));
            var second = await store.SaveAsync(correction, source.Value.Id, 1, Guid.CreateVersion7(), Hash("second"u8.ToArray()), "第二修订", document, source.Value.ParserRuntimeId, default); Assert.IsTrue(second.IsSuccess);
            Assert.IsNull(await tasks.ReadDispatchAsync(binding, rebuilt.Value.TaskId, heldRebuild.LeaseToken.Value, default));
            Assert.AreEqual(first.Value.Id, await db.QuerySingleAsync<Guid>("SELECT CorrectionRevisionId FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = rebuilt.Value.TaskId }));
            // 即使错误地修改旧任务文档版本，也不能采用新的人工头或降级为自动原文。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET DocumentVersion=3 WHERE Id=@Id", new { Id = rebuilt.Value.TaskId });
            Assert.IsNull(await tasks.ReadDispatchAsync(binding with { DocumentVersion = 3 }, rebuilt.Value.TaskId, heldRebuild.LeaseToken.Value, default));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET DocumentVersion=2 WHERE Id=@Id", new { Id = rebuilt.Value.TaskId });
            await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET CorrectionPayloadHash=NULL WHERE Id=@Id", new { Id = edited.Value }));
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id", new { Id = edited.Value }));
            Task Format(string type) => db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentType=@Type WHERE Id=@Id; UPDATE fn_ai_knowledge_import SET ContentType=@Type WHERE VersionId=@Id", new { Type = type, Id = binding.VersionId });
        }
    }
    private static async Task Recover(DbConnection db, DatabaseProvider provider, string connection, Guid task)
    {
        var sqlServer = provider == DatabaseProvider.SqlServer;
        await db.ExecuteAsync(sqlServer ? "ALTER TABLE dbo.fn_ai_knowledge_index_task DROP CONSTRAINT CK_fn_ai_knowledge_index_task_Correction" : "ALTER TABLE fn_ai_knowledge_index_task DROP CHECK CK_fn_ai_knowledge_index_task_Correction");
        await db.ExecuteAsync("ALTER TABLE fn_ai_knowledge_index_task DROP COLUMN CorrectionPayloadHash");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "332_AiKnowledgeIndexCorrection.sql", sqlServer);
            await ReviewFixMigrationRecoverySupport.CreateRunner(provider, connection).MigrateAsync();
            Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.ColumnExistsAsync(db, "fn_ai_knowledge_index_task", "CorrectionPayloadHash", sqlServer));
            Assert.AreEqual("queued", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = task }));
        }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
