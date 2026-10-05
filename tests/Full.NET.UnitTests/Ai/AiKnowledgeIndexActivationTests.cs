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
public sealed class AiKnowledgeIndexActivationTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Public_read_proof_survives_writer_logout_but_original_retry_still_denies(bool tenant)
    {
        var f = new Fixture(tenant); Assert.IsTrue(await f.Activate()); f.Denied = true;
        Assert.IsFalse(await f.Store.IsActiveAsync(f.Binding, f.TaskId, f.Lease, default));
        var calls = f.Authorizations;
        Assert.IsTrue(await f.Store.IsReadyForReadAsync(f.Status, [], default));
        Assert.AreEqual(calls, f.Authorizations); Assert.AreEqual(2, f.Writes);
    }

    [TestMethod] [DataRow("visibility")] [DataRow("metadata")] [DataRow("receipt")] [DataRow("manifest")]
    public async Task Public_read_rechecks_reader_snapshot_and_complete_proof_in_transaction(string fault)
    {
        var f = new Fixture(false); Assert.IsTrue(await f.Activate()); var expected = f.Status;
        f.AtAuthority = () =>
        {
            if (fault == "visibility") f.ReadDenied = true;
            else if (fault == "metadata") f.Status = f.Status with { KnowledgeBaseVersion = 2 };
            else if (fault == "receipt") f.Receipts = [];
            else f.ManifestRow = f.ManifestRow with { Payload = new byte[8] };
        };
        Assert.IsFalse(await f.Store.IsReadyForReadAsync(expected, [], default)); Assert.AreEqual(2, f.Writes);
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
            var json = f.Pointer.ReceiptJson.Replace('"' + new string('A', 64) + '"', "null", StringComparison.Ordinal);
            f.Pointer = f.Pointer with { ReceiptJson = json, ReceiptHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))) };
        }
        else if (fault == "manifest") f.ManifestRow = f.ManifestRow with { Payload = new byte[8] };
        else f.Receipts = [f.Receipts[0] with { Id = Guid.CreateVersion7() }];
        Assert.IsFalse(await f.Store.IsActiveAsync(f.Binding, f.TaskId, f.Lease, default)); Assert.AreEqual(2, f.Writes);
    }

    private sealed class Fixture
    {
        internal readonly Guid TaskId = Guid.CreateVersion7(), Lease = Guid.CreateVersion7();
        internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal readonly IClock Clock = Substitute.For<IClock>();
        internal readonly IDataTransactionState Transactions = Substitute.For<IDataTransactionState>();
        internal readonly AiKnowledgeIndexTaskBinding Binding;
        internal readonly AiKnowledgeIndexGenerationProof Proof;
        internal readonly AiKnowledgeIndexTextManifestSnapshot Manifest;
        internal readonly AiKnowledgeIndexActivationStore Store;
        internal AiKnowledgeIndexSessionRecord SessionRow;
        internal AiKnowledgeIndexTextManifestRecord ManifestRow;
        internal AiKnowledgeIndexVectorReceiptRecord[] Receipts;
        internal AiKnowledgeIndexBatchRecord[] Batches;
        internal AiKnowledgeIndexActivationRecord? Pointer;
        internal AiKnowledgeIndexStatusRecord Status;
        internal bool ReadDenied;
        internal bool Completed, Denied, SourceDenied;
        internal string? DeniedPermission;
        internal int Writes, Begins, Rollbacks, Authorizations, InsertCount = 1;
        internal Action? AtAuthority, AtInsert;

        internal Fixture(bool scoped)
        {
            var tenant = new CurrentTenantAccessor(); Guid? tenantId = scoped ? Guid.CreateVersion7() : null;
            if (tenantId is { } id) tenant.SetTenant(new(id, "test", "测试")); else tenant.SetHost();
            var chunks = AiKnowledgeIndexTextPlanTests.Source() with { TenantId = tenantId };
            Binding = new(chunks.KnowledgeBaseId, Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
            Status = new() { KnowledgeBaseId = Binding.KnowledgeBaseId, DocumentId = Binding.DocumentId, VersionId = Binding.VersionId,
                OwnerUserId = Binding.OwnerUserId, DocumentVersion = 1, KnowledgeBaseVersion = 1, TaskId = TaskId, ActiveTaskId = TaskId, ActiveLeaseToken = Lease };
            var plan = AiKnowledgeIndexTextPlan.Create(chunks, default)!; var bytes = AiKnowledgeIndexTextManifestCodec.Encode(plan, default);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            Manifest = new(AiKnowledgeIndexTextManifestCodec.Decode(bytes, default), hash);
            ManifestRow = new() { Id = TaskId, Payload = bytes, PayloadHash = hash, FormatKey = AiKnowledgeIndexTextManifestCodec.FormatKey };
            var session = new SessionBindingSnapshot(Binding.OwnerUserId, tenantId, Guid.CreateVersion7(), "stamp", scoped ? "tenant" : "host", scoped ? "tenant" : "host");
            SessionRow = new() { Id = Guid.CreateVersion7(), IndexTaskId = TaskId, ActorUserId = session.UserId, TenantId = tenantId,
                SessionId = session.SessionId, SessionKind = session.SessionKind, SecurityStamp = session.SecurityStamp, ActorScope = session.ActorScope, EffectiveScope = session.EffectiveScope };
            var source = new AiKnowledgeIndexDispatchRecord { TaskId = TaskId, DocumentId = Binding.DocumentId, VersionId = Binding.VersionId,
                DocumentVersion = 1, KnowledgeBaseVersion = 1, ModelConfigId = chunks.ModelConfigId, ModelConfigVersion = 1,
                FileId = Guid.CreateVersion7(), ContentHash = chunks.Chunks.ContentHash, ContentType = "text/plain", SizeBytes = 1, LeaseToken = Lease, LeaseExpiresAtUtc = Now.AddSeconds(30) };
            Receipts = [new() { Id = Guid.CreateVersion7(), BatchOrdinal = 0, InputCount = 1, RequestHash = new string('A', 64), PayloadHash = new string('A', 64),
                ProviderKey = "openai_compatible", ModelId = "text-embedding-3-small", Dimensions = 1536, FormatKey = AiKnowledgeIndexVectorCodec.FormatKey, DistanceKey = "cosine" }];
            Batches = [new() { Id = Receipts[0].Id, IndexTaskId = TaskId }]; Proof = new(source, session, Array.AsReadOnly(Receipts.ToArray()));
            Clock.UtcNow.Returns(Now); var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(Guid.CreateVersion7());
            query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Pointer);
            query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => { Assert.IsTrue(Transactions.HasTransaction); return ReadDenied ? null : Status; });
            query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => SessionRow);
            query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => ManifestRow);
            query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { AtAuthority?.Invoke(); return SourceDenied ? null : source; });
            var auth = Substitute.For<IBackgroundSessionAuthorization>();
            auth.AuthorizeAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            { Assert.IsFalse(Transactions.HasTransaction); Authorizations++; return Denied || call.ArgAt<string>(1) == DeniedPermission ? null : new AuthorizedSessionActor(session.UserId, tenantId, session.SessionId); });
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
            var tasks = new AiKnowledgeIndexTaskStore(query, command, tenant, Clock, ids);
            Store = new(new CollectionQuery(query, this), command, new Transaction(this), Transactions, tenant, Clock, ids, tasks, auth, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        }
        internal Task<bool> Activate(CancellationToken token = default) => Store.ActivateAsync(Binding, TaskId, Lease, Proof, Manifest, token);
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
