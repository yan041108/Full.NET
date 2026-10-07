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
public sealed class AiKnowledgeIndexDocxManifestStoreTests
{
    [TestMethod] [DataRow("ai.knowledge_bases.read")] [DataRow("ai.knowledge_documents.read")]
    [DataRow("ai.knowledge_documents.source_read")] [DataRow("ai.knowledge_documents.upload")]
    [DataRow("ai.knowledge_documents.index_enqueue")]
    public async Task Every_required_permission_gates_docx_write_and_recovery(string permission)
    {
        var f = new Fixture(false) { DeniedPermission = permission }; f.Row = f.NewRow();
        Assert.IsFalse(await f.Write()); Assert.IsNull(await f.Read()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("text-format")] [DataRow("text-bytes")]
    [DataRow("oversize")] [DataRow("self-consistent-corrupt")]
    public async Task Legacy_text_or_self_consistent_damaged_rows_cannot_restore_docx(string fault)
    {
        var f = new Fixture(false) { Unique = true }; var row = f.NewRow();
        var bytes = fault switch
        {
            "oversize" => new byte[AiKnowledgeIndexDocxManifestCodec.MaxPayloadBytes + 1],
            "self-consistent-corrupt" => new byte[8],
            _ => AiKnowledgeIndexTextManifestCodec.Encode(AiKnowledgeIndexTextPlan.Create(AiKnowledgeIndexTextPlanTests.Source(), default)!, default)
        };
        f.Row = row with { FormatKey = fault == "text-format" ? AiKnowledgeIndexTextManifestCodec.FormatKey : row.FormatKey,
            Payload = bytes, PayloadHash = Convert.ToHexString(SHA256.HashData(bytes)) };
        Assert.IsNull(await f.Read()); Assert.IsFalse(await f.Write()); Assert.AreEqual(1, f.Rollbacks);
    }

    [TestMethod]
    public async Task File_binding_changed_during_read_cannot_restore_original_plan()
    {
        var f = new Fixture(false); f.Row = f.NewRow();
        f.AfterRead = () => f.ChangeSource(fileId: Guid.CreateVersion7());
        Assert.IsNull(await f.Read());
    }

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
        Assert.AreEqual(17, read.Batches.Sum(b => b.Chunks.Count)); Assert.IsTrue(f.AuthCalls >= 8);
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
            "tenant" => source with { TenantId = Guid.CreateVersion7() }, "hash" => source with { Document = source.Document with { ContentHash = new string('b', 64) } },
            _ => source
        };
        if (fault == "format") f.ChangeSource(contentType: "text/plain");
        f.Plan = AiKnowledgeIndexDocxPlan.Create(source, default)!;
        Assert.IsFalse(await f.Write()); Assert.AreEqual(0, f.Writes); Assert.AreEqual(0, f.Commits);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Unique_race_only_reuses_identical_manifest_after_rollback(bool changed)
    {
        var f = new Fixture(false) { Unique = true }; f.Row = f.NewRow();
        if (changed) f.Plan = AiKnowledgeIndexDocxPlan.Create(f.Input with { Document = AiKnowledgeIndexDocxCodecTests.Plan(1).Source.Document, Chunks = AiKnowledgeIndexDocxCodecTests.Plan(1).Source.Chunks }, default)!;
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
        internal readonly AiKnowledgeIndexDocxManifestStore Store;
        internal readonly AiKnowledgeDocxModelChunks Input;
        internal AiKnowledgeIndexDispatchRecord Source;
        internal AiKnowledgeIndexDocxPlan Plan;
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
            Input = AiKnowledgeIndexDocxCodecTests.Plan(17).Source with { KnowledgeBaseId = Binding.KnowledgeBaseId, KnowledgeBaseVersion = 1, ModelConfigVersion = 1, TenantId = tenantId };
            Plan = AiKnowledgeIndexDocxPlan.Create(Input, default)!; Clock.UtcNow.Returns(Now);
            Source = new() { TaskId = TaskId, DocumentId = Binding.DocumentId, VersionId = Binding.VersionId, DocumentVersion = 1,
                KnowledgeBaseVersion = 1, LeaseToken = Lease, LeaseExpiresAtUtc = Now.AddSeconds(30), ModelConfigId = Input.ModelConfigId,
                ModelConfigVersion = 1, FileId = Guid.CreateVersion7(), ContentHash = Input.Document.ContentHash, ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document", SizeBytes = Input.Document.SizeBytes };
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
            auth.AuthorizeAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            { Assert.IsFalse(State.HasTransaction); AuthCalls++; return Denied || call.ArgAt<string>(1) == DeniedPermission ? null : new AuthorizedSessionActor(session.UserId, tenantId, session.SessionId); });
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            { Assert.IsTrue(State.HasTransaction); Writes++; if (Failure is not null) throw Failure;
                if (Unique) throw new DataCommandException(DataCommandFailureKind.UniqueConstraint, new IOException());
                if (WriteCount == 1) { var p = (IReadOnlyDictionary<string, object?>)call[1]!; Row = NewRow() with { Payload = ((byte[])p["Payload"]!).ToArray(), PayloadHash = (string)p["PayloadHash"]! }; }
                AfterWrite?.Invoke(); return WriteCount; });
            var sessions = new AiKnowledgeIndexSessionService(Query, command, new Transaction(this), State, tenant, Clock, ids, tasks, auth);
            Store = new(Query, command, new Transaction(this), State, tenant, Clock, tasks, sessions, auth);
        }
        internal AiKnowledgeIndexTextManifestRecord NewRow()
        { var bytes = AiKnowledgeIndexDocxManifestCodec.Encode(Plan, default); return new() { Id = TaskId, FormatKey = AiKnowledgeIndexDocxManifestCodec.FormatKey, Payload = bytes, PayloadHash = Convert.ToHexString(SHA256.HashData(bytes)) }; }
        internal Task<bool> Write(CancellationToken token = default) => Store.PersistAsync(Binding, TaskId, Lease, Plan, token);
        internal Task<AiKnowledgeIndexDocxPlan?> Read() => Store.ReadAsync(Binding, TaskId, Lease, default);
        internal void ChangeSource(string? contentType = null, Guid? fileId = null)
        {
            var before = Source;
            Source = new() { TaskId = before.TaskId, DocumentId = before.DocumentId, VersionId = before.VersionId,
                DocumentVersion = before.DocumentVersion, KnowledgeBaseVersion = before.KnowledgeBaseVersion,
                ModelConfigId = before.ModelConfigId, ModelConfigVersion = before.ModelConfigVersion, LeaseToken = before.LeaseToken,
                LeaseExpiresAtUtc = before.LeaseExpiresAtUtc, FileId = fileId ?? before.FileId, ContentHash = before.ContentHash,
                ContentType = contentType ?? before.ContentType, SizeBytes = before.SizeBytes };
        }
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
