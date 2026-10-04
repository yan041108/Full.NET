using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>会话冻结是恢复执行的身份来源，事务失败不能留下未绑定任务。</summary>
[TestClass]
public sealed class AiKnowledgeIndexSessionTests
{
    [TestMethod] [DataRow(false, "refresh")] [DataRow(true, "refresh")]
    [DataRow(false, "oidc-application")] [DataRow(true, "oidc-application")]
    public async Task Creation_freezes_trusted_session_in_one_transaction(bool tenant, string kind)
    {
        var f = new Fixture(tenant, kind); Assert.AreEqual(f.TaskId, await f.Service.EnqueueAsync(f.Binding, f.Session, default));
        Assert.AreEqual(2, f.Writes); Assert.AreEqual(1, f.Commits); Assert.AreEqual(0, f.Rollbacks);
        Assert.IsTrue(f.PermissionCalls >= 8); Assert.IsNotNull(f.Row);
        Assert.AreEqual(f.Session, await f.Service.ReadHeldAsync(f.Binding, f.TaskId, f.Lease, default));
    }

    [TestMethod] [DataRow("owner")] [DataRow("tenant")] [DataRow("session")] [DataRow("kind")]
    [DataRow("stamp")] [DataRow("actor")] [DataRow("effective")]
    public async Task Invalid_or_untrusted_binding_never_writes(string changed)
    {
        var f = new Fixture(false);
        var session = changed switch
        {
            "owner" => f.Session with { UserId = Guid.CreateVersion7() }, "tenant" => f.Session with { TenantId = Guid.CreateVersion7() },
            "session" => f.Session with { SessionId = Guid.Empty }, "kind" => f.Session with { SessionKind = "api-key" },
            "stamp" => f.Session with { SecurityStamp = new string('x', 129) }, "actor" => f.Session with { ActorScope = " " },
            _ => f.Session with { EffectiveScope = new string('x', 65) },
        };
        Assert.IsNull(await f.Service.EnqueueAsync(f.Binding, session, default)); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("ai.knowledge_bases.read")] [DataRow("ai.knowledge_documents.read")]
    [DataRow("ai.knowledge_documents.source_read")] [DataRow("ai.knowledge_documents.upload")]
    public async Task Each_required_permission_denies_creation(string permission)
    {
        var f = new Fixture(false) { DeniedPermission = permission };
        Assert.IsNull(await f.Service.EnqueueAsync(f.Binding, f.Session, default)); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    public async Task Zero_task_write_does_not_insert_binding_or_commit_partial_task()
    {
        var f = new Fixture(false) { TaskWrite = 0 };
        Assert.IsNull(await f.Service.EnqueueAsync(f.Binding, f.Session, default)); Assert.AreEqual(1, f.Writes);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Failed_second_write_rolls_back_task(bool infrastructure)
    {
        var f = new Fixture(false) { SessionWrite = 0, InfrastructureFailure = infrastructure };
        if (infrastructure) await Assert.ThrowsExactlyAsync<IOException>(() => f.Service.EnqueueAsync(f.Binding, f.Session, default));
        else await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.EnqueueAsync(f.Binding, f.Session, default));
        Assert.AreEqual(0, f.Commits); Assert.AreEqual(1, f.Rollbacks); Assert.AreEqual(2, f.Writes);
    }

    [TestMethod] [DataRow("same")] [DataRow("session")] [DataRow("stamp")] [DataRow("kind")]
    [DataRow("actor")] [DataRow("effective")] [DataRow("legacy")]
    public async Task Unique_race_reuses_only_identical_persisted_binding(string changed)
    {
        var f = new Fixture(false) { UniqueRace = true }; f.Row = f.NewRow();
        f.Row = changed switch
        {
            "session" => f.Row with { SessionId = Guid.CreateVersion7() }, "stamp" => f.Row with { SecurityStamp = "new" },
            "kind" => f.Row with { SessionKind = SessionBindingKinds.OidcApplication }, "actor" => f.Row with { ActorScope = "other" },
            "effective" => f.Row with { EffectiveScope = "other" }, "legacy" => null, _ => f.Row,
        };
        Assert.AreEqual(changed == "same" ? f.TaskId : (Guid?)null, await f.Service.EnqueueAsync(f.Binding, f.Session, default));
        Assert.AreEqual(1, f.Writes); Assert.AreEqual(1, f.Rollbacks);
    }

    [TestMethod] [DataRow("permission")] [DataRow("source")]
    public async Task Authority_lost_during_commit_cannot_return_task(string changed)
    {
        var f = new Fixture(false); f.AfterCommit = () => { if (changed == "source") f.Row = null; else f.DeniedPermission = "ai.knowledge_bases.read"; };
        Assert.IsNull(await f.Service.EnqueueAsync(f.Binding, f.Session, default)); Assert.AreEqual(1, f.Commits);
    }

    [TestMethod] [DataRow("expired")] [DataRow("missing")] [DataRow("owner")] [DataRow("task")] [DataRow("tenant")]
    public async Task Held_read_rejects_missing_foreign_or_expired_binding(string changed)
    {
        var f = new Fixture(false); f.Row = f.NewRow();
        if (changed == "expired") f.Clock.UtcNow.Returns(f.Row.LeaseExpiresAtUtc!.Value);
        else f.Row = changed switch { "missing" => null, "owner" => f.Row with { ActorUserId = Guid.CreateVersion7() },
            "task" => f.Row with { IndexTaskId = Guid.CreateVersion7() }, _ => f.Row with { TenantId = Guid.CreateVersion7() } };
        Assert.IsNull(await f.Service.ReadHeldAsync(f.Binding, f.TaskId, f.Lease, default));
    }

    [TestMethod]
    public async Task Ambient_transaction_rejects_before_identity_or_sql()
    {
        var f = new Fixture(false); f.State.HasTransaction.Returns(true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.EnqueueAsync(f.Binding, f.Session, default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.ReadHeldAsync(f.Binding, f.TaskId, f.Lease, default));
        Assert.AreEqual(0, f.PermissionCalls); Assert.IsFalse(f.Query.ReceivedCalls().Any()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    public async Task Held_read_rechecks_clock_after_query_wait()
    {
        var f = new Fixture(false); f.Row = f.NewRow();
        f.Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        { f.Clock.UtcNow.Returns(f.Row.LeaseExpiresAtUtc!.Value); return f.Row; });
        Assert.IsNull(await f.Service.ReadHeldAsync(f.Binding, f.TaskId, f.Lease, default));
    }

    [TestMethod]
    public async Task Cancelled_held_query_cannot_return_frozen_identity()
    {
        var f = new Fixture(false); using var cancel = new CancellationTokenSource(); f.Row = f.NewRow();
        f.Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { cancel.Cancel(); return f.Row; });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Service.ReadHeldAsync(f.Binding, f.TaskId, f.Lease, cancel.Token));
    }

    private sealed class Fixture
    {
        internal readonly IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        internal readonly IDataTransactionState State = Substitute.For<IDataTransactionState>();
        internal readonly IClock Clock = Substitute.For<IClock>();
        internal readonly Guid TaskId = Guid.CreateVersion7(), Lease = Guid.CreateVersion7();
        internal readonly AiKnowledgeIndexTaskBinding Binding = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
        internal readonly SessionBindingSnapshot Session;
        internal readonly AiKnowledgeIndexSessionService Service;
        internal AiKnowledgeIndexSessionRecord? Row;
        internal string? DeniedPermission;
        internal int Writes, Commits, Rollbacks, PermissionCalls, TaskWrite = 1, SessionWrite = 1;
        internal bool UniqueRace, InfrastructureFailure;
        internal Action? AfterCommit;
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;
        internal Fixture(bool scoped, string kind = "refresh")
        {
            var tenant = new CurrentTenantAccessor(); Guid? tenantId = scoped ? Guid.CreateVersion7() : null;
            if (tenantId is { } id) tenant.SetTenant(new TenantContext(id, "test", "测试")); else tenant.SetHost();
            Session = new(Binding.OwnerUserId, tenantId, Guid.CreateVersion7(), "stamp", "host", tenantId is { } value ? $"tenant:{value:N}" : "host", kind);
            var command = Substitute.For<ICommandExecutor>(); var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(TaskId, Guid.CreateVersion7());
            Clock.UtcNow.Returns(now); var auth = Substitute.For<IBackgroundSessionAuthorization>();
            auth.AuthorizeAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Assert.IsFalse(State.HasTransaction); PermissionCalls++;
                return (string)call[1]! == DeniedPermission ? null : new AuthorizedSessionActor(Session.UserId, Session.TenantId, Session.SessionId);
            });
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Row);
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Assert.IsTrue(State.HasTransaction); Writes++;
                if (((SqlStatement)call[0]!).Name.Contains("index_task", StringComparison.Ordinal))
                {
                    if (UniqueRace) throw new DataCommandException(DataCommandFailureKind.UniqueConstraint, new IOException());
                    return TaskWrite;
                }
                if (InfrastructureFailure) throw new IOException("second write failed");
                if (SessionWrite == 1) Row = NewRow(); return SessionWrite;
            });
            Service = new(Query, command, new Transaction(this), State, tenant, Clock, ids, new(Query, command, tenant, Clock, ids), auth);
        }
        internal AiKnowledgeIndexSessionRecord NewRow() => new() { Id = Guid.CreateVersion7(), IndexTaskId = TaskId, ActorUserId = Session.UserId,
            TenantId = Session.TenantId, SessionId = Session.SessionId, SessionKind = Session.SessionKind, SecurityStamp = Session.SecurityStamp,
            ActorScope = Session.ActorScope, EffectiveScope = Session.EffectiveScope, CreatedAtUtc = now, LeaseExpiresAtUtc = now.AddSeconds(30) };
        private sealed class Transaction(Fixture fixture) : ICommandTransaction
        {
            public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
            {
                fixture.State.HasTransaction.Returns(true);
                try { var result = await action(cancellationToken); fixture.Commits++; return result; }
                catch { fixture.Rollbacks++; throw; }
                finally { fixture.State.HasTransaction.Returns(false); fixture.AfterCommit?.Invoke(); }
            }
        }
    }
}
