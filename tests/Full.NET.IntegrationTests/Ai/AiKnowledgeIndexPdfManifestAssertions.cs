using System.Security.Cryptography;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实 AI SQL/事务配合受控原会话授权；分页输入只验证持久化，不冒充文件解析或真实登录。</summary>
internal static class AiKnowledgeIndexPdfManifestAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, AiOperationBudgetAssertions.TestClock clock)
    {
        await factory.InitializeAsync(); await using var db = AiOperationBudgetAssertions.Connection(factory);
        foreach (var scoped in new[] { false, true })
        {
            var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            clock.UtcNow = new DateTimeOffset((DateTime)p["Now"]!, TimeSpan.Zero);
            var hash = new string('a', 64);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentType='application/pdf',ContentHash=@Hash WHERE Id=@VersionId", new { Hash = hash, VersionId = p["VersionId"] });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET ContentType='application/pdf',ContentHash=@Hash WHERE VersionId=@VersionId", new { Hash = hash, VersionId = p["VersionId"] });
            var binding = new AiKnowledgeIndexTaskBinding((Guid)p["KnowledgeBaseId"]!, (Guid)p["DocumentId"]!, (Guid)p["VersionId"]!, 1, 1, (Guid)p["OwnerUserId"]!);
            var session = new SessionBindingSnapshot(binding.OwnerUserId, (Guid?)p["TenantId"], Guid.CreateVersion7(), "stamp", scoped ? "tenant" : "host", scoped ? "tenant" : "host");
            var auth = new Authorization(session);
            var input = new AiKnowledgePdfModelChunks(binding.KnowledgeBaseId, 1, session.TenantId, (Guid)p["ModelConfigId"]!, 1,
                "openai_compatible", "text-embedding-3-small", hash, AiKnowledgeParsedPdf.ParserVersion, AiKnowledgeCl100kTokenizer.Version, new(),
                Enumerable.Range(0, 17).Select(i => new AiKnowledgePdfChunk(i, i / 16 + 1, i % 16 * 4, 4, "body", "body", 1)).ToArray());
            var plan = AiKnowledgeIndexPdfPlan.Create(input, default)!;
            var taskId = await Use(async (tasks, sessions, _, _) => await sessions.EnqueueAsync(binding, session, default)); Assert.IsNotNull(taskId);
            var held = await Use((tasks, _, _, _) => tasks.AcquireAsync(binding, taskId.Value, default)); Assert.IsNotNull(held); Assert.IsNotNull(held.LeaseToken);
            var lease = held.LeaseToken.Value;
            var raced = await Task.WhenAll(Write(plan), Write(plan)); Assert.IsTrue(raced.All(value => value));
            var original = AiKnowledgeIndexPdfManifestCodec.Encode(plan, default);
            CollectionAssert.AreEqual(original, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_text_manifest WHERE Id=@Id", new { Id = taskId.Value }));
            var snapshot = await Read(); Assert.IsNotNull(snapshot); Assert.AreEqual(plan.Summary, snapshot.Manifest.Summary);
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(original)), snapshot.PayloadHash);
            p["TaskId"] = taskId.Value; p["LeaseToken"] = lease; p["Now"] = clock.UtcNow.UtcDateTime;
            p["SessionId"] = session.SessionId; p["SessionKind"] = session.SessionKind; p["SecurityStamp"] = session.SecurityStamp;
            p["ActorScope"] = session.ActorScope; p["EffectiveScope"] = session.EffectiveScope;
            p["ContentHash"] = hash; p["ContentType"] = "application/pdf"; p["SizeBytes"] = 10;
            p["FormatKey"] = AiKnowledgeIndexPdfManifestCodec.FormatKey; p["Payload"] = original; p["PayloadHash"] = snapshot.PayloadHash;
            var sqlServer = db is Microsoft.Data.SqlClient.SqlConnection;
            var insert = sqlServer ? (scoped ? AiKnowledgeIndexTextManifestSql.InsertSqlServerTenant : AiKnowledgeIndexTextManifestSql.InsertSqlServerHost)
                : (scoped ? AiKnowledgeIndexTextManifestSql.InsertTenant : AiKnowledgeIndexTextManifestSql.InsertHost);
            var find = scoped ? AiKnowledgeIndexTextManifestSql.FindTenant : AiKnowledgeIndexTextManifestSql.FindHost;
            foreach (var (key, value) in new (string, object)[] { ("LeaseToken", Guid.CreateVersion7()), ("SessionId", Guid.CreateVersion7()),
                ("SecurityStamp", "other"), ("FileId", Guid.CreateVersion7()), ("ContentHash", "other"), ("ContentType", "text/plain"),
                ("OwnerUserId", Guid.CreateVersion7()), ("KnowledgeBaseVersion", 2) })
            {
                var denied = new Dictionary<string, object?>(p) { [key] = value };
                Assert.AreEqual(0, await db.ExecuteAsync(insert.Text, denied));
                Assert.IsNull(await db.QuerySingleOrDefaultAsync(find.Text, denied));
            }
            if (scoped)
            {
                var denied = new Dictionary<string, object?>(p) { ["TenantId"] = Guid.CreateVersion7() };
                Assert.AreEqual(0, await db.ExecuteAsync(insert.Text, denied)); Assert.IsNull(await db.QuerySingleOrDefaultAsync(find.Text, denied));
                Assert.IsNull(await db.QuerySingleOrDefaultAsync(AiKnowledgeIndexTextManifestSql.FindHost.Text, p));
            }
            var changed = AiKnowledgeIndexPdfPlan.Create(input with { Chunks = [input.Chunks[0]] }, default)!;
            Assert.IsFalse(await Write(changed));
            Assert.IsNull(await Use((_, _, _, text) => text.ReadAsync(binding, taskId.Value, lease, default)));
            foreach (var permission in new[] { "ai.knowledge_bases.read", "ai.knowledge_documents.read", "ai.knowledge_documents.source_read", "ai.knowledge_documents.upload", "ai.knowledge_documents.index_enqueue" })
            {
                auth.DeniedPermission = permission; Assert.IsNull(await Read()); Assert.IsFalse(await Write(plan)); auth.DeniedPermission = null;
            }
            foreach (var (flip, restore) in new[]
            {
                ("UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@DocumentId", "UPDATE fn_ai_knowledge_document SET IsDeleted=0 WHERE Id=@DocumentId"),
                ("UPDATE fn_ai_knowledge_base SET EmbeddingModelVersion=2 WHERE Id=@KnowledgeBaseId", "UPDATE fn_ai_knowledge_base SET EmbeddingModelVersion=1 WHERE Id=@KnowledgeBaseId"),
                ("UPDATE fn_ai_model_config SET Version=2 WHERE Id=@ModelConfigId", "UPDATE fn_ai_model_config SET Version=1 WHERE Id=@ModelConfigId"),
                ("UPDATE fn_ai_knowledge_import SET StateKey='cleanup_pending' WHERE VersionId=@VersionId", "UPDATE fn_ai_knowledge_import SET StateKey='completed' WHERE VersionId=@VersionId"),
                ("UPDATE fn_ai_knowledge_version SET ContentHash='changed' WHERE Id=@VersionId", "UPDATE fn_ai_knowledge_version SET ContentHash=@Hash WHERE Id=@VersionId"),
                ("UPDATE fn_ai_knowledge_version SET ContentType='text/plain' WHERE Id=@VersionId", "UPDATE fn_ai_knowledge_version SET ContentType='application/pdf' WHERE Id=@VersionId"),
                ("UPDATE fn_ai_knowledge_index_session SET SecurityStamp='changed' WHERE IndexTaskId=@Id", "UPDATE fn_ai_knowledge_index_session SET SecurityStamp='stamp' WHERE IndexTaskId=@Id")
            })
            {
                p["Id"] = taskId.Value; p["Hash"] = hash;
                Assert.AreEqual(1, await db.ExecuteAsync(flip, p)); Assert.IsNull(await Read()); Assert.IsFalse(await Write(plan));
                Assert.AreEqual(1, await db.ExecuteAsync(restore, p)); Assert.IsNotNull(await Read());
            }
            Assert.IsNull(await Use((_, _, store, _) => store.ReadSnapshotAsync(binding, taskId.Value, Guid.CreateVersion7(), default)));
            var corrupt = new byte[8];
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_text_manifest SET Payload=@Payload,PayloadHash=@Hash WHERE Id=@Id", new { Payload = corrupt, Hash = Convert.ToHexString(SHA256.HashData(corrupt)), Id = taskId.Value });
            Assert.IsNull(await Read()); Assert.IsFalse(await Write(plan));
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_text_manifest WHERE Id=@Id", new { Id = taskId.Value });
            var expiry = clock.UtcNow;
            Assert.IsFalse(await Use((_, _, store, _) => store.PersistAsync(binding, taskId.Value, lease, plan, default), expire: true));
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@Id", new { Id = taskId.Value }));
            clock.UtcNow = expiry; Assert.IsTrue(await Write(plan));
            // 复用既有清理路径，不让历史表名造成 PDF 清单遗漏。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1,Version=2 WHERE Id=@DocumentId", p);
            await Use(async (_, _, _, _) =>
            {
                await using var scope = factory.Services.CreateAsyncScope(); SetScope(scope.ServiceProvider);
                var cleanup = scope.ServiceProvider.GetRequiredService<AiKnowledgeIndexCleanupStore>();
                await cleanup.CleanupDocumentAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.OwnerUserId, default);
                return true;
            });
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@Id", new { Id = taskId.Value }));

            Task<bool> Write(AiKnowledgeIndexPdfPlan value) => Use((_, _, store, _) => store.PersistAsync(binding, taskId.Value, lease, value, default));
            Task<AiKnowledgeIndexPdfManifestSnapshot?> Read() => Use((_, _, store, _) => store.ReadSnapshotAsync(binding, taskId.Value, lease, default));
            void SetScope(IServiceProvider services)
            {
                var writer = services.GetRequiredService<ICurrentTenantContextWriter>();
                if (session.TenantId is { } id) writer.SetTenant(new TenantContext(id, "pdf-test", "PDF测试")); else writer.SetHost();
            }
            async Task<T> Use<T>(Func<AiKnowledgeIndexTaskStore, AiKnowledgeIndexSessionService, AiKnowledgeIndexPdfManifestStore, AiKnowledgeIndexTextManifestStore, Task<T>> action, bool expire = false)
            {
                await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider; SetScope(services);
                var query = services.GetRequiredService<IQueryExecutor>(); var command = services.GetRequiredService<ICommandExecutor>();
                var tenant = services.GetRequiredService<ICurrentTenant>(); var state = services.GetRequiredService<IDataTransactionState>();
                var transaction = services.GetRequiredService<ICommandTransaction>(); var options = services.GetRequiredService<IOptions<DatabaseOptions>>();
                var tasks = new AiKnowledgeIndexTaskStore(query, command, tenant, clock, services.GetRequiredService<IIdGenerator>());
                var sessions = new AiKnowledgeIndexSessionService(query, command, transaction, state, tenant, clock, services.GetRequiredService<IIdGenerator>(), tasks, auth);
                ICommandExecutor saving = expire ? new ExpireAfterInsert(command, clock) : command;
                return await action(tasks, sessions, new(query, saving, transaction, state, tenant, clock, tasks, sessions, auth, options),
                    new(query, command, transaction, state, tenant, clock, tasks, sessions, auth, options));
            }
        }
    }

    private sealed class Authorization(SessionBindingSnapshot expected) : IBackgroundSessionAuthorization
    {
        internal string? DeniedPermission;
        public Task<AuthorizedSessionActor?> AuthorizeAsync(SessionBindingSnapshot binding, string permissionCode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AuthorizedSessionActor?>(binding != expected || permissionCode == DeniedPermission ? null : new(binding.UserId, binding.TenantId, binding.SessionId));
        }
    }
    private sealed class ExpireAfterInsert(ICommandExecutor inner, AiOperationBudgetAssertions.TestClock clock) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var count = await inner.ExecuteAsync(statement, parameters, cancellationToken);
            if (statement.Name.StartsWith("ai.knowledge_text_manifest.insert", StringComparison.Ordinal) && count == 1) clock.UtcNow = clock.UtcNow.AddSeconds(30);
            return count;
        }
    }
}
