using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库人工完整代次与归档；外部授权、Files 和模型为明确替身，不冒充发布、检索或 Native。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionGenerationTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Full_human_generations_are_atomic_replayable_and_survive_new_drafts_without_rebilling(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync();
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        foreach (var scoped in new[] { false, true })
        {
            var seed = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            var binding = new AiKnowledgeIndexTaskBinding((Guid)seed["KnowledgeBaseId"]!, (Guid)seed["DocumentId"]!, (Guid)seed["VersionId"]!, 1, 1, (Guid)seed["OwnerUserId"]!);
            var session = new SessionBindingSnapshot(binding.OwnerUserId, (Guid?)seed["TenantId"], Guid.CreateVersion7(), "generation-stamp", scoped ? "tenant" : "host", scoped ? "tenant" : "host");
            const string originalText = "原始文件文字"; var bytes = Encoding.UTF8.GetBytes(originalText); var hash = Digest(bytes);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size WHERE Id=@Id; UPDATE fn_ai_knowledge_import SET ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@Id", new { Hash = hash, Size = bytes.Length, Id = binding.VersionId });
            await using var scope = factory.Services.CreateAsyncScope(); var sp = scope.ServiceProvider; var writer = sp.GetRequiredService<ICurrentTenantContextWriter>();
            if (session.TenantId is { } id) writer.SetTenant(new(id, "generation", "人工代次")); else writer.SetHost();
            var tenant = sp.GetRequiredService<ICurrentTenant>(); var state = sp.GetRequiredService<IDataTransactionState>();
            var query = sp.GetRequiredService<IQueryExecutor>(); var command = sp.GetRequiredService<ICommandExecutor>(); var transaction = sp.GetRequiredService<ICommandTransaction>();
            var clock = sp.GetRequiredService<IClock>(); var ids = sp.GetRequiredService<IIdGenerator>(); var options = sp.GetRequiredService<IOptions<DatabaseOptions>>();
            Assert.IsNotNull(sp.GetRequiredService<AiKnowledgeCorrectionActivationStore>(), "API 读取证明不应依赖 Worker 的模型派发或解析服务。");
            var ports = new AiKnowledgeCorrectionManifestTests.Ports(state, tenant, session, (Guid)seed["FileId"]!, binding.VersionId, bytes.Length, hash);
            var tasks = new AiKnowledgeIndexTaskStore(query, command, tenant, clock, ids);
            var sessions = new AiKnowledgeIndexSessionService(query, command, transaction, state, tenant, clock, ids, tasks, ports);
            var filePorts = new RevokingFiles(ports);
            var sourceAuthority = new AiKnowledgeCorrectionAuthority(query, state, tenant, ports, filePorts, filePorts); var store = sp.GetRequiredService<AiKnowledgeCorrectionStore>();
            var planning = new AiKnowledgeCorrectionIndexService(query, command, transaction, state, tenant, clock, options, tasks, sessions, ports, sourceAuthority, store);
            var budget = sp.GetRequiredService<IAiOperationBudgetStore>(); var batches = new AiKnowledgeIndexBatchStore(query, command, tasks, ids, options);
            var admission = new AiKnowledgeCorrectionBudgetService(planning, batches, budget, state);
            var model = new AiKnowledgeCorrectionVectorTests.Model(state);
            var execution = new AiKnowledgeCorrectionEmbeddingService(planning, admission, budget, state, [model], sp.GetRequiredService<AiModelBindingScope>());
            var vectors = new AiKnowledgeCorrectionVectorStore(query, command, transaction, state, tenant, clock, options, tasks, planning);
            var failure = new ArchiveFailure(command, state);
            var activation = new AiKnowledgeCorrectionActivationStore(query, failure, transaction, state, tenant, clock, ids, options, (tasks).Scope, sourceAuthority, ports);
            var generation = new AiKnowledgeCorrectionPersistenceService(execution, vectors, planning, sessions, activation);
            var correction = new AiKnowledgeCorrectionStore.Binding(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, binding.OwnerUserId, 1);
            var parsed = AiKnowledgeParsedDocument.FromText(new(originalText, hash, bytes.Length), AiKnowledgeTextFormat.PlainText, default)!;
            var source = await store.CreateSourceAsync(correction, Guid.CreateVersion7(), Digest("source"u8.ToArray()), parsed, "managed:utf8-text-v1", default); Assert.IsTrue(source.IsSuccess);
            var draft = (await store.ReadCurrentAsync(correction, parsed.Format, hash, bytes.Length, parsed.Policy, default))!.Document; var first = draft.Blocks[0];
            draft = draft.Replace(first.Id, first.Parts[0].Id, "人工校对的第一版", default);
            draft = draft.Append(first.Id, "同片段人工补写", Guid.CreateVersion7, default);
            draft = draft.Merge(first.Id, draft.Blocks[1].Id, default);
            for (var part = 0; part < 17; part++) draft = draft.Append(null, $"明确补写的第 {part} 项", Guid.CreateVersion7, default);
            var saved = await store.SaveAsync(correction, source.Value!.Id, 0, Guid.CreateVersion7(), Digest("first"u8.ToArray()), "完整代次校对", draft, source.Value.ParserRuntimeId, default); Assert.IsTrue(saved.IsSuccess);
            binding = binding with { DocumentVersion = 2 }; correction = correction with { DocumentVersion = 2 };
            var task = await sessions.EnqueueAsync(binding, session, default); Assert.IsNotNull(task);
            var held = await tasks.AcquireAsync(binding, task.Value, default, 180); Assert.IsNotNull(held?.LeaseToken); var lease = held.LeaseToken.Value;
            Assert.IsFalse(await activation.ActivateAsync(binding, task.Value, lease, planning, vectors, default)); Assert.AreEqual(0, model.Calls);
            failure.Enabled = true;
            Assert.IsFalse(await generation.ExecuteGenerationAsync(binding, task.Value, lease, default));
            Assert.AreEqual(2, model.Calls); Assert.AreEqual("running", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = task.Value }));
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_generation WHERE Id=@Id", new { Id = task.Value }));
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE DocumentId=@Id", new { Id = binding.DocumentId }));
            var prepared = await planning.ReadAsync(binding, task.Value, lease, default); Assert.IsNotNull(prepared); Assert.AreEqual(2, prepared.Plan.Batches.Count);
            var extra = Guid.CreateVersion7(); await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_batch (Id,IndexTaskId,BatchOrdinal,RequestHash,InputCount,CreatedAtUtc) VALUES (@Id,@TaskId,2,@Hash,1,@Now)", new { Id = extra, TaskId = task.Value, Hash = new string('A', 64), Now = DateTime.UtcNow });
            Assert.IsNull(await vectors.ReadWholePlanProofAsync(binding, task.Value, lease, default)); Assert.IsFalse(await activation.ActivateAsync(binding, task.Value, lease, planning, vectors, default));
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_batch WHERE Id=@Id", new { Id = extra }); failure.Enabled = false;
            Assert.IsTrue(await generation.ExecuteGenerationAsync(binding, task.Value, lease, default)); Assert.IsTrue(await generation.ExecuteGenerationAsync(binding, task.Value, lease, default));
            Assert.AreEqual(2, model.Calls); Assert.AreEqual("completed", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = task.Value }));
            Assert.IsTrue(await activation.IsActiveAsync(binding, task.Value, lease, default));
            var pointer = await db.QuerySingleAsync<AiKnowledgeIndexActivationRecord>("SELECT Id,DocumentId,Id AS IndexTaskId,VersionId,PlanHash,ManifestHash,ReceiptHash,ReceiptJson,LeaseToken,ActivatedAtUtc FROM fn_ai_knowledge_generation WHERE Id=@Id", new { Id = task.Value });
            var receipts = JsonSerializer.Deserialize(pointer.ReceiptJson, AiKnowledgeIndexActivationJsonContext.Default.AiKnowledgeIndexVectorReceiptRecordArray)!;
            var firstReceipt = receipts[0]; receipts[0] = firstReceipt with { RequestHash = new string('F', 64) };
            var altered = JsonSerializer.Serialize(receipts, AiKnowledgeIndexActivationJsonContext.Default.AiKnowledgeIndexVectorReceiptRecordArray);
            await Receipt(altered, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(altered))), receipts[0].RequestHash);
            Assert.IsFalse(await activation.IsActiveAsync(binding, task.Value, lease, default), "自洽的账本/回执元数据不能替代真实人工输入指纹。");
            await Receipt(pointer.ReceiptJson, pointer.ReceiptHash, firstReceipt.RequestHash);
            Assert.IsTrue(await activation.IsActiveAsync(binding, task.Value, lease, default));
            ports.Ready = false; Assert.IsFalse(await activation.IsActiveAsync(binding, task.Value, lease, default)); ports.Ready = true;
            ports.Denied = AiKnowledgeCorrectionPermissions.Read; Assert.IsFalse(await activation.IsActiveAsync(binding, task.Value, lease, default)); ports.Denied = null;
            var fileReads = 0;
            filePorts.AfterRead = () => { if (++fileReads == 2) ports.Denied = AiKnowledgeCorrectionPermissions.Read; };
            Assert.IsFalse(await activation.IsActiveAsync(binding, task.Value, lease, default), "末次 Files 等待期间撤权仍须拒绝，不能复用等待前的身份结果。");
            Assert.AreEqual(2, fileReads); filePorts.AfterRead = null; ports.Denied = null;
            ports.Session = session with { SessionId = Guid.CreateVersion7() }; Assert.IsFalse(await activation.IsActiveAsync(binding, task.Value, lease, default)); ports.Session = session;
            using (var cancelled = new CancellationTokenSource())
            { cancelled.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => activation.IsActiveAsync(binding, task.Value, lease, cancelled.Token)); }
            var publicationFiles = new AiKnowledgeIndexEmbeddingAssertions.ControlledFiles((Guid)seed["FileId"]!, binding.VersionId, bytes, hash);
            var publication = new AiKnowledgePublicationTests.Fixture(sp, publicationFiles, session, activation);
            var statuses = new AiKnowledgeIndexStatusService(query, state, tenant, publication.Access, publication.Permission,
                sp.GetRequiredService<AiKnowledgeIndexActivationStore>(), options, correctionActivation: activation);
            var readyStatus = await statuses.ReadAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, session.UserId, default);
            Assert.IsTrue(readyStatus.IsSuccess, readyStatus.Error?.Code); Assert.AreEqual("ready", readyStatus.Value!.State);
            var reviewer = session with { UserId = Guid.CreateVersion7(), SessionId = Guid.CreateVersion7() };
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_member (Id,KnowledgeBaseId,UserId,CreatedAtUtc) VALUES (@Id,@Base,@Actor,@Now); INSERT INTO fn_ai_knowledge_document_member (Id,DocumentId,UserId,CreatedAtUtc) VALUES (@Member,@Doc,@Actor,@Now)",
                new { Id = Guid.CreateVersion7(), Member = Guid.CreateVersion7(), Base = binding.KnowledgeBaseId, Doc = binding.DocumentId, Actor = reviewer.UserId, Now = DateTimeOffset.UtcNow });
            var request = new RequestAiKnowledgePublication(Guid.CreateVersion7(), binding.VersionId, 0, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            var requested = await publication.Publications.RequestAsync(binding.KnowledgeBaseId, binding.DocumentId, request, session, default);
            Assert.IsTrue(requested.IsSuccess, requested.Error?.Code); var proposal = requested.Value!;
            Assert.IsFalse((await publication.Publications.ReviewAsync(binding.KnowledgeBaseId, binding.DocumentId, proposal.Id, new(true, proposal.Version, proposal.SnapshotHash), session, default)).IsSuccess);
            publication.Identity.Current = reviewer;
            var reviewed = await publication.Publications.ReviewAsync(binding.KnowledgeBaseId, binding.DocumentId, proposal.Id, new(true, proposal.Version, proposal.SnapshotHash), reviewer, default);
            Assert.IsTrue(reviewed.IsSuccess, reviewed.Error?.Code); proposal = reviewed.Value!; publication.Identity.Current = session;
            var published = await publication.Publications.PublishAsync(binding.KnowledgeBaseId, binding.DocumentId, proposal.Id, new(proposal.Version, proposal.SnapshotHash), session, default);
            Assert.IsTrue(published.IsSuccess, published.Error?.Code);
            var readParameters = await publication.Access.ParametersAsync(reviewer.UserId, default);
            readParameters["KnowledgeBaseId"] = binding.KnowledgeBaseId; readParameters["DocumentId"] = binding.DocumentId;
            readParameters["VersionId"] = binding.VersionId; readParameters["TaskId"] = task.Value; readParameters["PublicationId"] = proposal.Id;
            var publishedSource = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgePublishedSourceSql.ForGeneration(provider, scoped), readParameters, default);
            Assert.IsNotNull(publishedSource); Assert.AreEqual(saved.Value!.Id, publishedSource.CorrectionRevisionId);
            var modelRecord = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(scoped ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
                new Dictionary<string, object?> { ["ModelConfigId"] = seed["ModelConfigId"] }, default); Assert.IsNotNull(modelRecord);
            publication.Identity.Current = reviewer; ports.Session = session with { SessionId = Guid.CreateVersion7() };
            var probe = new float[1536]; probe[0] = 1; var pipeline = new AiKnowledgeAnswerPipeline(publication.Retrieval, ids);
            var preparedAnswer = await pipeline.PrepareAsync([binding.KnowledgeBaseId], reviewer.UserId, modelRecord, probe, "说明人工校对内容", default);
            Assert.IsTrue(preparedAnswer.Context.Citations[0].Chunk.Correction is { Spans.Count: 2 });
            Assert.IsTrue(preparedAnswer.Retrieval.Chunks.All(c => c.Chunk.Correction is not null && c.Chunk.PageNumber is null && c.Chunk.SourceLocation is null));
            var firstCitation = preparedAnswer.Context.Citations[0]; Assert.AreEqual("corrected", firstCitation.Chunk.Correction!.Spans[0].ContentKind);
            Assert.AreEqual(originalText.Length, firstCitation.Chunk.Correction.Spans[0].SourceLength);
            Assert.AreEqual("supplement", firstCitation.Chunk.Correction.Spans[1].ContentKind); Assert.IsNull(firstCitation.Chunk.Correction.Spans[1].SourceLocation);
            var response = Encoding.UTF8.GetBytes($$"""{"answer":"已核对并保留人工补写性质","citationIds":["{{firstCitation.Id}}"],"refusal":false}""");
            var acceptedAnswer = await pipeline.AcceptAsync(preparedAnswer, response, default);
            var historyGuard = new AiKnowledgeAnswerHistoryGuard(query, state, tenant, options, publication.Access, publication.Permission, publication.Proof);
            var storedAnswer = await AiKnowledgeAnswerStoreAssertions.VerifyAsync(db, query, command, transaction, tenant, clock, options, historyGuard, preparedAnswer, acceptedAnswer);
            var historyQuery = new AiChatSessionQueryService(query, tenant, options, clock, historyGuard);
            var history = await historyQuery.GetByIdAsync(storedAnswer.SessionId, reviewer.UserId); Assert.IsTrue(history.IsSuccess);
            var returned = history.Value!.Messages.Single(m => m.Id == storedAnswer.MessageId).Citations.Single();
            Assert.IsNull(returned.Offset); Assert.IsNull(returned.Length); Assert.IsNotNull(returned.Correction);
            Assert.AreEqual(2, returned.Correction.Spans.Count); Assert.AreEqual(saved.Value.Id, returned.Correction.RevisionId);
            var storedJson = await db.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@Id", new { Id = storedAnswer.MessageId });
            var metadata = AiKnowledgeAnswerLineageCodec.Decode(storedJson, storedAnswer.MessageId, storedAnswer.SessionId, reviewer.UserId, session.TenantId);
            Assert.AreEqual(4, metadata.Version);
            // 合法结构与真实摘要仍不能为伪造坐标背书；同一批历史中的有效答案不受另一条坏引用影响。
            var badReference = metadata.References[0] with { Correction = metadata.References[0].Correction! with { Spans = metadata.References[0].Correction!.Spans.Select((s, i) => i == 0 ? s with { SourceOffset = s.SourceOffset + 1 } : s).ToArray() } };
            var alteredJson = AiKnowledgeAnswerLineageCodec.Encode(metadata with { References = [badReference] });
            Assert.IsNull(await historyGuard.AuthorizeAsync(alteredJson, storedAnswer.MessageId, storedAnswer.SessionId, reviewer.UserId, false, default));
            var badMessage = Guid.CreateVersion7(); var badLineage = metadata with { MessageId = badMessage, References = [badReference] };
            var batchProof = await historyGuard.AuthorizeBatchAsync([new(storedAnswer.MessageId, storedAnswer.SessionId, storedJson), new(badMessage, storedAnswer.SessionId, AiKnowledgeAnswerLineageCodec.Encode(badLineage))], reviewer.UserId, default);
            Assert.IsTrue(batchProof.ContainsKey(storedAnswer.MessageId)); Assert.IsFalse(batchProof.ContainsKey(badMessage));
            Assert.AreEqual(2, model.Calls, "召回、上下文和历史恢复不调用索引模型。");
            publication.Identity.Current = session; ports.Session = session;
            ports.Session = session with { SessionId = Guid.CreateVersion7() };
            Assert.IsFalse(await activation.IsActiveAsync(binding, task.Value, lease, default));
            Assert.IsTrue(await publication.Proof.VerifyAsync(publishedSource, readParameters, default), "普通读者不依赖原索引写入人在线。"); ports.Session = session;
            ports.Denied = AiKnowledgeCorrectionPermissions.Read;
            Assert.IsTrue(await publication.Proof.VerifyAsync(publishedSource, readParameters, default), "普通读权独立于写入人的校对维护权限。"); ports.Denied = null;
            ports.Ready = false; Assert.IsFalse(await publication.Proof.VerifyAsync(publishedSource, readParameters, default));
            Assert.AreEqual("failed", (await statuses.ReadAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, session.UserId, default)).Value!.State); ports.Ready = true;
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_document_member WHERE DocumentId=@Doc AND UserId=@Actor", new { Doc = binding.DocumentId, Actor = reviewer.UserId });
            Assert.IsFalse(await publication.Proof.VerifyAsync(publishedSource, readParameters, default));
            publication.Identity.Current = reviewer;
            await Assert.ThrowsAsync<InvalidDataException>(() => publication.Retrieval.RevalidateAsync(preparedAnswer.Retrieval, default));
            var deniedHistory = await historyQuery.GetByIdAsync(storedAnswer.SessionId, reviewer.UserId); Assert.IsTrue(deniedHistory.IsSuccess);
            Assert.AreEqual("", deniedHistory.Value!.Messages.Single(m => m.Id == storedAnswer.MessageId).Content); publication.Identity.Current = session;
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_document_member (Id,DocumentId,UserId,CreatedAtUtc) VALUES (@Id,@Doc,@Actor,@Now)", new { Id = Guid.CreateVersion7(), Doc = binding.DocumentId, Actor = reviewer.UserId, Now = DateTimeOffset.UtcNow });
            Assert.IsTrue(await publication.Proof.VerifyAsync(publishedSource, readParameters, default));
            var originalBinding = binding;
            for (var head = 2; head <= 3; head++)
            {
                draft = draft.Replace(first.Id, first.Parts[0].Id, $"人工校对的第 {head} 版", default);
                var next = await store.SaveAsync(correction, source.Value.Id, head - 1, Guid.CreateVersion7(), Digest(Encoding.UTF8.GetBytes($"head-{head}")), "新草稿不覆盖旧完整代次", draft, source.Value.ParserRuntimeId, default); Assert.IsTrue(next.IsSuccess);
                binding = binding with { DocumentVersion = head + 1 }; correction = correction with { DocumentVersion = head + 1 };
                Assert.IsTrue(await activation.IsActiveAsync(originalBinding, task.Value, lease, default));
                var newTask = await sessions.EnqueueAsync(binding, session, default); Assert.IsNotNull(newTask);
                var acquired = await tasks.AcquireAsync(binding, newTask.Value, default, 180); Assert.IsNotNull(acquired?.LeaseToken);
                Assert.IsTrue(await generation.ExecuteGenerationAsync(binding, newTask.Value, acquired.LeaseToken.Value, default));
                Assert.AreEqual(newTask.Value, await db.QuerySingleAsync<Guid>("SELECT IndexTaskId FROM fn_ai_knowledge_active_generation WHERE DocumentId=@Id", new { Id = binding.DocumentId }));
                Assert.AreEqual(head, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_generation WHERE DocumentId=@Id", new { Id = binding.DocumentId }));
                Assert.IsTrue(await publication.Proof.VerifyAsync(publishedSource, readParameters, default), "新人工草稿及候选不能改变旧有效发布。");
                publication.Identity.Current = reviewer; await publication.Retrieval.RevalidateAsync(preparedAnswer.Retrieval, default); publication.Identity.Current = session;
            }
            Assert.AreEqual(6, model.Calls);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_revision SET CleanupState=2,Payload=@Payload WHERE Id=@Id", new { Payload = "{}"u8.ToArray(), Id = saved.Value!.Id });
            Assert.IsTrue(await activation.IsActiveAsync(originalBinding, task.Value, lease, default), "旧归档从完整清单独立恢复，不追随新头或读取已清理旧修订正文。");
            Assert.IsTrue(await publication.Proof.VerifyAsync(publishedSource, readParameters, default));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_publication SET EffectiveToUtc=@Expires WHERE Id=@Id", new { Expires = DateTimeOffset.UtcNow.AddSeconds(-1), Id = proposal.Id });
            Assert.IsFalse(await publication.Proof.VerifyAsync(publishedSource, readParameters, default));
            publication.Identity.Current = reviewer;
            Assert.IsNull(await historyGuard.AuthorizeAsync(storedJson, storedAnswer.MessageId, storedAnswer.SessionId, reviewer.UserId, false, default)); publication.Identity.Current = session;
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_publication SET EffectiveToUtc=@Expires WHERE Id=@Id", new { Expires = request.EffectiveToUtc, Id = proposal.Id });
            Assert.IsTrue(await publication.Proof.VerifyAsync(publishedSource, readParameters, default));
            Assert.IsTrue((await publication.Publications.WithdrawAsync(binding.KnowledgeBaseId, binding.DocumentId, new(1, proposal.Id), session, default)).IsSuccess);
            Assert.IsFalse(await publication.Proof.VerifyAsync(publishedSource, readParameters, default));
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_read_lease WHERE GenerationId=@Id", new { Id = task.Value }));
            Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_publication WHERE DocumentId=@Id", new { Id = binding.DocumentId }));
            Assert.AreEqual(6, model.Calls);
            Task Receipt(string json, string receiptHash, string requestHash) => db.ExecuteAsync("UPDATE fn_ai_knowledge_index_batch SET RequestHash=@RequestHash WHERE Id=@OperationId; UPDATE fn_ai_operation_budget SET RequestHash=@RequestHash WHERE Id=@OperationId; UPDATE fn_ai_knowledge_generation SET ReceiptJson=@Json,ReceiptHash=@Hash WHERE Id=@TaskId; UPDATE fn_ai_knowledge_active_generation SET ReceiptJson=@Json,ReceiptHash=@Hash WHERE IndexTaskId=@TaskId", new { RequestHash = requestHash, OperationId = firstReceipt.Id, Json = json, Hash = receiptHash, TaskId = task.Value });
        }
    }
    /// <summary>在真实 AI 事务外的 Files 等待结束时撤权，模拟最后一次外部等待的竞争窗口。</summary>
    private sealed class RevokingFiles(AiKnowledgeCorrectionManifestTests.Ports inner) : IHostFileReferenceReader, ITenantResourceFileDescriptorReader
    {
        internal Action? AfterRead;
        public async Task<HostFileReference?> GetReadyReferenceAsync(Guid fileId, CancellationToken cancellationToken = default)
        { var result = await inner.GetReadyReferenceAsync(fileId, cancellationToken); AfterRead?.Invoke(); return result; }
        public async Task<TenantResourceFileDescriptor?> GetReadyDescriptorAsync(string module, Guid resource, Guid fileId, CancellationToken cancellationToken = default)
        { var result = await inner.GetReadyDescriptorAsync(module, resource, fileId, cancellationToken); AfterRead?.Invoke(); return result; }
    }
    private sealed class ArchiveFailure(ICommandExecutor inner, IDataTransactionState state) : ICommandExecutor
    {
        internal bool Enabled;
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (Enabled && statement.Name.StartsWith("ai.knowledge_publication.archive_", StringComparison.Ordinal))
            { Assert.IsTrue(state.HasTransaction); return Task.FromResult(0); }
            return inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
