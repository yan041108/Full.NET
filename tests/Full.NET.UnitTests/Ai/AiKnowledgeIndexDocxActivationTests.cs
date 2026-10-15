using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>消费者提交顺序、原授权和等待后拒绝；真实数据库锁由双库测试证明。</summary>
[TestClass]
public sealed partial class AiKnowledgeIndexDocxActivationTests
{
    [TestMethod] [DataRow("ai.knowledge_documents.index_read")] [DataRow("ai.knowledge_documents.index_rebuild")]
    public async Task Rebuild_activation_stops_after_additional_permission_revocation(string permission)
    {
        var f = new Fixture(false); f.SessionRow = f.SessionRow with { Generation = 1 }; f.DeniedPermission = permission;
        Assert.IsFalse(await f.Activate()); Assert.AreEqual(0, f.Writes);
        f.DeniedPermission = null; Assert.IsTrue(await f.Activate());
    }

    [TestMethod]
    public async Task Index_permission_revocation_stops_activation()
    {
        var f = new Fixture(false) { DeniedPermission = "ai.knowledge_documents.index_enqueue" };
        Assert.IsFalse(await f.Activate()); Assert.AreEqual(0, f.Writes);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Completion_and_pointer_commit_together_and_original_retry_does_not_write(bool tenant)
    {
        var f = new Fixture(tenant); Assert.IsTrue(await f.Activate()); Assert.IsTrue(f.Completed); Assert.IsNotNull(f.Pointer);
        Assert.AreEqual(2, f.Writes); var pointer = f.Pointer;
        f.Clock.UtcNow.Returns(f.Now.AddMinutes(1)); Assert.IsTrue(await f.Store.IsActiveAsync(f.Binding, f.TaskId, f.Lease, default));
        Assert.AreEqual(pointer, f.Pointer); Assert.AreEqual(2, f.Writes); Assert.IsTrue(f.Authorizations >= 8);
    }

    [TestMethod] [DataRow("source")] [DataRow("session")] [DataRow("manifest")]
    [DataRow("batch_count")] [DataRow("receipt")]
    public async Task Changed_locked_rows_refuse_before_completion(string fault)
    {
        var f = new Fixture(false); f.AtAuthority = () =>
        {
            if (fault == "source") f.SourceDenied = true;
            else if (fault == "session") f.SessionRow = f.SessionRow with { SessionId = Guid.CreateVersion7() };
            else if (fault == "manifest") f.ManifestRow = f.ManifestRow with { Payload = new byte[8] };
            else if (fault == "batch_count") f.Batches = [];
            else f.Receipts = [f.Receipts[0] with { PayloadHash = new string('B', 64) }];
        };
        Assert.IsFalse(await f.Activate()); Assert.IsFalse(f.Completed); Assert.IsNull(f.Pointer); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("zero")] [DataRow("expiry")] [DataRow("cancellation")] [DataRow("database")]
    public async Task Failure_after_completion_rolls_back_pointer_and_task(string fault)
    {
        var f = new Fixture(false); using var cancel = new CancellationTokenSource();
        f.AtInsert = () =>
        {
            if (fault == "zero") f.InsertCount = 0;
            else if (fault == "expiry") f.Clock.UtcNow.Returns(f.Now.AddSeconds(30));
            else if (fault == "cancellation") cancel.Cancel();
            else throw new IOException("database unavailable");
        };
        if (fault == "database") await Assert.ThrowsExactlyAsync<IOException>(() => f.Activate(cancel.Token));
        else if (fault == "cancellation") await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Activate(cancel.Token));
        else Assert.IsFalse(await f.Activate(cancel.Token));
        Assert.IsFalse(f.Completed); Assert.IsNull(f.Pointer); Assert.AreEqual(1, f.Rollbacks);
    }

    [TestMethod] [DataRow("permission")] [DataRow("ambient")] [DataRow("cancellation")]
    public async Task Initial_authority_failure_never_opens_transaction(string fault)
    {
        var f = new Fixture(false); using var cancel = new CancellationTokenSource();
        if (fault == "permission") f.Denied = true;
        else if (fault == "ambient") f.Transactions.HasTransaction.Returns(true);
        else cancel.Cancel();
        if (fault == "ambient") await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Activate(cancel.Token));
        else if (fault == "cancellation") await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Activate(cancel.Token));
        else Assert.IsFalse(await f.Activate(cancel.Token));
        Assert.AreEqual(0, f.Begins); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("permission")] [DataRow("json_hash")] [DataRow("json_shape")]
    [DataRow("receipt")] [DataRow("manifest")] [DataRow("json_property_null")]
    public async Task Completed_retry_rechecks_original_authority_and_durable_proof(string fault)
    {
        var f = new Fixture(false); Assert.IsTrue(await f.Activate()); Assert.IsNotNull(f.Pointer);
        if (fault == "permission") f.Denied = true;
        else if (fault == "json_hash") f.Pointer = f.Pointer with { ReceiptHash = new string('B', 64) };
        else if (fault == "json_shape") f.Pointer = f.Pointer with { ReceiptJson = "[]", ReceiptHash = Convert.ToHexString(SHA256.HashData("[]"u8)) };
        else if (fault == "json_property_null")
        {
            var json = f.Pointer.ReceiptJson.Replace('"' + f.Receipts[0].RequestHash + '"', "null", StringComparison.Ordinal);
            f.Pointer = f.Pointer with { ReceiptJson = json, ReceiptHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))) };
        }
        else if (fault == "manifest") f.ManifestRow = f.ManifestRow with { Payload = new byte[8] };
        else f.Receipts = [f.Receipts[0] with { Id = Guid.CreateVersion7() }];
        Assert.IsFalse(await f.Store.IsActiveAsync(f.Binding, f.TaskId, f.Lease, default)); Assert.AreEqual(2, f.Writes);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Completed_consumer_replays_without_running_lease_or_embedding(bool tenant)
    {
        var f = new Fixture(tenant); Assert.IsTrue(await f.Activate());
        f.Clock.UtcNow.Returns(f.Now.AddHours(1));
        Assert.IsTrue(await f.Consumer.ExecuteGenerationAsync(f.Binding, f.TaskId, f.Lease, default));
        Assert.IsTrue(await f.Consumer.ActivateGenerationAsync(f.Binding, f.TaskId, f.Lease, default));
        Assert.AreEqual(2, f.Writes);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Authorization_revoked_during_payload_wait_refuses_completed_recovery(bool tenant)
    {
        var f = new Fixture(tenant); Assert.IsTrue(await f.Activate()); var pointer = f.Pointer;
        f.AtPayload = () => f.Denied = true;
        Assert.IsFalse(await f.Store.IsActiveAsync(f.Binding, f.TaskId, f.Lease, default));
        Assert.AreEqual(pointer, f.Pointer); Assert.AreEqual(2, f.Writes);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Actual_vector_bytes_are_verified_before_activation_and_completed_recovery(bool completed)
    {
        var f = new Fixture(false); if (completed) Assert.IsTrue(await f.Activate());
        var row = f.Payloads[0]; f.Payloads[0] = row with { Payload = new byte[8] };
        Assert.IsFalse(completed ? await f.Store.IsActiveAsync(f.Binding, f.TaskId, f.Lease, default) : await f.Activate());
        Assert.AreEqual(completed ? 2 : 0, f.Writes);
    }

    [TestMethod]
    public async Task Noncooperative_authorization_cancellation_refuses_without_opening_transaction()
    {
        var f = new Fixture(false); using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeAuthorization = _ => { entered.TrySetResult(); return release.Task; };
        var pending = f.Activate(cancel.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancel.Cancel();
            await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromSeconds(7)));
            Assert.AreEqual(0, f.Begins); Assert.AreEqual(0, f.Writes);
        }
        finally { release.TrySetResult(); try { await pending; } catch (IOException) { } }
    }

    [TestMethod]
    public async Task Cancellation_inside_local_transaction_waits_for_its_query_and_rolls_back()
    {
        var f = new Fixture(false); Assert.IsTrue(await f.Activate()); using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AiKnowledgeIndexVectorRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { Assert.IsTrue(f.Transactions.HasTransaction); entered.TrySetResult(); return release.Task; });
        var pending = f.Store.IsActiveAsync(f.Binding, f.TaskId, f.Lease, cancel.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancel.Cancel();
            Assert.IsFalse(pending.IsCompleted, "本地事务必须由原调用者等待回滚，不能超时后留在后台");
            release.TrySetResult(f.Payloads[0]);
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            Assert.AreEqual(1, f.Rollbacks); Assert.AreEqual(2, f.Writes);
        }
        finally { release.TrySetResult(f.Payloads[0]); try { await pending; } catch (OperationCanceledException) { } }
    }

    [TestMethod] [DataRow("endpoint")] [DataRow("organization")] [DataRow("hash")]
    [DataRow("size")] [DataRow("classification")] [DataRow("pointer")] [DataRow("session-case")]
    public async Task Final_authorization_wait_cannot_hide_ai_source_pointer_or_original_binding_changes(string fault)
    {
        var f = new Fixture(false); Assert.IsTrue(await f.Activate()); var begins = f.Begins;
        f.BeforeAuthorization = _ =>
        {
            if (f.Begins <= begins) return Task.CompletedTask;
            if (fault == "pointer") f.Pointer = f.Pointer! with { IndexTaskId = Guid.CreateVersion7() };
            else if (fault == "session-case") f.SessionRow = f.SessionRow with { SecurityStamp = "STAMP" };
            else
            {
                var old = f.Source;
                f.Source = new() { TaskId = old.TaskId, DocumentId = old.DocumentId, VersionId = old.VersionId,
                    DocumentVersion = old.DocumentVersion, KnowledgeBaseVersion = old.KnowledgeBaseVersion,
                    ModelConfigId = old.ModelConfigId, ModelConfigVersion = old.ModelConfigVersion,
                    LeaseToken = old.LeaseToken, LeaseExpiresAtUtc = old.LeaseExpiresAtUtc, FileId = old.FileId,
                    ContentHash = fault == "hash" ? new string('B', 64) : old.ContentHash, ContentType = old.ContentType,
                    SizeBytes = fault == "size" ? old.SizeBytes + 1 : old.SizeBytes,
                    DataClassification = fault == "classification" ? "restricted" : old.DataClassification,
                    ModelProviderKey = old.ModelProviderKey, ModelId = old.ModelId,
                    ModelEndpointBaseUrl = fault == "endpoint" ? "https://other.example/v1" : old.ModelEndpointBaseUrl,
                    ModelOrganizationId = fault == "organization" ? "other" : old.ModelOrganizationId };
            }
            return Task.CompletedTask;
        };
        Assert.IsFalse(await f.Store.IsActiveAsync(f.Binding, f.TaskId, f.Lease, default)); Assert.AreEqual(2, f.Writes);
    }

    private sealed class Fixture
    {
        internal readonly Guid TaskId = Guid.CreateVersion7(), Lease = Guid.CreateVersion7();
        internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal readonly IClock Clock = Substitute.For<IClock>();
        internal readonly IDataTransactionState Transactions = Substitute.For<IDataTransactionState>();
        internal readonly AiKnowledgeIndexTaskBinding Binding;
        internal readonly AiKnowledgeIndexDocxGenerationProof Proof;
        internal readonly AiKnowledgeIndexDocxManifestSnapshot Manifest;
        internal readonly AiKnowledgeIndexDocxActivationStore Store;
        internal AiKnowledgeIndexSessionRecord SessionRow;
        internal AiKnowledgeIndexTextManifestRecord ManifestRow;
        internal AiKnowledgeIndexVectorReceiptRecord[] Receipts;
        internal AiKnowledgeIndexBatchRecord[] Batches;
        internal AiKnowledgeIndexVectorRecord[] Payloads;
        internal AiKnowledgeIndexActivationRecord? Pointer;
        internal IQueryExecutor Query = null!;
        internal AiKnowledgeIndexTaskStore Tasks = null!;
        internal CurrentTenantAccessor Tenant = null!;
        internal AiKnowledgeIndexDispatchRecord Source;
        internal AiKnowledgeIndexDocxPersistenceService Consumer = null!;
        internal Action? AtPayload = null;
        internal int PayloadReads;
        internal bool Completed, Denied, SourceDenied;
        internal string? DeniedPermission;
        internal int Writes, Begins, Rollbacks, Authorizations, InsertCount = 1;
        internal Action? AtAuthority, AtInsert;
        internal Func<CancellationToken, Task>? BeforeAuthorization = null;

        internal Fixture(bool scoped)
        {
            var tenant = new CurrentTenantAccessor(); Tenant = tenant; Guid? tenantId = scoped ? Guid.CreateVersion7() : null;
            if (tenantId is { } id) tenant.SetTenant(new(id, "test", "测试")); else tenant.SetHost();
            var chunks = AiKnowledgeIndexDocxCodecTests.Plan(17).Source with { KnowledgeBaseVersion = 1, ModelConfigVersion = 1 } with { TenantId = tenantId };
            Binding = new(chunks.KnowledgeBaseId, Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
            var plan = AiKnowledgeIndexDocxPlan.Create(chunks, default)!; var bytes = AiKnowledgeIndexDocxManifestCodec.Encode(plan, default);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            Manifest = new(AiKnowledgeIndexDocxManifestCodec.Decode(bytes, default), hash);
            ManifestRow = new() { Id = TaskId, Payload = bytes, PayloadHash = hash, FormatKey = AiKnowledgeIndexDocxManifestCodec.FormatKey };
            var session = new SessionBindingSnapshot(Binding.OwnerUserId, tenantId, Guid.CreateVersion7(), "stamp", scoped ? "tenant" : "host", scoped ? "tenant" : "host");
            SessionRow = new() { Id = Guid.CreateVersion7(), IndexTaskId = TaskId, ActorUserId = session.UserId, TenantId = tenantId,
                SessionId = session.SessionId, SessionKind = session.SessionKind, SecurityStamp = session.SecurityStamp, ActorScope = session.ActorScope, EffectiveScope = session.EffectiveScope, LeaseExpiresAtUtc = Now.AddSeconds(30) };
            Source = new AiKnowledgeIndexDispatchRecord { TaskId = TaskId, DocumentId = Binding.DocumentId, VersionId = Binding.VersionId,
                DocumentVersion = 1, KnowledgeBaseVersion = 1, ModelConfigId = chunks.ModelConfigId, ModelConfigVersion = 1,
                FileId = Guid.CreateVersion7(), DataClassification = "internal", ContentHash = chunks.Document.ContentHash.ToUpperInvariant(), ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document", SizeBytes = chunks.Document.SizeBytes, ModelProviderKey = chunks.ProviderKey, ModelId = chunks.ModelId, ModelEndpointBaseUrl = "https://api.openai.com/v1", LeaseToken = Lease, LeaseExpiresAtUtc = Now.AddSeconds(30) };
            var model = new AiModelConfigRecord { Id = chunks.ModelConfigId, Version = 1, ProviderKey = chunks.ProviderKey,
                ModelId = chunks.ModelId, IsEnabled = true, EndpointBaseUrl = Source.ModelEndpointBaseUrl };
            Payloads = plan.Batches.Select(batch =>
            {
                var vectors = batch.Chunks.Select(_ => { var v = new float[1536]; v[0] = 1; return new ReadOnlyMemory<float>(v); }).ToArray();
                var payload = AiKnowledgeIndexDocxVectorCodec.Encode(plan, batch.Ordinal, vectors, default); var id = Guid.CreateVersion7();
                return new AiKnowledgeIndexVectorRecord { Id = id, IndexTaskId = TaskId, BatchOrdinal = batch.Ordinal,
                    InputCount = vectors.Length, RequestHash = Full.NET.Modules.Ai.Budgets.AiOperationBudgetStore.Fingerprint(
                        Full.NET.Modules.Ai.Budgets.AiEmbeddingBudgetRequest.Create(id, model, batch.Chunks.Select(c => c.Text).ToArray())),
                    Payload = payload, PayloadHash = Convert.ToHexString(SHA256.HashData(payload)), ModelConfigId = model.Id, ModelConfigVersion = 1,
                    ProviderKey = model.ProviderKey, ModelId = model.ModelId, Dimensions = 1536, FormatKey = AiKnowledgeIndexDocxVectorCodec.FormatKey, DistanceKey = "cosine" };
            }).ToArray();
            Receipts = Payloads.Select(r => new AiKnowledgeIndexVectorReceiptRecord { Id = r.Id, BatchOrdinal = r.BatchOrdinal, InputCount = r.InputCount,
                RequestHash = r.RequestHash, PayloadHash = r.PayloadHash, ProviderKey = r.ProviderKey, ModelId = r.ModelId, Dimensions = r.Dimensions,
                FormatKey = r.FormatKey, DistanceKey = r.DistanceKey }).ToArray();
            Batches = Payloads.Select(r => new AiKnowledgeIndexBatchRecord { Id = r.Id, IndexTaskId = TaskId, BatchOrdinal = r.BatchOrdinal,
                RequestHash = r.RequestHash, InputCount = r.InputCount }).ToArray();
            Proof = new(Source, session, Manifest, Array.AsReadOnly(Receipts.ToArray()));
            Clock.UtcNow.Returns(Now); var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(Guid.CreateVersion7());
            query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Pointer);
            query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(call => { PayloadReads++; AtPayload?.Invoke(); return Payloads.SingleOrDefault(row => row.BatchOrdinal == (int)((Dictionary<string, object?>)call[1]!)["BatchOrdinal"]!); });
            query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call => Completed && call.ArgAt<SqlStatement>(0).Name.Contains(".held_", StringComparison.Ordinal) ? null : SessionRow);
            query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => ManifestRow);
            query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { AtAuthority?.Invoke(); return SourceDenied ? null : Source; });
            var auth = Substitute.For<IBackgroundSessionAuthorization>();
            auth.AuthorizeAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
            { Assert.IsFalse(Transactions.HasTransaction); Authorizations++; if (BeforeAuthorization is not null) await BeforeAuthorization(call.ArgAt<CancellationToken>(2));
                return Denied || call.ArgAt<string>(1) == DeniedPermission ? null : new AuthorizedSessionActor(session.UserId, tenantId, session.SessionId); });
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Assert.IsTrue(Transactions.HasTransaction); Writes++; var sql = (SqlStatement)call[0]!; var p = (Dictionary<string, object?>)call[1]!;
                if (sql.Name.Contains("complete", StringComparison.Ordinal)) { Completed = true; return 1; }
                if (sql.Name.Contains("update", StringComparison.Ordinal)) return 0;
                AtInsert?.Invoke(); if (InsertCount != 1) return InsertCount;
                Pointer = new() { Id = (Guid)p["Id"]!, DocumentId = Binding.DocumentId, IndexTaskId = TaskId, VersionId = Binding.VersionId,
                    PlanHash = (string)p["PlanHash"]!, ManifestHash = (string)p["ManifestHash"]!, ReceiptJson = (string)p["ReceiptJson"]!, ReceiptHash = (string)p["ReceiptHash"]!, LeaseToken = Lease };
                return 1;
            });
            var tasks = new AiKnowledgeIndexTaskStore(query, command, tenant, Clock, ids); Tasks = tasks;
            query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(model);
            var collections = new CollectionQuery(query, this); Query = collections; var transaction = new Transaction(this);
            var options = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
            Store = new(collections, command, transaction, Transactions, tenant, Clock, ids, tasks, auth, options);
            var sessions = new AiKnowledgeIndexSessionService(collections, command, transaction, Transactions, tenant, Clock, ids, tasks, auth);
            var manifests = new AiKnowledgeIndexDocxManifestStore(collections, command, transaction, Transactions, tenant, Clock, tasks, sessions, auth);
            var vectors = new AiKnowledgeIndexDocxVectorStore(collections, command, transaction, Transactions, tenant, Clock, tasks, sessions, auth, options);
            Consumer = new(null!, vectors, manifests, sessions, Store);
        }
        internal Task<bool> Activate(CancellationToken token = default) => Store.ActivateAsync(Binding, TaskId, Lease, Proof, token);
        // 集合元素为 internal 行类型，不让动态代理尝试生成该泛型接口的默认代理。
        private sealed class CollectionQuery(IQueryExecutor inner, Fixture f) : IQueryExecutor
        {
            public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default) =>
                inner.QuerySingleOrDefaultAsync<T>(sql, parameters, cancellationToken);
            public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default) =>
                Task.FromResult((IReadOnlyList<T>)(typeof(T) == typeof(AiKnowledgeIndexBatchRecord) ? (object)f.Batches : f.Receipts));
        }
        private sealed class Transaction(Fixture f) : ICommandTransaction
        {
            public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
            {
                f.Begins++; var pointer = f.Pointer; var completed = f.Completed; f.Transactions.HasTransaction.Returns(true);
                try { return await action(token); }
                catch { f.Rollbacks++; f.Pointer = pointer; f.Completed = completed; throw; }
                finally { f.Transactions.HasTransaction.Returns(false); }
            }
        }
    }
}
