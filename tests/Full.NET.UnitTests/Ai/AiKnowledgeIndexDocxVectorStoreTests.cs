using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>有界捕获及原会话复核与真实 SQL 分开验证，不用模拟器证明账本结算。</summary>
[TestClass]
public sealed partial class AiKnowledgeIndexDocxVectorStoreTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Valid_complete_docx_batch_is_stored_and_restored_once(bool scoped)
    {
        var f = new Fixture(scoped);
        Assert.IsTrue(await f.Write()); Assert.AreEqual(1, f.Writes); Assert.AreEqual(1, f.Commits);
        Assert.IsNotNull(f.Row); CollectionAssert.AreEqual(f.Payload, f.Row.Payload);
        var result = await f.Read(); Assert.IsNotNull(result); Assert.AreEqual(f.Operation, result.OperationId);
        Assert.AreEqual(f.Plan.Summary.PlanHash, result.Snapshot.Payload.PlanHash);
        Assert.AreEqual(16, result.Snapshot.Payload.Chunks.Count); Assert.AreEqual(1, f.Writes);
    }

    [TestMethod] [DataRow(false, 0)] [DataRow(true, 1)]
    public async Task Large_profile_and_last_block_are_preserved(bool scoped, int ordinal)
    {
        var f = new Fixture(scoped, true, ordinal); Assert.IsTrue(await f.Write());
        var result = await f.Read(); Assert.IsNotNull(result);
        Assert.AreEqual(3072, result.Snapshot.Payload.Dimensions);
        Assert.AreEqual(ordinal == 0 ? 16 : 1, result.Snapshot.Payload.Chunks.Count);
        Assert.AreEqual(ordinal * 16, result.Snapshot.Payload.Chunks[0].Source.BlockSequence);
        Assert.AreEqual(int.MinValue, BitConverter.SingleToInt32Bits(result.Snapshot.Payload.Chunks[0].Vector.Span[1]));
    }

    [TestMethod]
    [DataRow("ai.knowledge_bases.read")] [DataRow("ai.knowledge_documents.read")]
    [DataRow("ai.knowledge_documents.source_read")] [DataRow("ai.knowledge_documents.upload")]
    [DataRow("ai.knowledge_documents.index_enqueue")]
    public async Task Revoked_original_permission_denies_write_and_recovery(string permission)
    {
        var f = new Fixture(false); f.Row = f.NewRow(); f.DeniedPermission = permission;
        Assert.IsFalse(await f.Write()); Assert.IsNull(await f.Read()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    [DataRow("permission")] [DataRow("session")] [DataRow("source")] [DataRow("model")] [DataRow("lease")]
    public async Task Authority_loss_while_model_waits_prevents_transaction(string fault)
    {
        var f = new Fixture(true); f.AfterModel = () => f.Lose(fault);
        Assert.IsFalse(await f.Write()); Assert.AreEqual(0, f.Writes); Assert.AreEqual(0, f.Commits);
    }

    [TestMethod]
    [DataRow("permission")] [DataRow("session")] [DataRow("source")] [DataRow("model")] [DataRow("lease")] [DataRow("ledger")]
    public async Task Recovery_rechecks_changes_after_first_vector_read(string fault)
    {
        var f = new Fixture(false); f.Row = f.NewRow(); f.AfterRead = () => f.Lose(fault);
        Assert.IsNull(await f.Read()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    [DataRow("permission")] [DataRow("session")] [DataRow("source")] [DataRow("model")] [DataRow("lease")]
    public async Task Recovery_denies_authority_loss_during_last_vector_query(string fault)
    {
        var f = new Fixture(false); f.Row = f.NewRow();
        f.AfterRead = () => { if (f.VectorReads == 2) f.Lose(fault); };
        Assert.IsNull(await f.Read());
    }

    [TestMethod]
    [DataRow("missing")] [DataRow("format")] [DataRow("hash")] [DataRow("corrupt")] [DataRow("task")] [DataRow("size")]
    public async Task Invalid_complete_manifest_denies_write_and_recovery(string fault)
    {
        var f = new Fixture(true); f.Row = f.NewRow(); var m = f.Manifest!;
        f.Manifest = fault switch { "missing" => null, "format" => m with { FormatKey = "knowledge-text-manifest-v1" },
            "hash" => m with { PayloadHash = new string('0', 64) }, "corrupt" => m with { Payload = new byte[8], PayloadHash = Hash(new byte[8]) },
            "task" => m with { Id = Guid.CreateVersion7() }, _ => m with { Payload = new byte[8388609] } };
        Assert.IsFalse(await f.Write()); Assert.IsNull(await f.Read()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    public async Task Different_valid_plan_and_changed_manifest_during_recovery_are_rejected()
    {
        var f = new Fixture(false); var original = f.Plan;
        f.Plan = AiKnowledgeIndexDocxPlan.Create(f.Input with { Document = f.Input.Document with { ContentHash = new string('b', 64) } }, default)!;
        Assert.IsFalse(await f.Write()); Assert.AreEqual(0, f.Writes); f.Plan = original; f.Row = f.NewRow();
        f.AfterRead = () => { f.Plan = AiKnowledgeIndexDocxPlan.Create(f.Input with { KnowledgeBaseVersion = 2 }, default)!; f.Manifest = f.NewManifest(); };
        Assert.IsNull(await f.Read());
    }

    [TestMethod]
    public async Task Input_vectors_are_frozen_before_model_wait()
    {
        var f = new Fixture(false);
        f.AfterModel = () => { var values = f.Vectors[0].ToArray(); values[0] = 9; f.Vectors[0] = values; };
        Assert.IsTrue(await f.Write()); CollectionAssert.AreEqual(f.Payload, f.Row!.Payload);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Post_write_expiry_or_cancellation_rolls_back(bool cancelled)
    {
        var f = new Fixture(false); using var cancellation = new CancellationTokenSource();
        f.AfterWrite = () => { if (cancelled) cancellation.Cancel(); else f.Clock.UtcNow.Returns(f.Now.AddSeconds(30)); };
        if (cancelled) await Assert.ThrowsAsync<OperationCanceledException>(() => f.Write(cancellation.Token));
        else Assert.IsFalse(await f.Write());
        Assert.IsNull(f.Row); Assert.AreEqual(1, f.Rollbacks); Assert.AreEqual(0, f.Commits);
    }

    [TestMethod] [DataRow("same")] [DataRow("operation")] [DataRow("payload")]
    public async Task Unique_conflict_only_reuses_identical_operation_and_bytes(string fault)
    {
        var f = new Fixture(true); f.Row = f.NewRow(); f.Unique = true;
        if (fault == "operation") f.Row = f.Row with { Id = Guid.CreateVersion7() };
        if (fault == "payload") { var vectors = f.Vectors.ToArray(); var first = vectors[0].ToArray(); first[0] = 2; vectors[0] = first;
            var bytes = AiKnowledgeIndexDocxVectorCodec.Encode(f.Plan, 0, vectors, default); f.Row = f.Row with { Payload = bytes, PayloadHash = Hash(bytes) }; }
        Assert.AreEqual(fault == "same", await f.Write()); Assert.AreEqual(1, f.Rollbacks); Assert.AreEqual(0, f.Commits);
    }

    [TestMethod]
    [DataRow("task")] [DataRow("ordinal")] [DataRow("model")] [DataRow("version")] [DataRow("provider")]
    [DataRow("dimensions")] [DataRow("format")] [DataRow("distance")] [DataRow("count")] [DataRow("request")] [DataRow("hash")] [DataRow("corrupt")]
    public async Task Invalid_vector_receipt_never_restores(string fault)
    {
        var f = new Fixture(false); var r = f.NewRow();
        f.Row = fault switch { "task" => r with { IndexTaskId = Guid.CreateVersion7() }, "ordinal" => r with { BatchOrdinal = 1 },
            "model" => r with { ModelConfigId = Guid.CreateVersion7() }, "version" => r with { ModelConfigVersion = 2 },
            "provider" => r with { ProviderKey = "other" }, "dimensions" => r with { Dimensions = 3072 },
            "format" => r with { FormatKey = "knowledge-text-vector-batch-v1" }, "distance" => r with { DistanceKey = "l2" },
            "count" => r with { InputCount = 1 }, "request" => r with { RequestHash = new string('0', 64) },
            "hash" => r with { PayloadHash = new string('0', 64) }, _ => r with { Payload = new byte[8], PayloadHash = Hash(new byte[8]) } };
        Assert.IsNull(await f.Read()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    public async Task Infrastructure_fault_and_ambient_transaction_propagate()
    {
        var f = new Fixture(false); f.Failure = new IOException();
        await Assert.ThrowsAsync<IOException>(() => f.Write()); Assert.AreEqual(1, f.Rollbacks);
        f.State.HasTransaction.Returns(true); await Assert.ThrowsAsync<InvalidOperationException>(() => f.Read());
    }

    [TestMethod]
    public async Task Post_commit_revocation_does_not_report_success()
    {
        var f = new Fixture(false); f.AfterCommit = () => f.Denied = true;
        Assert.IsFalse(await f.Write()); Assert.IsNotNull(f.Row); Assert.AreEqual(1, f.Commits);
    }

    private sealed class Fixture
    {
        internal readonly IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        internal readonly IDataTransactionState State = Substitute.For<IDataTransactionState>();
        internal readonly IClock Clock = Substitute.For<IClock>();
        internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal readonly Guid TaskId = Guid.CreateVersion7(), Lease = Guid.CreateVersion7();
        internal Guid Operation = Guid.CreateVersion7();
        internal readonly AiKnowledgeIndexTaskBinding Binding = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
        internal readonly AiKnowledgeIndexDocxVectorStore Store;
        internal readonly AiKnowledgeDocxModelChunks Input;
        internal AiModelConfigRecord Model;
        internal AiKnowledgeIndexDispatchRecord Source;
        internal AiKnowledgeIndexDocxPlan Plan;
        internal AiKnowledgeIndexTextManifestRecord? Manifest;
        internal AiKnowledgeIndexVectorRecord? Row;
        internal ReadOnlyMemory<float>[] Vectors;
        internal byte[] Payload;
        internal Dictionary<int, AiKnowledgeIndexVectorRecord>? Rows;
        internal Action? AfterReceipts = null;
        internal int ReceiptReads;
        internal Func<IReadOnlyList<AiKnowledgeIndexVectorReceiptRecord>, IReadOnlyList<AiKnowledgeIndexVectorReceiptRecord>>? ChangeReceipts;
        internal int Ordinal, Writes, Commits, Rollbacks, AuthCalls, VectorReads, WriteCount = 1;
        internal bool Denied, SessionDenied, SourceDenied, ModelDenied, Unique = false, LedgerDenied;
        internal string? DeniedPermission = null;
        internal Exception? Failure = null;
        internal Action? AfterModel = null, AfterManifest = null, AfterRead = null, AfterWrite = null, AfterCommit = null;

        internal Fixture(bool scoped, bool large = false, int ordinal = 0)
        {
            Ordinal = ordinal; var tenant = new CurrentTenantAccessor(); Guid? tenantId = scoped ? Guid.CreateVersion7() : null;
            if (tenantId is { } id) tenant.SetTenant(new TenantContext(id, "test", "测试")); else tenant.SetHost();
            Input = AiKnowledgeIndexDocxCodecTests.Plan(17).Source with { KnowledgeBaseId = Binding.KnowledgeBaseId, KnowledgeBaseVersion = 1,
                ModelConfigVersion = 1, TenantId = tenantId, ModelId = large ? "text-embedding-3-large" : "text-embedding-3-small" };
            Plan = AiKnowledgeIndexDocxPlan.Create(Input, default)!; Clock.UtcNow.Returns(Now);
            Source = NewSource(); Model = new() { Id = Input.ModelConfigId, Version = 1, IsEnabled = true, TenantId = tenantId,
                ProviderKey = Input.ProviderKey, ModelId = Input.ModelId, EndpointBaseUrl = "https://api.openai.com/v1" };
            Vectors = Plan.Batches[Ordinal].Chunks.Select(_ => { var values = new float[large ? 3072 : 1536]; values[0] = 1; values[1] = BitConverter.Int32BitsToSingle(int.MinValue); return new ReadOnlyMemory<float>(values); }).ToArray();
            Payload = AiKnowledgeIndexDocxVectorCodec.Encode(Plan, Ordinal, Vectors, default); Manifest = NewManifest();
            var session = new SessionBindingSnapshot(Binding.OwnerUserId, tenantId, Guid.CreateVersion7(), "stamp", "host", "host");
            var auth = Substitute.For<IBackgroundSessionAuthorization>(); var command = Substitute.For<ICommandExecutor>(); var ids = Substitute.For<IIdGenerator>();
            var tasks = new AiKnowledgeIndexTaskStore(Query, command, tenant, Clock, ids);
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => SourceDenied ? null : Source);
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => SessionDenied ? null : new()
            { Id = session.SessionId, IndexTaskId = TaskId, ActorUserId = session.UserId, TenantId = tenantId, SessionId = session.SessionId,
                SessionKind = session.SessionKind, SecurityStamp = session.SecurityStamp, ActorScope = session.ActorScope,
                EffectiveScope = session.EffectiveScope, LeaseExpiresAtUtc = Now.AddSeconds(30) });
            Query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { AfterModel?.Invoke(); return ModelDenied ? null : Model; });
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { var row = Manifest; AfterManifest?.Invoke(); return row; });
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { VectorReads++; var ordinal = (int)((IReadOnlyDictionary<string, object?>)_[1]!)["BatchOrdinal"]!;
                var row = LedgerDenied ? null : Rows is null ? Row : Rows.GetValueOrDefault(ordinal); AfterRead?.Invoke(); return row; });
            auth.AuthorizeAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            { Assert.IsFalse(State.HasTransaction); AuthCalls++; return Denied || call.ArgAt<string>(1) == DeniedPermission ? null : new AuthorizedSessionActor(session.UserId, tenantId, session.SessionId); });
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Assert.IsTrue(State.HasTransaction); Writes++; if (Failure is not null) throw Failure;
                if (Unique) throw new DataCommandException(DataCommandFailureKind.UniqueConstraint, new IOException());
                var p = (IReadOnlyDictionary<string, object?>)call[1]!;
                Assert.AreEqual(Manifest!.PayloadHash, p["DocxManifestHash"]);
                if (WriteCount == 1) Row = NewRow() with { Id = (Guid)p["OperationId"]!, Payload = ((byte[])p["Payload"]!).ToArray(),
                    PayloadHash = (string)p["PayloadHash"]!, RequestHash = (string)p["RequestHash"]! };
                AfterWrite?.Invoke(); return WriteCount;
            });
            var sessions = new AiKnowledgeIndexSessionService(Query, command, new Transaction(this), State, tenant, Clock, ids, tasks, auth);
            Store = new(new ReceiptQuery(this), command, new Transaction(this), State, tenant, Clock, tasks, sessions, auth, Options.Create(new DatabaseOptions()));
        }
        // 集合读取手写边界，内部行类型无需对动态代理程序集开放。
        private sealed class ReceiptQuery(Fixture fixture) : IQueryExecutor
        {
            public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default)
                => fixture.Query.QuerySingleOrDefaultAsync<T>(sql, parameters, cancellationToken);
            public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default)
            {
                Assert.AreEqual(typeof(AiKnowledgeIndexVectorReceiptRecord), typeof(T));
                fixture.ReceiptReads++; IReadOnlyList<AiKnowledgeIndexVectorReceiptRecord> rows = fixture.LedgerDenied || fixture.Rows is null ? []
                    : fixture.Rows.OrderBy(x => x.Key).Select(x => ToReceipt(x.Value)).ToArray();
                if (fixture.ChangeReceipts is not null) rows = fixture.ChangeReceipts(rows);
                fixture.AfterReceipts?.Invoke(); return Task.FromResult((IReadOnlyList<T>)(object)rows);
            }
        }
        internal Task<bool> Write(CancellationToken token = default) => Store.PersistAsync(Binding, TaskId, Lease, Operation, Plan, Ordinal, Vectors, token);
        internal Task<AiKnowledgeIndexDocxStoredBatch?> Read(CancellationToken token = default) => Store.ReadSnapshotAsync(Binding, TaskId, Lease, Ordinal, token);
        internal AiKnowledgeIndexTextManifestRecord NewManifest()
        { var bytes = AiKnowledgeIndexDocxManifestCodec.Encode(Plan, default); return new() { Id = TaskId, FormatKey = AiKnowledgeIndexDocxManifestCodec.FormatKey, Payload = bytes, PayloadHash = Hash(bytes) }; }
        internal AiKnowledgeIndexVectorRecord NewRow() => new() { Id = Operation, IndexTaskId = TaskId, BatchOrdinal = Ordinal,
            ModelConfigId = Model.Id, ModelConfigVersion = Model.Version, ProviderKey = Model.ProviderKey, ModelId = Model.ModelId,
            Dimensions = Vectors[0].Length, FormatKey = AiKnowledgeIndexDocxVectorCodec.FormatKey, DistanceKey = "cosine",
            Payload = Payload.ToArray(), PayloadHash = Hash(Payload), InputCount = Vectors.Length,
            RequestHash = AiOperationBudgetStore.Fingerprint(AiEmbeddingBudgetRequest.Create(Operation, Model, Plan.Batches[Ordinal].Chunks.Select(c => c.Text).ToArray())) };
        internal AiKnowledgeIndexDispatchRecord NewSource(string? mime = null, Guid? file = null) => new()
        { TaskId = TaskId, DocumentId = Binding.DocumentId, VersionId = Binding.VersionId, DocumentVersion = 1, KnowledgeBaseVersion = 1,
            ModelConfigId = Input.ModelConfigId, ModelConfigVersion = 1, LeaseToken = Lease, LeaseExpiresAtUtc = Now.AddSeconds(30),
            FileId = file ?? Source?.FileId ?? Guid.CreateVersion7(), ContentHash = Input.Document.ContentHash, ContentType = mime ?? "application/vnd.openxmlformats-officedocument.wordprocessingml.document", SizeBytes = Input.Document.SizeBytes,
                DataClassification = "internal", ModelProviderKey = Input.ProviderKey, ModelId = Input.ModelId,
                ModelEndpointBaseUrl = "https://api.openai.com/v1" };
        internal void Lose(string fault)
        { if (fault == "permission") Denied = true; else if (fault == "session") SessionDenied = true; else if (fault == "source") SourceDenied = true;
            else if (fault == "model") ModelDenied = true; else if (fault == "ledger") LedgerDenied = true; else Clock.UtcNow.Returns(Now.AddSeconds(30)); }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private sealed class Transaction(Fixture fixture) : ICommandTransaction
    {
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
        {
            var saved = fixture.Row; fixture.State.HasTransaction.Returns(true);
            try { var result = await action(token); fixture.Commits++; return result; }
            catch { fixture.Row = saved; fixture.Rollbacks++; throw; }
            finally { fixture.State.HasTransaction.Returns(false); fixture.AfterCommit?.Invoke(); }
        }
    }
}
