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

/// <summary>原会话授权不进入本地事务；等待后失去租约时必须回滚清单。</summary>
[TestClass]
public sealed class AiKnowledgeIndexTextManifestStoreTests
{
    [TestMethod]
    public async Task Index_permission_revocation_stops_manifest_write()
    {
        var f = new Fixture(false) { DeniedPermission = "ai.knowledge_documents.index_enqueue" };
        Assert.IsFalse(await f.Write()); Assert.AreEqual(0, f.Writes);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Whole_plan_is_saved_once_and_read_with_current_authority(bool tenant)
    {
        var f = new Fixture(tenant); Assert.IsTrue(await f.Write()); Assert.AreEqual(1, f.Writes); Assert.AreEqual(1, f.Commits);
        Assert.IsNotNull(f.Row); Assert.AreEqual(f.TaskId, f.Row.Id);
        var read = await f.Read(); Assert.IsNotNull(read); Assert.AreEqual(f.Plan.Summary, read.Summary);
        Assert.AreEqual(17, read.Batches.Sum(b => b.ChunkCount)); Assert.IsTrue(f.AuthCalls >= 8);
    }

    [TestMethod] [DataRow("kb")] [DataRow("kb_version")] [DataRow("model")] [DataRow("model_version")]
    [DataRow("tenant")] [DataRow("hash")] [DataRow("format")]
    public async Task Plan_from_another_source_never_opens_write_transaction(string fault)
    {
        var f = new Fixture(false); var source = f.Input;
        source = fault switch
        {
            "kb" => source with { KnowledgeBaseId = Guid.CreateVersion7() }, "kb_version" => source with { KnowledgeBaseVersion = 2 },
            "model" => source with { ModelConfigId = Guid.CreateVersion7() }, "model_version" => source with { ModelConfigVersion = 2 },
            "tenant" => source with { TenantId = Guid.CreateVersion7() }, "hash" => source with { Chunks = source.Chunks with { ContentHash = new string('B', 64) } },
            _ => source with { Chunks = source.Chunks with { Format = AiKnowledgeTextFormat.Markdown } }
        };
        f.Plan = AiKnowledgeIndexTextPlan.Create(source, default)!;
        Assert.IsFalse(await f.Write()); Assert.AreEqual(0, f.Writes); Assert.AreEqual(0, f.Commits);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Unique_race_only_reuses_identical_manifest_after_rollback(bool changed)
    {
        var f = new Fixture(false) { Unique = true }; f.Row = f.NewRow();
        if (changed) f.Plan = AiKnowledgeIndexTextPlan.Create(f.Input with { Chunks = f.Input.Chunks with
            { Chunks = [f.Input.Chunks.Chunks[0] with { Text = "y", EmbeddingText = "y" }] } }, default)!;
        Assert.AreEqual(!changed, await f.Write()); Assert.AreEqual(1, f.Rollbacks); Assert.AreEqual(0, f.Commits);
    }

    [TestMethod] [DataRow("permission")] [DataRow("session")] [DataRow("source")] [DataRow("model")]
    public async Task Revocation_during_model_read_prevents_transaction(string fault)
    {
        var f = new Fixture(false); f.AfterModel = () => f.Lose(fault);
        Assert.IsFalse(await f.Write()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("permission")] [DataRow("session")] [DataRow("source")] [DataRow("expiry")]
    public async Task Stored_read_wait_cannot_deliver_old_authority(string fault)
    {
        var f = new Fixture(false); f.Row = f.NewRow(); f.AfterRead = () => f.Lose(fault);
        Assert.IsNull(await f.Read()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("expiry")] [DataRow("cancellation")]
    public async Task Write_wait_loss_rolls_back_before_commit(string fault)
    {
        var f = new Fixture(false); using var cancel = new CancellationTokenSource();
        f.AfterWrite = () => { if (fault == "expiry") f.Lose(fault); else cancel.Cancel(); };
        if (fault == "expiry") Assert.IsFalse(await f.Write(cancel.Token));
        else await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Write(cancel.Token));
        Assert.AreEqual(1, f.Writes); Assert.AreEqual(0, f.Commits); Assert.AreEqual(1, f.Rollbacks); Assert.IsNull(f.Row);
    }

    [TestMethod] [DataRow("id")] [DataRow("format")] [DataRow("hash")] [DataRow("payload")]
    public async Task Corrupt_rows_never_report_a_valid_plan(string fault)
    {
        var f = new Fixture(false); var row = f.NewRow();
        f.Row = fault switch { "id" => row with { Id = Guid.CreateVersion7() }, "format" => row with { FormatKey = "future" },
            "hash" => row with { PayloadHash = new string('F', 64) }, _ => row with { Payload = new byte[8] } };
        Assert.IsNull(await f.Read());
    }

    [TestMethod]
    public async Task Zero_write_and_infrastructure_error_cannot_become_idempotent_success()
    {
        var f = new Fixture(false) { WriteCount = 0 }; Assert.IsFalse(await f.Write()); Assert.IsNull(f.Row);
        f = new(false) { Failure = new IOException("database unavailable") };
        await Assert.ThrowsExactlyAsync<IOException>(() => f.Write()); Assert.AreEqual(1, f.Rollbacks);
    }

    [TestMethod]
    public async Task Ambient_transaction_and_cancelled_reads_are_rejected()
    {
        var f = new Fixture(false); f.State.HasTransaction.Returns(true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Write()); await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Read());
        Assert.AreEqual(0, f.AuthCalls); Assert.AreEqual(0, f.Writes);
        f = new(false); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Store.ReadAsync(f.Binding, f.TaskId, f.Lease, cancel.Token));
    }

    private sealed class Fixture
    {
        internal readonly IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        internal readonly IDataTransactionState State = Substitute.For<IDataTransactionState>();
        internal readonly IClock Clock = Substitute.For<IClock>();
        internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal readonly Guid TaskId = Guid.CreateVersion7(), Lease = Guid.CreateVersion7();
        internal readonly AiKnowledgeIndexTaskBinding Binding = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
        internal readonly AiKnowledgeIndexTextManifestStore Store;
        internal readonly AiKnowledgeModelChunks Input;
        internal readonly AiKnowledgeIndexDispatchRecord Source;
        internal AiKnowledgeIndexTextPlan Plan;
        internal AiKnowledgeIndexTextManifestRecord? Row;
        internal bool Denied, SessionDenied, SourceDenied, ModelDenied, Unique;
        internal string? DeniedPermission;
        internal int Writes, Commits, Rollbacks, AuthCalls, WriteCount = 1;
        internal Exception? Failure;
        internal Action? AfterModel, AfterRead, AfterWrite;
        internal Fixture(bool scoped)
        {
            var tenant = new CurrentTenantAccessor(); Guid? tenantId = scoped ? Guid.CreateVersion7() : null;
            if (tenantId is { } id) tenant.SetTenant(new TenantContext(id, "test", "测试")); else tenant.SetHost();
            Input = AiKnowledgeIndexTextPlanTests.Source(17) with { KnowledgeBaseId = Binding.KnowledgeBaseId, TenantId = tenantId };
            Plan = AiKnowledgeIndexTextPlan.Create(Input, default)!; Clock.UtcNow.Returns(Now);
            Source = new() { TaskId = TaskId, DocumentId = Binding.DocumentId, VersionId = Binding.VersionId, DocumentVersion = 1,
                KnowledgeBaseVersion = 1, LeaseToken = Lease, LeaseExpiresAtUtc = Now.AddSeconds(30), ModelConfigId = Input.ModelConfigId,
                ModelConfigVersion = 1, FileId = Guid.CreateVersion7(), ContentHash = Input.Chunks.ContentHash, ContentType = "text/plain", SizeBytes = 17 };
            var model = new AiModelConfigRecord { Id = Input.ModelConfigId, Version = 1, IsEnabled = true, TenantId = tenantId,
                ProviderKey = Input.ProviderKey, ModelId = Input.ModelId, EndpointBaseUrl = "https://api.openai.com/v1" };
            var session = new SessionBindingSnapshot(Binding.OwnerUserId, tenantId, Guid.CreateVersion7(), "stamp", "host", "host");
            var auth = Substitute.For<IBackgroundSessionAuthorization>(); var command = Substitute.For<ICommandExecutor>(); var ids = Substitute.For<IIdGenerator>();
            var tasks = new AiKnowledgeIndexTaskStore(Query, command, tenant, Clock, ids);
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => SourceDenied ? null : Source);
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => SessionDenied ? null : new()
            { Id = session.SessionId, IndexTaskId = TaskId, ActorUserId = session.UserId, TenantId = tenantId, SessionId = session.SessionId, SessionKind = session.SessionKind,
                SecurityStamp = session.SecurityStamp, ActorScope = session.ActorScope, EffectiveScope = session.EffectiveScope, LeaseExpiresAtUtc = Now.AddSeconds(30) });
            Query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { AfterModel?.Invoke(); return ModelDenied ? null : model; });
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { var saved = Row; AfterRead?.Invoke(); return saved; });
            auth.AuthorizeAllAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(call =>
            { Assert.IsFalse(State.HasTransaction); AuthCalls += call.ArgAt<IReadOnlyCollection<string>>(1).Count; return Denied || call.ArgAt<IReadOnlyCollection<string>>(1).Contains(DeniedPermission!) ? null : new AuthorizedSessionActor(session.UserId, tenantId, session.SessionId); });
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            { Assert.IsTrue(State.HasTransaction); Writes++; if (Failure is not null) throw Failure;
                if (Unique) throw new DataCommandException(DataCommandFailureKind.UniqueConstraint, new IOException());
                if (WriteCount == 1) { var p = (IReadOnlyDictionary<string, object?>)call[1]!; Row = NewRow() with { Payload = ((byte[])p["Payload"]!).ToArray(), PayloadHash = (string)p["PayloadHash"]! }; }
                AfterWrite?.Invoke(); return WriteCount; });
            var sessions = new AiKnowledgeIndexSessionService(Query, command, new Transaction(this), State, tenant, Clock, ids, tasks, auth);
            Store = new(Query, command, new Transaction(this), State, tenant, Clock, tasks, sessions, auth);
        }
        internal AiKnowledgeIndexTextManifestRecord NewRow()
        { var bytes = AiKnowledgeIndexTextManifestCodec.Encode(Plan, default); return new() { Id = TaskId, FormatKey = AiKnowledgeIndexTextManifestCodec.FormatKey, Payload = bytes, PayloadHash = Convert.ToHexString(SHA256.HashData(bytes)) }; }
        internal Task<bool> Write(CancellationToken token = default) => Store.PersistAsync(Binding, TaskId, Lease, Plan, token);
        internal Task<AiKnowledgeIndexTextManifest?> Read() => Store.ReadAsync(Binding, TaskId, Lease, default);
        internal void Lose(string fault)
        { if (fault == "permission") Denied = true; else if (fault == "session") SessionDenied = true; else if (fault == "source") SourceDenied = true; else if (fault == "model") ModelDenied = true; else Clock.UtcNow.Returns(Now.AddSeconds(30)); }
    }
    private sealed class Transaction(Fixture fixture) : ICommandTransaction
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken token) => ExecuteAsync(async ct => { await action(ct); return true; }, token);
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
        {
            var saved = fixture.Row; fixture.State.HasTransaction.Returns(true);
            try { var result = await action(token); fixture.Commits++; return result; }
            catch { fixture.Row = saved; fixture.Rollbacks++; throw; }
            finally { fixture.State.HasTransaction.Returns(false); }
        }
    }
}
