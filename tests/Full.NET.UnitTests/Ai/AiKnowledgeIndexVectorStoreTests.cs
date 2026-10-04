using System.Runtime.InteropServices;
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
using NSubstitute;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Ai;

/// <summary>授权不进入 AI 事务，持久恢复和写入均复核等待后的原会话与有效租约。</summary>
[TestClass]
public sealed class AiKnowledgeIndexVectorStoreTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Valid_batch_is_frozen_and_written_in_one_short_transaction(bool tenant)
    {
        var f = new Fixture(tenant); Assert.IsTrue(await f.Write()); Assert.AreEqual(1, f.Writes); Assert.AreEqual(1, f.Commits);
        Assert.IsNotNull(f.Row); Assert.AreEqual(f.Batch.OperationId, f.Row.Id);
        CollectionAssert.AreEqual(f.OriginalPayload, f.Row.Payload);
        Assert.IsTrue(await f.Read()); Assert.AreEqual(1, f.Writes); Assert.IsTrue(f.AuthCalls >= 8);
    }

    [TestMethod] [DataRow("id")] [DataRow("task")] [DataRow("lease")] [DataRow("document")]
    public async Task Foreign_batch_identity_never_writes(string fault)
    {
        var f = new Fixture(false);
        if (fault == "id") f.Batch = f.Batch with { OperationId = Guid.Empty };
        else f.Batch = f.Batch with { Source = f.NewSource(fault) };
        Assert.IsFalse(await f.Write()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("model")] [DataRow("session")] [DataRow("authority")] [DataRow("permission")]
    public async Task Lost_authority_during_model_read_never_opens_write_transaction(string fault)
    {
        var f = new Fixture(false);
        f.AfterModel = () => { if (fault == "model") f.ModelDenied = true; else if (fault == "session") f.SessionDenied = true;
            else if (fault == "authority") f.SourceDenied = true; else f.Denied = true; };
        Assert.IsFalse(await f.Write()); Assert.AreEqual(0, f.Writes); Assert.AreEqual(0, f.Commits);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Unique_race_only_reuses_exact_payload_after_rollback(bool conflict)
    {
        var f = new Fixture(false) { Unique = true }; f.Row = f.NewRow();
        if (conflict) f.Row = f.Row with { PayloadHash = new string('B', 64) };
        Assert.AreEqual(!conflict, await f.Write()); Assert.AreEqual(1, f.Rollbacks); Assert.AreEqual(0, f.Commits);
        Assert.AreEqual(1, f.Writes);
    }

    [TestMethod]
    public async Task Zero_guarded_write_is_refusal_and_infrastructure_errors_propagate()
    {
        var f = new Fixture(false) { WriteCount = 0 }; Assert.IsFalse(await f.Write()); Assert.IsNull(f.Row);
        f = new(false) { Failure = new IOException("database unavailable") };
        await Assert.ThrowsExactlyAsync<IOException>(() => f.Write()); Assert.AreEqual(1, f.Rollbacks); Assert.IsNull(f.Row);
    }

    [TestMethod]
    public async Task Caller_vector_mutation_during_model_read_cannot_change_frozen_write()
    {
        var f = new Fixture(false); Assert.IsTrue(MemoryMarshal.TryGetArray(f.Batch.Chunks[0].Vector, out var segment));
        f.AfterModel = () => segment.Array![segment.Offset] = 99;
        Assert.IsTrue(await f.Write()); Assert.IsNotNull(f.Row); CollectionAssert.AreEqual(f.OriginalPayload, f.Row.Payload);
    }

    [TestMethod] [DataRow("authority")] [DataRow("session")] [DataRow("permission")] [DataRow("expiry")]
    public async Task Stored_query_wait_cannot_deliver_previous_authority(string fault)
    {
        var f = new Fixture(false); f.Row = f.NewRow();
        f.AfterRead = () => { if (fault == "authority") f.SourceDenied = true; else if (fault == "session") f.SessionDenied = true;
            else if (fault == "permission") f.Denied = true; else f.Clock.UtcNow.Returns(f.Now.AddSeconds(30)); };
        Assert.IsFalse(await f.Read()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    public async Task Revocation_after_commit_rejects_success_without_removing_derived_data()
    {
        var f = new Fixture(false); f.AfterCommit = () => f.Denied = true;
        Assert.IsFalse(await f.Write()); Assert.IsNotNull(f.Row); Assert.AreEqual(1, f.Commits);
    }

    [TestMethod] [DataRow("expiry")] [DataRow("cancellation")]
    public async Task Write_wait_loss_rolls_back_before_transaction_can_commit(string fault)
    {
        var f = new Fixture(false); using var cancel = new CancellationTokenSource();
        f.AfterWrite = () => { if (fault == "expiry") f.Clock.UtcNow.Returns(f.Now.AddSeconds(30)); else cancel.Cancel(); };
        if (fault == "expiry") Assert.IsFalse(await f.Write(cancel.Token));
        else await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Write(cancel.Token));
        Assert.AreEqual(1, f.Writes); Assert.AreEqual(0, f.Commits); Assert.AreEqual(1, f.Rollbacks); Assert.IsNull(f.Row);
    }

    [TestMethod] [DataRow("task")] [DataRow("model")] [DataRow("hash")] [DataRow("format")]
    [DataRow("distance")] [DataRow("count")] [DataRow("provider")] [DataRow("dimension")]
    public async Task Corrupt_stored_metadata_cannot_be_reported_as_complete(string fault)
    {
        var f = new Fixture(false); f.Row = f.NewRow(); f.Row = fault switch
        {
            "task" => f.Row with { IndexTaskId = Guid.CreateVersion7() }, "model" => f.Row with { ModelConfigVersion = 2 },
            "hash" => f.Row with { RequestHash = new string('F', 64) }, "format" => f.Row with { FormatKey = "future" },
            "distance" => f.Row with { DistanceKey = "dot" }, "count" => f.Row with { InputCount = 2 },
            "provider" => f.Row with { ProviderKey = "other" }, _ => f.Row with { Dimensions = 3072 },
        };
        Assert.IsFalse(await f.Read()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    public async Task Malformed_payload_with_matching_hash_fails_closed()
    {
        var f = new Fixture(false); byte[] bad = new byte[8];
        f.Row = f.NewRow() with { Payload = bad, PayloadHash = Convert.ToHexString(SHA256.HashData(bad)) };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Read()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    public async Task Ambient_transaction_and_cancelled_wait_are_rejected()
    {
        var f = new Fixture(false); f.State.HasTransaction.Returns(true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Write()); await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Read());
        Assert.AreEqual(0, f.AuthCalls); Assert.AreEqual(0, f.Writes);
        f = new(false); f.Row = f.NewRow(); using var cancel = new CancellationTokenSource(); f.AfterRead = cancel.Cancel;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Store.IsStoredAsync(f.Binding, f.TaskId, f.Lease, 0, cancel.Token));
    }

    [TestMethod] [DataRow("same")] [DataRow("text")] [DataRow("offset")] [DataRow("tokens")]
    [DataRow("context")] [DataRow("model")] [DataRow("knowledge")]
    public async Task Stored_vector_must_match_complete_plan_batch_instead_of_only_existing(string fault)
    {
        var f = new Fixture(false); f.Row = f.NewRow(); var chunk = f.Batch.Chunks[0].Source;
        chunk = fault switch { "text" => chunk with { Text = "改变😀", EmbeddingText = "标题\n改变😀" }, "offset" => chunk with { Offset = chunk.Offset + 1 },
            "tokens" => chunk with { TokenCount = chunk.TokenCount + 1 }, "context" => chunk with { Context = [] }, _ => chunk };
        var input = AiKnowledgeIndexTextPlanTests.Source() with { KnowledgeBaseId = fault == "knowledge" ? Guid.CreateVersion7() : f.Binding.KnowledgeBaseId,
            ModelConfigId = f.Source.ModelConfigId, ModelConfigVersion = fault == "model" ? 2 : 1,
            Chunks = AiKnowledgeIndexTextPlanTests.Source().Chunks with { ContentHash = f.Source.ContentHash, Chunks = [chunk] } };
        var plan = AiKnowledgeIndexTextPlan.Create(input, default); Assert.IsNotNull(plan);
        var manifest = AiKnowledgeIndexTextManifestCodec.Decode(AiKnowledgeIndexTextManifestCodec.Encode(plan, default), default);
        Assert.AreEqual(fault == "same", await f.Store.MatchesPlanAsync(f.Binding, f.TaskId, f.Lease, manifest, 0, default));
        Assert.AreEqual(0, f.Writes);
    }

    private sealed class Fixture
    {
        internal readonly IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        internal readonly IDataTransactionState State = Substitute.For<IDataTransactionState>();
        internal readonly IClock Clock = Substitute.For<IClock>();
        internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal readonly Guid TaskId = Guid.CreateVersion7(), Lease = Guid.CreateVersion7();
        internal readonly AiKnowledgeIndexTaskBinding Binding = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
        internal readonly AiKnowledgeIndexVectorStore Store;
        internal readonly AiKnowledgeIndexDispatchRecord Source;
        internal readonly byte[] OriginalPayload;
        internal AiKnowledgeIndexEmbeddingBatch Batch;
        internal AiKnowledgeIndexVectorRecord? Row;
        internal bool Denied, SessionDenied, SourceDenied, ModelDenied, Unique;
        internal int Writes, Commits, Rollbacks, AuthCalls, WriteCount = 1;
        internal Exception? Failure;
        internal Action? AfterModel, AfterRead, AfterCommit, AfterWrite;
        private readonly AiModelConfigRecord model;
        internal Fixture(bool scoped)
        {
            var tenant = new CurrentTenantAccessor(); Guid? tenantId = scoped ? Guid.CreateVersion7() : null;
            if (tenantId is { } id) tenant.SetTenant(new TenantContext(id, "test", "测试")); else tenant.SetHost();
            Clock.UtcNow.Returns(Now); Source = NewSource();
            Batch = AiKnowledgeIndexVectorCodecTests.Sample() with { Source = Source }; OriginalPayload = AiKnowledgeIndexVectorCodec.Encode(Batch, 1536, default);
            model = new() { Id = Source.ModelConfigId, Version = 1, IsEnabled = true, TenantId = tenantId,
                ProviderKey = "openai_compatible", ModelId = "text-embedding-3-small", EndpointBaseUrl = "https://api.openai.com/v1" };
            var session = new SessionBindingSnapshot(Binding.OwnerUserId, tenantId, Guid.CreateVersion7(), "stamp", "host", "host");
            var auth = Substitute.For<IBackgroundSessionAuthorization>(); var command = Substitute.For<ICommandExecutor>(); var ids = Substitute.For<IIdGenerator>();
            var tasks = new AiKnowledgeIndexTaskStore(Query, command, tenant, Clock, ids);
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => SourceDenied ? null : Source);
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => SessionDenied ? null : new()
            { Id = session.SessionId, IndexTaskId = TaskId, ActorUserId = session.UserId, TenantId = tenantId, SessionId = session.SessionId, SessionKind = session.SessionKind,
                SecurityStamp = session.SecurityStamp, ActorScope = session.ActorScope, EffectiveScope = session.EffectiveScope, LeaseExpiresAtUtc = Now.AddSeconds(30) });
            Query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { AfterModel?.Invoke(); return ModelDenied ? null : model; });
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { var saved = Row; AfterRead?.Invoke(); return saved; });
            auth.AuthorizeAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
            { Assert.IsFalse(State.HasTransaction); AuthCalls++; return Denied ? null : new AuthorizedSessionActor(session.UserId, session.TenantId, session.SessionId); });
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            { Assert.IsTrue(State.HasTransaction); Writes++; if (Failure is not null) throw Failure;
                if (Unique) throw new DataCommandException(DataCommandFailureKind.UniqueConstraint, new IOException());
                if (WriteCount == 1) Row = NewRow() with { Payload = ReadSqlParameter<byte[]>(call[1], "Payload").ToArray(),
                    PayloadHash = ReadSqlParameter<string>(call[1], "PayloadHash"), RequestHash = ReadSqlParameter<string>(call[1], "RequestHash") };
                AfterWrite?.Invoke(); return WriteCount; });
            var sessions = new AiKnowledgeIndexSessionService(Query, command, new Transaction(this), State, tenant, Clock, ids, tasks, auth);
            Store = new(Query, command, new Transaction(this), State, tenant, Clock, tasks, sessions, auth, Options.Create(new DatabaseOptions()));
        }
        internal Task<bool> Write(CancellationToken token = default) => Store.PersistAsync(Binding, TaskId, Lease, Batch, token);
        internal Task<bool> Read() => Store.IsStoredAsync(Binding, TaskId, Lease, 0, default);
        internal AiKnowledgeIndexVectorRecord NewRow() => new() { Id = Batch.OperationId, IndexTaskId = TaskId, BatchOrdinal = 0,
            ModelConfigId = model.Id, ModelConfigVersion = 1, ProviderKey = model.ProviderKey, ModelId = model.ModelId, Dimensions = 1536,
            FormatKey = AiKnowledgeIndexVectorCodec.FormatKey, DistanceKey = "cosine", Payload = OriginalPayload.ToArray(), InputCount = 1,
            PayloadHash = Convert.ToHexString(SHA256.HashData(OriginalPayload)),
            RequestHash = AiOperationBudgetStore.Fingerprint(AiEmbeddingBudgetRequest.Create(Batch.OperationId, model, Batch.Chunks.Select(item => item.Source.EmbeddingText).ToArray())) };
        internal AiKnowledgeIndexDispatchRecord NewSource(string? fault = null) => new() { TaskId = fault == "task" ? Guid.CreateVersion7() : TaskId,
            DocumentId = fault == "document" ? Guid.CreateVersion7() : Binding.DocumentId, VersionId = Binding.VersionId, DocumentVersion = 1, KnowledgeBaseVersion = 1,
            ModelConfigId = Source?.ModelConfigId ?? Guid.CreateVersion7(), ModelConfigVersion = 1, LeaseToken = fault == "lease" ? Guid.CreateVersion7() : Lease,
            LeaseExpiresAtUtc = Now.AddSeconds(30), FileId = Source?.FileId ?? Guid.CreateVersion7(), ContentHash = new string('A', 64), ContentType = "text/plain", SizeBytes = 20 };
        private sealed class Transaction(Fixture fixture) : ICommandTransaction
        {
            public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
            {
                fixture.State.HasTransaction.Returns(true);
                var before = fixture.Row;
                try { var result = await action(token); fixture.Commits++; return result; }
                catch { fixture.Row = before; fixture.Rollbacks++; throw; }
                finally { fixture.State.HasTransaction.Returns(false); fixture.AfterCommit?.Invoke(); }
            }
        }
    }
}
