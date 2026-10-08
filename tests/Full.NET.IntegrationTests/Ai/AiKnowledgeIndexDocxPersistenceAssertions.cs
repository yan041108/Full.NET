using System.Security.Cryptography;
using Dapper;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库验证 DOCX 完整原文、向量及结算账本关联；原子激活和完成恢复沿用受控原会话，不冒充上传或云模型执行。</summary>
internal static class AiKnowledgeIndexDocxPersistenceAssertions
{
    internal sealed class WorkerControls
    {
        internal IBackgroundSessionAuthorization Authorization = null!;
        internal void Register(IServiceCollection services)
        {
            new AiModule().AddBackgroundServices(services, new ConfigurationBuilder().Build());
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                && (d.ImplementationType == typeof(AiKnowledgeIndexHostedProcessor) || d.ImplementationType == typeof(AiAgentRunHostedProcessor))).ToArray()) services.Remove(descriptor);
            services.RemoveAll<IBackgroundSessionAuthorization>(); services.AddScoped<IBackgroundSessionAuthorization>(_ => Authorization);
        }
    }
    internal sealed record NativeProbe(SessionBindingSnapshot Session, Func<AiKnowledgeIndexTaskBinding, Task<Guid>> Enqueue,
        Func<AiKnowledgeIndexTaskBinding, Task> ReadReady, Func<AiKnowledgeIndexTaskBinding, Task> Recover);
    internal static async Task VerifyAsync(FullNetApiFactory factory, AiOperationBudgetAssertions.TestClock clock, WorkerControls controls, NativeProbe? native = null)
    {
        await factory.InitializeAsync(); await using var db = AiOperationBudgetAssertions.Connection(factory);
        foreach (var scoped in new[] { false, true })
        {
            var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            var probe = scoped ? null : native;
            if (probe is not null)
            {
                p["OwnerUserId"] = probe.Session.UserId;
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET OwnerUserId=@OwnerUserId WHERE Id=@KnowledgeBaseId", p);
            }
            clock.UtcNow = new DateTimeOffset((DateTime)p["Now"]!, TimeSpan.Zero); var now = clock.UtcNow;
            var hash = new string('A', 64); var modelId = scoped ? "text-embedding-3-large" : "text-embedding-3-small";
            const string mime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentType=@Mime,ContentHash=@Hash,SizeBytes=97 WHERE Id=@VersionId", new { Mime = mime, Hash = hash, VersionId = p["VersionId"] });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET ContentType=@Mime,ContentHash=@Hash,SizeBytes=97 WHERE VersionId=@VersionId", new { Mime = mime, Hash = hash, VersionId = p["VersionId"] });
            await db.ExecuteAsync("UPDATE fn_ai_model_config SET ModelId=@Model WHERE Id=@Id", new { Model = modelId, Id = p["ModelConfigId"] });
            var binding = new AiKnowledgeIndexTaskBinding((Guid)p["KnowledgeBaseId"]!, (Guid)p["DocumentId"]!, (Guid)p["VersionId"]!, 1, 1, (Guid)p["OwnerUserId"]!);
            var session = probe?.Session ?? new SessionBindingSnapshot(binding.OwnerUserId, (Guid?)p["TenantId"], Guid.CreateVersion7(), "stamp", scoped ? "tenant" : "host", scoped ? "tenant" : "host");
            var auth = new Authorization(session); controls.Authorization = auth;
            var text = new System.Text.StringBuilder(); var blocks = new List<AiKnowledgeDocxBlock>();
            for (var i = 0; i < 17; i++)
            {
                var content = $"正文 {i} 😀";
                // 同一完整计划包含正文段落及单列表格，真实召回不能丢失服务器逻辑坐标。
                blocks.Add(i < 8 ? new(i, "paragraph", text.Length, content.Length, i + 1, null, null, null, 0, false)
                    : new(i, "tableCell", text.Length, content.Length, i + 1, 1, i - 7, 1, 1, i == 8));
                text.Append(content).Append('\n');
            }
            var document = new AiKnowledgeParsedDocx(hash.ToLowerInvariant(), 97, text.ToString(), blocks.AsReadOnly());
            var chunks = AiKnowledgeDocxModelChunker.Chunk(new AiKnowledgeBaseRecord { Id = binding.KnowledgeBaseId, Version = 1, TenantId = session.TenantId, IsEnabled = true, EmbeddingModelConfigId = (Guid)p["ModelConfigId"]!, EmbeddingModelVersion = 1 },
                new AiModelConfigRecord { Id = (Guid)p["ModelConfigId"]!, Version = 1, TenantId = session.TenantId, IsEnabled = true, ProviderKey = "openai_compatible", ModelId = modelId, EndpointBaseUrl = "https://api.openai.com/v1" }, document, new(), default);
            Assert.IsTrue(chunks.IsSuccess); var input = chunks.Output!; var plan = AiKnowledgeIndexDocxPlan.Create(input, default)!;
            Guid? task = probe is null ? await Use(a => a.Sessions.EnqueueAsync(binding, session, default)) : await probe.Enqueue(binding); Assert.IsNotNull(task); var taskId = task.Value;
            var held = await Use(a => a.Tasks.AcquireAsync(binding, taskId, default)); Assert.IsNotNull(held); Assert.IsNotNull(held.LeaseToken); var lease = held.LeaseToken.Value;
            Assert.IsTrue(await Use(a => a.Manifests.PersistAsync(binding, taskId, lease, plan, default)));
            var manifest = AiKnowledgeIndexDocxManifestCodec.Encode(plan, default); var manifestHash = Hash(manifest);
            CollectionAssert.AreEqual(manifest, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_docx_manifest WHERE Id=@Id", new { Id = taskId }));
            var restoredManifest = await Use(a => a.Manifests.ReadAsync(binding, taskId, lease, default)); Assert.IsNotNull(restoredManifest);
            Assert.AreEqual(document.Text, restoredManifest.Source.Document.Text);
            var model = await Use(a => a.Query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(scoped ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat, AiSqlParameters.Create(("ModelConfigId", input.ModelConfigId)), default)); Assert.IsNotNull(model);
            p["TaskId"] = taskId; p["LeaseToken"] = lease; p["Now"] = now.UtcDateTime;
            p["SessionId"] = session.SessionId; p["SessionKind"] = session.SessionKind; p["SecurityStamp"] = session.SecurityStamp;
            p["ActorScope"] = session.ActorScope; p["EffectiveScope"] = session.EffectiveScope; p["ScopeKey"] = scoped ? session.TenantId!.Value.ToString("N") : "host";
            p["ContentHash"] = hash; p["ContentType"] = mime; p["SizeBytes"] = 97; p["DocxManifestHash"] = manifestHash;
            p["ProviderKey"] = model.ProviderKey; p["ModelId"] = model.ModelId; p["Dimensions"] = scoped ? 3072 : 1536;
            p["FormatKey"] = AiKnowledgeIndexDocxVectorCodec.FormatKey; p["DistanceKey"] = "cosine";
            var sqlServer = db is Microsoft.Data.SqlClient.SqlConnection;
            var insert = sqlServer ? (scoped ? AiKnowledgeIndexDocxVectorSql.InsertDocxSqlServerTenant : AiKnowledgeIndexDocxVectorSql.InsertDocxSqlServerHost)
                : (scoped ? AiKnowledgeIndexDocxVectorSql.InsertDocxTenant : AiKnowledgeIndexDocxVectorSql.InsertDocxHost);
            var find = sqlServer ? (scoped ? AiKnowledgeIndexDocxVectorSql.FindDocxSqlServerTenant : AiKnowledgeIndexDocxVectorSql.FindDocxSqlServerHost)
                : (scoped ? AiKnowledgeIndexDocxVectorSql.FindDocxTenant : AiKnowledgeIndexDocxVectorSql.FindDocxHost);
            p["ExpectedBatchCount"] = 2;
            var receipts = sqlServer ? (scoped ? AiKnowledgeIndexDocxVectorSql.ReceiptsDocxSqlServerTenant : AiKnowledgeIndexDocxVectorSql.ReceiptsDocxSqlServerHost)
                : (scoped ? AiKnowledgeIndexDocxVectorSql.ReceiptsDocxTenant : AiKnowledgeIndexDocxVectorSql.ReceiptsDocxHost);
            long charged = 0; var operations = new List<Guid>();
            for (var ordinal = 0; ordinal < 2; ordinal++)
            {
                Assert.IsNull(await WholeProof());
                var vectors = plan.Batches[ordinal].Chunks.Select(_ => { var v = new float[scoped ? 3072 : 1536]; v[0] = 1; v[1] = BitConverter.Int32BitsToSingle(int.MinValue); return new ReadOnlyMemory<float>(v); }).ToArray();
                var request = AiEmbeddingBudgetRequest.Create(Guid.CreateVersion7(), model, plan.Batches[ordinal].Chunks.Select(c => c.Text).ToArray());
                var batch = await Use(a => a.Batches.PrepareAsync(binding, taskId, lease, ordinal, AiOperationBudgetStore.Fingerprint(request), vectors.Length, default)); Assert.IsNotNull(batch);
                request = request with { OperationId = batch.Id }; operations.Add(batch.Id);
                await Use(a => a.Budgets.ReserveAsync(request));
                Assert.IsFalse(await Write()); Assert.IsNull(await Read());
                await Use(async a => { await a.Budgets.SettleAsync(batch.Id, ordinal == 0 ? new AiOperationUsage(1, 0) : new AiOperationUsage(null, null), "succeeded"); return true; });
                var original = AiKnowledgeIndexDocxVectorCodec.Encode(plan, ordinal, vectors, default);
                p["OperationId"] = batch.Id; p["BatchOrdinal"] = ordinal; p["RequestHash"] = batch.RequestHash; p["InputCount"] = vectors.Length;
                p["Payload"] = original; p["PayloadHash"] = Hash(original);
                var chargesBefore = await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = batch.Id });
                var raced = await Task.WhenAll(Write(), Write()); Assert.IsTrue(raced.All(value => value));
                CollectionAssert.AreEqual(original, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = batch.Id }));
                var restored = await Read(); Assert.IsNotNull(restored); Assert.AreEqual(batch.Id, restored.OperationId);
                Assert.AreEqual(chargesBefore, await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = batch.Id }));
                Assert.AreEqual(ordinal * 16, restored.Snapshot.Payload.Chunks[0].Source.BlockSequence); Assert.AreEqual(vectors.Length, restored.Snapshot.Payload.Chunks.Count);
                Assert.AreEqual(int.MinValue, BitConverter.SingleToInt32Bits(restored.Snapshot.Payload.Chunks[0].Vector.Span[1]));
                var altered = vectors.ToArray(); var changed = altered[0].ToArray(); changed[0] = 2; altered[0] = changed;
                Assert.IsFalse(await Use(a => a.Vectors.PersistAsync(binding, taskId, lease, batch.Id, plan, ordinal, altered, default)));
                Assert.IsFalse(await Use(a => a.Vectors.PersistAsync(binding, taskId, lease, Guid.CreateVersion7(), plan, ordinal, vectors, default)));
                // 每个拒绝条件从同一健康记录单独翻转，恢复后再证明可读。
                foreach (var (flip, restore) in new[]
                {
                    ("UsageStatus='reserved'", ordinal == 0 ? "UsageStatus='known'" : "UsageStatus='unknown'"),
                    ("Outcome='failed'", "Outcome='succeeded'"),
                    ("ScopeKey='other'", "ScopeKey=@ScopeKey"),
                    ("RequestHash='other'", "RequestHash=@RequestHash"),
                    ("ModelConfigId=@LeaseToken", "ModelConfigId=@ModelConfigId"),
                    ("ProviderKey='other'", "ProviderKey=@ProviderKey"),
                    ("ModelId='other'", "ModelId=@ModelId"),
                    ("SettledAtUtc=NULL", "SettledAtUtc=@Now")
                })
                {
                    await db.ExecuteAsync("UPDATE fn_ai_operation_budget SET " + flip + " WHERE Id=@OperationId", p);
                    Assert.IsFalse(await Write(), flip); Assert.IsNull(await Read(), flip);
                    Assert.AreEqual(0, await db.ExecuteAsync(insert.Text, p), flip); Assert.IsNull(await db.QuerySingleOrDefaultAsync(find.Text, p), flip);
                    await db.ExecuteAsync("UPDATE fn_ai_operation_budget SET " + restore + " WHERE Id=@OperationId", p);
                    Assert.IsNotNull(await Read(), restore);
                }
                foreach (var (key, value) in new (string, object)[] { ("LeaseToken", Guid.CreateVersion7()), ("TaskId", Guid.CreateVersion7()),
                    ("SessionId", Guid.CreateVersion7()), ("SecurityStamp", "other"), ("FileId", Guid.CreateVersion7()), ("ContentHash", "other"),
                    ("ContentType", "text/plain"), ("OwnerUserId", Guid.CreateVersion7()), ("KnowledgeBaseVersion", 2), ("DocxManifestHash", new string('0', 64)) })
                {
                    var denied = new Dictionary<string, object?>(p) { [key] = value };
                    Assert.AreEqual(0, await db.ExecuteAsync(insert.Text, denied), key); Assert.IsNull(await db.QuerySingleOrDefaultAsync(find.Text, denied), key);
                }
                if (scoped)
                {
                    var denied = new Dictionary<string, object?>(p) { ["TenantId"] = Guid.CreateVersion7() };
                    Assert.AreEqual(0, await db.ExecuteAsync(insert.Text, denied)); Assert.IsNull(await db.QuerySingleOrDefaultAsync(find.Text, denied));
                    Assert.IsNull(await db.QuerySingleOrDefaultAsync(sqlServer ? AiKnowledgeIndexDocxVectorSql.FindDocxSqlServerHost.Text : AiKnowledgeIndexDocxVectorSql.FindDocxHost.Text, p));
                }
                foreach (var permission in new[] { "ai.knowledge_bases.read", "ai.knowledge_documents.read", "ai.knowledge_documents.source_read", "ai.knowledge_documents.upload", "ai.knowledge_documents.index_enqueue" })
                {
                    auth.DeniedPermission = permission; Assert.IsFalse(await Write()); Assert.IsNull(await Read()); auth.DeniedPermission = null;
                }
                var corrupt = new byte[8];
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_docx_manifest SET Payload=@Payload,PayloadHash=@Hash WHERE Id=@Id", new { Payload = corrupt, Hash = Hash(corrupt), Id = taskId });
                Assert.IsFalse(await Write()); Assert.IsNull(await Read()); Assert.AreEqual(0, await db.ExecuteAsync(insert.Text, p));
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_docx_manifest SET Payload=@Payload,PayloadHash=@Hash WHERE Id=@Id", new { Payload = manifest, Hash = manifestHash, Id = taskId });
                // 保留旧摘要但篡改字节，SQL 与应用都必须拒绝。
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_docx_manifest SET Payload=@Payload WHERE Id=@Id", new { Payload = corrupt, Id = taskId });
                Assert.IsNull(await Read()); Assert.AreEqual(0, await db.ExecuteAsync(insert.Text, p));
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_docx_manifest SET Payload=@Payload WHERE Id=@Id", new { Payload = manifest, Id = taskId });
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload,PayloadHash=@Hash WHERE Id=@Id", new { Payload = corrupt, Hash = Hash(corrupt), Id = batch.Id });
                Assert.IsNull(await Read()); Assert.IsFalse(await Write());
                await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = batch.Id });
                Assert.IsFalse(await Use(a => a.Vectors.PersistAsync(binding, taskId, lease, batch.Id, plan, ordinal, vectors, default), expire: true));
                Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = batch.Id }));
                clock.UtcNow = now; Assert.IsTrue(await Write());
                Assert.AreEqual(chargesBefore, await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = batch.Id }));
                charged += chargesBefore;
                Task<bool> Write() => Use(a => a.Vectors.PersistAsync(binding, taskId, lease, batch.Id, plan, ordinal, vectors, default));
                Task<AiKnowledgeIndexDocxStoredBatch?> Read() => Use(a => a.Vectors.ReadSnapshotAsync(binding, taskId, lease, ordinal, default));
            }
            var proof = await WholeProof(); Assert.IsNotNull(proof); Assert.HasCount(2, proof.Receipts);
            Assert.AreEqual(manifestHash, proof.Manifest.PayloadHash); Assert.AreEqual(plan.Summary.PlanHash, proof.Manifest.Manifest.Summary.PlanHash);
            Assert.AreEqual(2, (await db.QueryAsync(receipts.Text, p)).Count());
            var firstPayload = await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = operations[0] });
            foreach (var keepHash in new[] { true, false })
            {
                var broken = new byte[8];
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload,PayloadHash=@Hash WHERE Id=@Id",
                    new { Payload = broken, Hash = keepHash ? Hash(firstPayload) : Hash(broken), Id = operations[0] });
                Assert.IsNull(await WholeProof());
                if (keepHash) Assert.AreEqual(1, (await db.QueryAsync(receipts.Text, p)).Count());
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload,PayloadHash=@Hash WHERE Id=@Id",
                    new { Payload = firstPayload, Hash = Hash(firstPayload), Id = operations[0] });
                Assert.IsNotNull(await WholeProof());
            }
            foreach (var (flip, restore) in new[] { ("Outcome='failed'", "Outcome='succeeded'"),
                ("SettledAtUtc=NULL", "SettledAtUtc=@Now"), ("UsageStatus='reserved'", "UsageStatus='known'") })
            {
                await db.ExecuteAsync("UPDATE fn_ai_operation_budget SET " + flip + " WHERE Id=@Id", new { Id = operations[0], Now = now.UtcDateTime });
                Assert.IsNull(await WholeProof()); Assert.AreEqual(1, (await db.QueryAsync(receipts.Text, p)).Count());
                await db.ExecuteAsync("UPDATE fn_ai_operation_budget SET " + restore + " WHERE Id=@Id", new { Id = operations[0], Now = now.UtcDateTime });
                Assert.IsNotNull(await WholeProof());
            }
            // 多出的未结算批次不能被账本 JOIN 隐藏，原始批次数必须匹配完整清单。
            var extra = await Use(a => a.Batches.PrepareAsync(binding, taskId, lease, 2, new string('b', 64), 1, default)); Assert.IsNotNull(extra);
            Assert.IsNull(await WholeProof()); Assert.AreEqual(0, (await db.QueryAsync(receipts.Text, p)).Count());
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_batch WHERE Id=@Id", new { Id = extra.Id }); Assert.IsNotNull(await WholeProof());
            foreach (var permission in new[] { "ai.knowledge_bases.read", "ai.knowledge_documents.read", "ai.knowledge_documents.source_read", "ai.knowledge_documents.upload", "ai.knowledge_documents.index_enqueue" })
            { auth.DeniedPermission = permission; Assert.IsNull(await WholeProof()); auth.DeniedPermission = null; }
            Task<AiKnowledgeIndexDocxGenerationProof?> WholeProof() => Use(a => a.Vectors.ReadWholePlanProofAsync(binding, taskId, lease, default));
            foreach (var (flip, restore) in new[]
            {
                ("UPDATE fn_ai_knowledge_base SET EmbeddingModelVersion=2 WHERE Id=@KnowledgeBaseId", "UPDATE fn_ai_knowledge_base SET EmbeddingModelVersion=1 WHERE Id=@KnowledgeBaseId"),
                ("UPDATE fn_ai_model_config SET Version=2 WHERE Id=@ModelConfigId", "UPDATE fn_ai_model_config SET Version=1 WHERE Id=@ModelConfigId"),
                ("UPDATE fn_ai_knowledge_import SET StateKey='cleanup_pending' WHERE VersionId=@VersionId", "UPDATE fn_ai_knowledge_import SET StateKey='completed' WHERE VersionId=@VersionId"),
                ("UPDATE fn_ai_knowledge_version SET SizeBytes=98 WHERE Id=@VersionId", "UPDATE fn_ai_knowledge_version SET SizeBytes=97 WHERE Id=@VersionId"),
                ("UPDATE fn_ai_knowledge_index_session SET SecurityStamp='other' WHERE IndexTaskId=@TaskId", "UPDATE fn_ai_knowledge_index_session SET SecurityStamp=@SecurityStamp WHERE IndexTaskId=@TaskId")
            })
            {
                await db.ExecuteAsync(flip, p);
                Assert.IsNull(await Use(a => a.Vectors.ReadSnapshotAsync(binding, taskId, lease, 0, default)));
                await db.ExecuteAsync(restore, p);
                Assert.IsNotNull(await Use(a => a.Vectors.ReadSnapshotAsync(binding, taskId, lease, 0, default)));
            }
            Assert.IsNotNull(await WholeProof());
            Assert.AreEqual(charged, await db.QuerySingleAsync<long>("SELECT SUM(ChargedTokens) FROM fn_ai_operation_budget WHERE ModelConfigId=@Id", new { Id = model.Id }));
            Assert.AreEqual("running", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = taskId }));
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE DocumentId=@Id", new { Id = binding.DocumentId }));
            // 完成前拒绝写入的失败与第一次完成 UPDATE 后的失败都不能留下孤立终态。
            proof = await WholeProof(); Assert.IsNotNull(proof);
            foreach (var permission in new[] { "ai.knowledge_bases.read", "ai.knowledge_documents.read", "ai.knowledge_documents.source_read", "ai.knowledge_documents.upload", "ai.knowledge_documents.index_enqueue" })
            {
                auth.DeniedPermission = permission;
                Assert.IsFalse(await Use(a => a.Activations.ActivateAsync(binding, taskId, lease, proof, default)));
                auth.DeniedPermission = null;
            }
            Assert.IsFalse(await Use(a => a.Activations.ActivateAsync(binding, taskId, lease, proof, default), failActivation: true));
            Assert.AreEqual("running", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", p));
            Assert.IsTrue(await Use(a => a.Consumer.ActivateGenerationAsync(binding, taskId, lease, default)));
            Assert.AreEqual("completed", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
            var pointerBefore = await db.QuerySingleAsync<AiKnowledgeIndexActivationRecord>("SELECT Id,DocumentId,IndexTaskId,VersionId,PlanHash,ManifestHash,ReceiptHash,ReceiptJson,LeaseToken,ActivatedAtUtc FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", p);
            Assert.AreEqual(taskId, pointerBefore.IndexTaskId); Assert.AreEqual(lease, pointerBefore.LeaseToken);
            Assert.IsTrue(await Use(a => a.Consumer.ExecuteGenerationAsync(binding, taskId, lease, default)));
            Assert.IsTrue(await Use(a => a.Consumer.ExecuteGenerationAsync(binding, taskId, lease, default)));
            Assert.IsFalse(await Use(a => a.Consumer.ExecuteGenerationAsync(binding, taskId, Guid.CreateVersion7(), default)));
            foreach (var permission in new[] { "ai.knowledge_bases.read", "ai.knowledge_documents.read", "ai.knowledge_documents.source_read", "ai.knowledge_documents.upload", "ai.knowledge_documents.index_enqueue" })
            {
                auth.DeniedPermission = permission;
                Assert.IsFalse(await Use(a => a.Consumer.ExecuteGenerationAsync(binding, taskId, lease, default)));
                auth.DeniedPermission = null;
            }
            foreach (var (flip, restore) in new[]
            {
                ("UPDATE fn_ai_model_config SET EndpointBaseUrl='https://other.example/v1' WHERE Id=@ModelConfigId", "UPDATE fn_ai_model_config SET EndpointBaseUrl=@Endpoint WHERE Id=@ModelConfigId"),
                ("UPDATE fn_ai_model_config SET OrganizationId='other' WHERE Id=@ModelConfigId", "UPDATE fn_ai_model_config SET OrganizationId=@Organization WHERE Id=@ModelConfigId"),
                ("UPDATE fn_ai_knowledge_index_session SET SecurityStamp='other' WHERE IndexTaskId=@TaskId", "UPDATE fn_ai_knowledge_index_session SET SecurityStamp=@SecurityStamp WHERE IndexTaskId=@TaskId"),
                ("UPDATE fn_ai_operation_budget SET Outcome='failed' WHERE Id=@OperationId", "UPDATE fn_ai_operation_budget SET Outcome='succeeded' WHERE Id=@OperationId")
            })
            {
                p["Endpoint"] = model.EndpointBaseUrl; p["Organization"] = model.OrganizationId;
                await db.ExecuteAsync(flip, p);
                Assert.IsFalse(await Use(a => a.Consumer.ExecuteGenerationAsync(binding, taskId, lease, default)));
                await db.ExecuteAsync(restore, p);
                Assert.IsTrue(await Use(a => a.Consumer.ExecuteGenerationAsync(binding, taskId, lease, default)));
            }
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Broken WHERE Id=@Id", new { Broken = new byte[8], Id = operations[0] });
            Assert.IsFalse(await Use(a => a.Consumer.ExecuteGenerationAsync(binding, taskId, lease, default)));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload WHERE Id=@Id", new { Payload = firstPayload, Id = operations[0] });
            Assert.IsTrue(await Use(a => a.Consumer.ExecuteGenerationAsync(binding, taskId, lease, default)));
            Assert.AreEqual(pointerBefore, await db.QuerySingleAsync<AiKnowledgeIndexActivationRecord>("SELECT Id,DocumentId,IndexTaskId,VersionId,PlanHash,ManifestHash,ReceiptHash,ReceiptJson,LeaseToken,ActivatedAtUtc FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", p));
            Assert.AreEqual(charged, await db.QuerySingleAsync<long>("SELECT SUM(ChargedTokens) FROM fn_ai_operation_budget WHERE ModelConfigId=@Id", new { Id = model.Id }));
            // 当前读取者使用真实元数据 SQL 和完整证明，不要求历史写入会话仍可派发。
            var readParameters = new Dictionary<string, object?>(AiSqlParameters.Create(("OwnerUserId", binding.OwnerUserId), ("ActiveRoleIdsJson", "[]"),
                ("ActiveOrganizationUnitIdsJson", "[]"), ("KnowledgeBaseId", binding.KnowledgeBaseId), ("DocumentId", binding.DocumentId), ("VersionId", binding.VersionId)));
            var status = await ReadStatus(); Assert.IsNotNull(status);
            auth.DeniedPermission = "ai.knowledge_documents.index_enqueue";
            Assert.IsFalse(await Use(a => a.Activations.IsActiveAsync(binding, taskId, lease, default)));
            Assert.IsTrue(await Use(a => a.Activations.IsReadyForReadAsync(status, readParameters, default)));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Broken WHERE Id=@Id", new { Broken = new byte[8], Id = operations[0] });
            Assert.IsFalse(await Use(a => a.Activations.IsReadyForReadAsync(status, readParameters, default)));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload WHERE Id=@Id", new { Payload = firstPayload, Id = operations[0] });
            if (probe is not null) await probe.ReadReady(binding);
            auth.DeniedPermission = null;
            if (native is null)
            await using (var retrievalScope = factory.Services.CreateAsyncScope())
            {
                var writer = retrievalScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                if (session.TenantId is { } currentTenant) writer.SetTenant(new(currentTenant, "retrieval", "DOCX检索测试")); else writer.SetHost();
                charged = checked(charged + await AiKnowledgeRetrievalAssertions.VerifyAsync(retrievalScope.ServiceProvider, db, binding, factory));
            }
            // 生产 Worker DI、双库格式扫描及 Retry 条件 UPDATE：已结算完整数据恢复不再次读取文件或派发模型。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='failed',AttemptCount=0,LeaseToken=NULL,LeaseExpiresAtUtc=NULL WHERE Id=@TaskId", p);
            if (probe is null)
                Assert.IsTrue((await Use(a => a.Retry.RetryAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, taskId, session, default))).IsSuccess);
            else await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='queued' WHERE Id=@TaskId", p);
            Assert.AreEqual(0, await RunWorker(false));
            if (probe is null) Assert.AreEqual(1, await RunWorker(true)); else await probe.Recover(binding);
            Assert.AreEqual("completed", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
            var recoveredLease = await db.QuerySingleAsync<Guid>("SELECT LeaseToken FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", p);
            Assert.AreNotEqual(lease, recoveredLease);
            Assert.IsTrue(await Use(a => a.Activations.IsActiveAsync(binding, taskId, recoveredLease, default)));
            Assert.AreEqual(charged, await db.QuerySingleAsync<long>("SELECT SUM(ChargedTokens) FROM fn_ai_operation_budget WHERE ModelConfigId=@Id", new { Id = model.Id }));
            // 真实退役清理必须锁定并证明后继代次；错误所有者和损坏后继不能删除旧清单。
            var oldTask = Guid.CreateVersion7();
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET Version=2 WHERE Id=@DocumentId", p);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET DocumentVersion=2 WHERE Id=@TaskId", p);
            binding = binding with { DocumentVersion = 2 };
            // 旧任务必须真实早于后继；同一创建时间下更大的 UUID 会成为状态查询的最新候选。
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_task (Id,DocumentId,VersionId,DocumentVersion,KnowledgeBaseVersion,ModelConfigId,ModelConfigVersion,StateKey,AttemptCount,CreatedAtUtc,UpdatedAtUtc) SELECT @OldTask,DocumentId,VersionId,1,KnowledgeBaseVersion,ModelConfigId,ModelConfigVersion,'completed',1,@RetiredCreatedAt,@RetiredCreatedAt FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", new { OldTask = oldTask, TaskId = taskId, RetiredCreatedAt = now.AddDays(-3).UtcDateTime });
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_docx_manifest (Id,FormatKey,PayloadHash,Payload,CreatedAtUtc) SELECT @OldTask,FormatKey,PayloadHash,Payload,CreatedAtUtc FROM fn_ai_knowledge_docx_manifest WHERE Id=@TaskId", new { OldTask = oldTask, TaskId = taskId });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_active_generation SET ActivatedAtUtc=@Time WHERE DocumentId=@DocumentId", new { Time = now.AddDays(-2).UtcDateTime, binding.DocumentId });
            var successorStatus = await ReadStatus(); Assert.IsNotNull(successorStatus);
            Assert.AreEqual(taskId, successorStatus.TaskId); Assert.AreEqual(1, successorStatus.IsCurrent);
            Assert.IsTrue(await Use(a => a.Activations.IsReadyForReadAsync(successorStatus, readParameters, default)));
            await Retire(Guid.CreateVersion7()); Assert.AreEqual(1L, await OldManifestCount());
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Broken WHERE Id=@Id", new { Broken = new byte[8], Id = operations[0] });
            await Retire(binding.OwnerUserId); Assert.AreEqual(1L, await OldManifestCount());
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload WHERE Id=@Id", new { Payload = firstPayload, Id = operations[0] });
            auth.DeniedPermission = "ai.knowledge_documents.index_enqueue";
            await Retire(binding.OwnerUserId); Assert.AreEqual(0L, await OldManifestCount()); auth.DeniedPermission = null;
            Assert.AreEqual(taskId, await db.QuerySingleAsync<Guid>("SELECT IndexTaskId FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", p));
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_docx_manifest WHERE Id=@TaskId", p));
            Task<long> OldManifestCount() => db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_docx_manifest WHERE Id=@Id", new { Id = oldTask });
            Task<bool> Retire(Guid owner) => Use(async a => { await a.Cleanup.CleanupRetiredTaskAsync(binding.KnowledgeBaseId, binding.DocumentId, owner, oldTask, now.AddHours(-24), default); return true; });
            Task<AiKnowledgeIndexStatusRecord?> ReadStatus() => Use(a => a.Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.For(sqlServer ? DatabaseProvider.SqlServer : DatabaseProvider.MySql, scoped), readParameters, default));
            async Task<int> RunWorker(bool enabled)
            {
                await using var root = factory.Services.CreateAsyncScope(); var services = root.ServiceProvider;
                services.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                var coordinator = new AiKnowledgeIndexWorkerCoordinator(services.GetRequiredService<IQueryExecutor>(), services.GetRequiredService<IDataTransactionState>(),
                    services.GetRequiredService<IServiceScopeFactory>(), clock, services.GetRequiredService<IOptions<DatabaseOptions>>(),
                    Options.Create(new AiKnowledgeIndexWorkerOptions { Enabled = true, BatchSize = 1 }), NullLogger<AiKnowledgeIndexWorkerCoordinator>.Instance,
                    docxOptions: Options.Create(new AiKnowledgeDocxRuntimeOptions { Enabled = enabled }));
                return await coordinator.ProcessPendingAsync(default);
            }
            // 活动终态墓碑删除也要去掉指针和完整派生数据，费用审计凭据保持完整。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1,Version=3 WHERE Id=@DocumentId", p);
            await Use(async a => { await a.Cleanup.CleanupDocumentAsync(binding.KnowledgeBaseId, binding.DocumentId, Guid.CreateVersion7(), default); return true; });
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_docx_manifest WHERE Id=@TaskId", p));
            await Use(async a => { await a.Cleanup.CleanupDocumentAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.OwnerUserId, default); return true; });
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_docx_manifest WHERE Id=@TaskId", p));
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_vector WHERE Id IN @Ids", new { Ids = operations }));
            Assert.AreEqual(2L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE Id IN @Ids", new { Ids = operations }));
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", p));
            async Task<T> Use<T>(Func<Access, Task<T>> action, bool expire = false, bool failActivation = false)
            {
                await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
                var writer = services.GetRequiredService<ICurrentTenantContextWriter>();
                if (session.TenantId is { } id) writer.SetTenant(new TenantContext(id, "docx-test", "DOCX测试")); else writer.SetHost();
                var query = services.GetRequiredService<IQueryExecutor>(); var command = services.GetRequiredService<ICommandExecutor>();
                var tenant = services.GetRequiredService<ICurrentTenant>(); var state = services.GetRequiredService<IDataTransactionState>();
                var transaction = services.GetRequiredService<ICommandTransaction>(); var options = services.GetRequiredService<IOptions<DatabaseOptions>>();
                var ids = services.GetRequiredService<IIdGenerator>(); var tasks = new AiKnowledgeIndexTaskStore(query, command, tenant, clock, ids);
                var sessions = new AiKnowledgeIndexSessionService(query, command, transaction, state, tenant, clock, ids, tasks, auth);
                return await action(new(tasks, sessions, new(query, command, transaction, state, tenant, clock, tasks, sessions, auth, options),
                    new(query, expire ? new ExpireAfterInsert(command, clock) : command, transaction, state, tenant, clock, tasks, sessions, auth, options),
                    new(query, command, tasks, ids, options), query, services.GetRequiredService<IAiOperationBudgetStore>(), services.GetRequiredService<AiKnowledgeIndexCleanupStore>(),
                    new(query, failActivation ? new RejectActivationInsert(command) : command, transaction, state, tenant, clock, ids, tasks, auth, options),
                    new(query, command, state, tenant, clock, tasks, auth, docx: Options.Create(new AiKnowledgeDocxRuntimeOptions { Enabled = true }))));
            }
        }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private sealed record Access(AiKnowledgeIndexTaskStore Tasks, AiKnowledgeIndexSessionService Sessions,
        AiKnowledgeIndexDocxManifestStore Manifests, AiKnowledgeIndexDocxVectorStore Vectors, AiKnowledgeIndexBatchStore Batches, IQueryExecutor Query, IAiOperationBudgetStore Budgets, AiKnowledgeIndexCleanupStore Cleanup, AiKnowledgeIndexDocxActivationStore Activations, AiKnowledgeIndexRetryService Retry)
    {
        internal AiKnowledgeIndexDocxPersistenceService Consumer => new(null!, Vectors, Manifests, Sessions, Activations);
    }
    /// <summary>真实完成 UPDATE 已执行后拒绝指针 INSERT，检验外层实际事务回滚。</summary>
    private sealed class RejectActivationInsert(ICommandExecutor inner) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            statement.Name.StartsWith("ai.knowledge_activation.insert_", StringComparison.Ordinal) ? Task.FromResult(0)
                : inner.ExecuteAsync(statement, parameters, cancellationToken);
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
            if (statement.Name.StartsWith("ai.knowledge_index_docx_vector.insert", StringComparison.Ordinal) && count == 1) clock.UtcNow = clock.UtcNow.AddSeconds(30);
            return count;
        }
    }
}
