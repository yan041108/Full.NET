using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>新代次与旧任务分离；授权只在事务外，未知写入结果通过相同请求键恢复。</summary>
[TestClass]
public sealed class AiKnowledgeIndexRebuildTests
{
    [TestMethod] [DataRow(false, "refresh")] [DataRow(true, "refresh")]
    [DataRow(false, "oidc-application")] [DataRow(true, "oidc-application")]
    public async Task Creates_new_task_and_session_without_modifying_old_task(bool scoped, string kind)
    {
        var f = new Fixture(scoped, kind); var result = await f.Run();
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(f.NewTask, result.Value!.TaskId);
        Assert.AreNotEqual(f.Request.ExpectedTaskId, result.Value.TaskId);
        Assert.AreEqual(2, f.Writes); Assert.AreEqual(1, f.Commits); Assert.AreEqual(0, f.Rollbacks);
        Assert.AreEqual(f.Request.ClientRequestId, f.Written!["RebuildRequestId"]);
        Assert.AreEqual(f.Request.ExpectedTaskId, f.Written["ExpectedTaskId"]);
    }

    [TestMethod]
    public async Task Same_request_replay_returns_created_task_without_another_write()
    {
        var f = new Fixture(); Assert.IsTrue((await f.Run()).IsSuccess);
        var replay = await f.Run(); Assert.IsTrue(replay.IsSuccess);
        Assert.AreEqual(f.NewTask, replay.Value!.TaskId); Assert.AreEqual(2, f.Writes);
    }

    [TestMethod] [DataRow("ai.knowledge_bases.read")] [DataRow("ai.knowledge_documents.read")]
    [DataRow("ai.knowledge_documents.source_read")] [DataRow("ai.knowledge_documents.upload")]
    [DataRow("ai.knowledge_documents.index_enqueue")] [DataRow("ai.knowledge_documents.index_read")]
    [DataRow("ai.knowledge_documents.index_rebuild")]
    public async Task Every_permission_is_required_before_reading_resources(string permission)
    {
        var f = new Fixture { Denied = permission }; var result = await f.Run();
        Assert.AreEqual(ErrorType.Forbidden, result.Error!.Type); Assert.AreEqual(0, f.Reads); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("request")] [DataRow("previous")] [DataRow("document")]
    [DataRow("knowledge")] [DataRow("confirmation")]
    public async Task Invalid_or_unconfirmed_request_has_no_effect(string field)
    {
        var f = new Fixture(); f.Request = field switch
        {
            "request" => f.Request with { ClientRequestId = Guid.Empty },
            "previous" => f.Request with { ExpectedTaskId = Guid.Empty },
            "document" => f.Request with { ExpectedDocumentVersion = 0 },
            "knowledge" => f.Request with { ExpectedKnowledgeBaseVersion = 0 },
            _ => f.Request with { ConfirmAdditionalCost = false }
        };
        Assert.AreEqual(ErrorType.Validation, (await f.Run()).Error!.Type); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    public async Task Disabled_by_default_does_not_admit_work()
    {
        var f = new Fixture(); f.Options.Enabled = false;
        Assert.IsFalse((await f.Run()).IsSuccess); Assert.AreEqual(0, f.Reads); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("version")] [DataRow("knowledge")]
    public async Task Stale_snapshot_does_not_enter_write_transaction(string field)
    {
        var f = new Fixture(); f.Source = field == "version" ? f.Source with { DocumentVersion = 4 } : f.Source with { KnowledgeBaseVersion = 5 };
        Assert.AreEqual(ErrorType.Conflict, (await f.Run()).Error!.Type); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    public async Task Locked_source_drift_rejects_without_inserting()
    {
        var f = new Fixture { DriftWhileLocking = true };
        Assert.AreEqual(ErrorType.Conflict, (await f.Run()).Error!.Type); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    public async Task Unknown_ledger_or_nonterminal_previous_task_rejects_conditionally()
    {
        var f = new Fixture { CreateCount = 0 };
        Assert.AreEqual(ErrorType.Conflict, (await f.Run()).Error!.Type);
        Assert.AreEqual(1, f.Writes); Assert.IsNull(f.Persisted);
    }

    [TestMethod]
    public async Task Failed_second_write_rolls_back_new_task()
    {
        var f = new Fixture { BindCount = 0 };
        Assert.AreEqual(ErrorType.Conflict, (await f.Run()).Error!.Type);
        Assert.AreEqual(1, f.Rollbacks); Assert.IsNull(f.Persisted); Assert.IsNull(f.Admitted);
    }

    [TestMethod]
    public async Task Existing_request_cannot_be_adopted_by_another_login()
    {
        var f = new Fixture(); Assert.IsTrue((await f.Run()).IsSuccess);
        f.Session = f.Session with { SessionId = Guid.CreateVersion7() };
        Assert.AreEqual(ErrorType.Conflict, (await f.Run()).Error!.Type); Assert.AreEqual(2, f.Writes);
    }

    [TestMethod]
    public async Task Revocation_after_commit_denies_success_without_erasing_saved_binding()
    {
        var f = new Fixture(); f.AfterCommit = () => f.Denied = AiKnowledgeDocumentPermissions.IndexRebuild;
        Assert.AreEqual(ErrorType.Forbidden, (await f.Run()).Error!.Type); Assert.IsNotNull(f.Persisted);
    }

    [TestMethod]
    public async Task Cancellation_and_ambient_transaction_are_not_downgraded()
    {
        var f = new Fixture(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Run(cancel.Token));
        f.State.HasTransaction.Returns(true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Run()); Assert.AreEqual(0, f.Writes);
    }

    private sealed class Fixture : IQueryExecutor
    {
        internal readonly Guid Knowledge = Guid.CreateVersion7(), Document = Guid.CreateVersion7(), Version = Guid.CreateVersion7(), NewTask = Guid.CreateVersion7();
        internal readonly IDataTransactionState State = Substitute.For<IDataTransactionState>();
        internal readonly AiKnowledgeIndexRebuildOptions Options = new() { Enabled = true };
        internal SessionBindingSnapshot Session;
        internal AiKnowledgeIndexRebuildRequest Request = new(Guid.CreateVersion7(), Guid.CreateVersion7(), 3, 4, true);
        internal AiKnowledgeIndexEnqueueRecord Source;
        internal AiKnowledgeIndexTaskRecord? Admitted;
        internal AiKnowledgeIndexSessionRecord? Persisted;
        internal Dictionary<string, object?>? Written;
        internal int Writes, Reads, Commits, Rollbacks, CreateCount = 1, BindCount = 1;
        internal bool DriftWhileLocking;
        internal string? Denied;
        internal Action? AfterCommit;
        private readonly AiKnowledgeIndexRebuildService service;
        internal Fixture(bool scoped = false, string kind = "refresh")
        {
            var tenant = new CurrentTenantAccessor(); Guid? tenantId = scoped ? Guid.CreateVersion7() : null;
            if (tenantId is { } id) tenant.SetTenant(new(id, "test", "测试")); else tenant.SetHost();
            Session = new(Guid.CreateVersion7(), tenantId, Guid.CreateVersion7(), "stamp", "host", "trusted", kind);
            Source = new() { KnowledgeBaseId = Knowledge, DocumentId = Document, VersionId = Version, OwnerUserId = Session.UserId,
                DocumentVersion = 3, KnowledgeBaseVersion = 4, ModelConfigId = Guid.CreateVersion7(), ModelConfigVersion = 1, ContentType = "text/plain" };
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(NewTask, Guid.CreateVersion7());
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var auth = Substitute.For<IBackgroundSessionAuthorization>();
            auth.AuthorizeAllAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Assert.IsFalse(State.HasTransaction);
                return call.ArgAt<IReadOnlyCollection<string>>(1).Contains(Denied!) ? null : new AuthorizedSessionActor(Session.UserId, Session.TenantId, Session.SessionId);
            });
            var command = Substitute.For<ICommandExecutor>();
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Assert.IsTrue(State.HasTransaction); Writes++; Written = new((Dictionary<string, object?>)call[1]!);
                if (call.ArgAt<SqlStatement>(0).Name.StartsWith("ai.knowledge_index_rebuild.create_", StringComparison.Ordinal))
                {
                    if (CreateCount == 1) Admitted = new() { Id = NewTask, DocumentId = Document, VersionId = Version, DocumentVersion = 3,
                        KnowledgeBaseVersion = 4, ModelConfigId = Source.ModelConfigId, ModelConfigVersion = 1, StateKey = "queued" };
                    return CreateCount;
                }
                if (BindCount == 1) Persisted = new() { Id = Guid.CreateVersion7(), IndexTaskId = NewTask, ActorUserId = Session.UserId,
                    TenantId = Session.TenantId, SessionId = Session.SessionId, SessionKind = Session.SessionKind, SecurityStamp = Session.SecurityStamp,
                    ActorScope = Session.ActorScope, EffectiveScope = Session.EffectiveScope };
                return BindCount;
            });
            service = new(this, command, new Transaction(this), State, tenant, clock, ids, auth, Microsoft.Extensions.Options.Options.Create(Options),
                Microsoft.Extensions.Options.Options.Create(new DatabaseOptions()), Microsoft.Extensions.Options.Options.Create(new AiKnowledgePdfRuntimeOptions()),
                Microsoft.Extensions.Options.Options.Create(new AiKnowledgeDocxRuntimeOptions()));
        }
        internal Task<Result<AiKnowledgeIndexEnqueueResponse>> Run(CancellationToken token = default) => service.RebuildAsync(Knowledge, Document, Version, Request, Session, token);
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Reads++; object? row = typeof(T) == typeof(AiKnowledgeIndexEnqueueRecord)
                ? State.HasTransaction && DriftWhileLocking ? Source with { ModelConfigVersion = 2 } : Source
                : typeof(T) == typeof(AiKnowledgeIndexTaskRecord) ? Admitted : Persisted;
            return Task.FromResult((T?)row);
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => throw new AssertFailedException("重建不扫描列表。");
        private sealed class Transaction(Fixture f) : ICommandTransaction
        {
            public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
            {
                var oldTask = f.Admitted; var oldSession = f.Persisted; f.State.HasTransaction.Returns(true);
                try { var result = await action(cancellationToken); f.Commits++; return result; }
                catch { f.Rollbacks++; f.Admitted = oldTask; f.Persisted = oldSession; throw; }
                finally { f.State.HasTransaction.Returns(false); f.AfterCommit?.Invoke(); }
            }
        }
    }
}
