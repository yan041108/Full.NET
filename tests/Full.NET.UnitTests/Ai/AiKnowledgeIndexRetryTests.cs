using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>重试只恢复原任务；等待后的授权、会话和来源漂移必须失败关闭。</summary>
[TestClass]
public sealed partial class AiKnowledgeIndexRetryTests
{
    [TestMethod] [DataRow(false, "refresh")] [DataRow(true, "refresh")]
    [DataRow(false, "oidc-application")] [DataRow(true, "oidc-application")]
    public async Task Retry_uses_current_source_and_exact_original_session(bool scoped, string kind)
    {
        var f = new Fixture(scoped, kind); var result = await f.Run();
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(f.TaskId, result.Value!.TaskId);
        Assert.AreEqual(f.Version, result.Value.VersionId); Assert.AreEqual(1, f.Writes);
        Assert.AreEqual(2, f.SourceReads); Assert.AreEqual(18, f.Authorizations);
    }

    [TestMethod] [DataRow("ai.knowledge_bases.read")] [DataRow("ai.knowledge_documents.read")]
    [DataRow("ai.knowledge_documents.source_read")] [DataRow("ai.knowledge_documents.upload")]
    [DataRow("ai.knowledge_documents.index_enqueue")] [DataRow("ai.knowledge_documents.index_retry")]
    public async Task Every_permission_denies_without_resource_access(string permission)
    {
        var f = new Fixture { Denied = permission }; var result = await f.Run();
        Assert.AreEqual(ErrorType.Forbidden, result.Error!.Type); Assert.AreEqual(0, f.Writes); Assert.AreEqual(0, f.SourceReads);
    }

    [TestMethod] [DataRow("missing")] [DataRow("id")] [DataRow("task")] [DataRow("user")]
    [DataRow("tenant")] [DataRow("session")] [DataRow("kind")] [DataRow("stamp")]
    [DataRow("actor")] [DataRow("scope")]
    public async Task Frozen_binding_cannot_be_replaced(string fault)
    {
        var f = new Fixture(); f.Persisted = fault switch
        {
            "missing" => null, "id" => f.Persisted! with { Id = Guid.Empty },
            "task" => f.Persisted! with { IndexTaskId = Guid.CreateVersion7() },
            "user" => f.Persisted! with { ActorUserId = Guid.CreateVersion7() },
            "tenant" => f.Persisted! with { TenantId = Guid.CreateVersion7() },
            "session" => f.Persisted! with { SessionId = Guid.CreateVersion7() },
            "kind" => f.Persisted! with { SessionKind = "oidc-application" },
            "stamp" => f.Persisted! with { SecurityStamp = f.Session.SecurityStamp.ToUpperInvariant() },
            "actor" => f.Persisted! with { ActorScope = "other" },
            _ => f.Persisted! with { EffectiveScope = "other" },
        };
        Assert.AreEqual(ErrorType.Conflict, (await f.Run()).Error!.Type); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("scope")] [DataRow("revoked")] [DataRow("cancelled")] [DataRow("transaction")]
    public async Task Session_query_wait_rechecks_before_write(string fault)
    {
        using var cancelled = new CancellationTokenSource(); var f = new Fixture();
        f.AfterSession = () =>
        {
            if (fault == "scope") f.Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "其他"));
            if (fault == "revoked") f.Denied = "ai.knowledge_documents.index_retry";
            if (fault == "cancelled") cancelled.Cancel();
            if (fault == "transaction") f.State.HasTransaction.Returns(true);
        };
        if (fault == "cancelled") await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Run(cancelled.Token));
        else if (fault == "transaction") await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Run());
        else Assert.IsFalse((await f.Run()).IsSuccess);
        Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    public async Task Final_authorization_wait_rechecks_source()
    {
        var f = new Fixture(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.AtAuthorization = count => { if (count != 18) return Task.CompletedTask; entered.SetResult(); return release.Task; };
        var request = f.Run(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Source = f.Source! with { ModelConfigVersion = 2 }; release.SetResult();
        Assert.AreEqual(ErrorType.Conflict, (await request).Error!.Type); Assert.AreEqual(1, f.Writes);
    }

    [TestMethod] [DataRow("source")] [DataRow("scope")] [DataRow("revoked")]
    public async Task Write_wait_drift_never_reports_success(string fault)
    {
        var f = new Fixture(); f.AfterWrite = () =>
        {
            if (fault == "source") f.Source = null;
            if (fault == "scope") f.Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "其他"));
            if (fault == "revoked") f.Denied = "ai.knowledge_documents.index_retry";
        };
        Assert.IsFalse((await f.Run()).IsSuccess); Assert.AreEqual(1, f.Writes);
    }

    [TestMethod]
    public async Task Lost_condition_returns_conflict_and_database_failure_propagates()
    {
        var f = new Fixture { Affected = 0 }; Assert.AreEqual(ErrorType.Conflict, (await f.Run()).Error!.Type);
        var failed = new Fixture { WriteFailure = true };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => failed.Run());
    }

    [TestMethod] [DataRow("user")] [DataRow("session")] [DataRow("tenant")] [DataRow("kind")]
    [DataRow("stamp")] [DataRow("actor")] [DataRow("scope")]
    public async Task Malformed_identity_never_reads_or_writes(string fault)
    {
        var f = new Fixture(); f.Session = fault switch
        {
            "user" => f.Session with { UserId = Guid.Empty }, "session" => f.Session with { SessionId = Guid.Empty },
            "tenant" => f.Session with { TenantId = Guid.CreateVersion7() }, "kind" => f.Session with { SessionKind = "api-key" },
            "stamp" => f.Session with { SecurityStamp = new string('s', 129) },
            "actor" => f.Session with { ActorScope = " " }, _ => f.Session with { EffectiveScope = new string('s', 65) },
        };
        Assert.AreEqual(ErrorType.Forbidden, (await f.Run()).Error!.Type); Assert.AreEqual(0, f.SourceReads); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("missing")] [DataRow("owner")] [DataRow("version")] [DataRow("model")]
    public async Task Invalid_source_never_writes(string fault)
    {
        var f = new Fixture(); f.Source = fault switch
        {
            "missing" => null, "owner" => f.Source! with { OwnerUserId = Guid.CreateVersion7() },
            "version" => f.Source! with { DocumentVersion = 0 }, _ => f.Source! with { ModelConfigId = Guid.Empty },
        };
        Assert.AreEqual(ErrorType.NotFound, (await f.Run()).Error!.Type); Assert.AreEqual(0, f.Writes);
    }

    private sealed class Fixture : IQueryExecutor
    {
        internal readonly CurrentTenantAccessor Tenant = new();
        internal readonly AiKnowledgePdfRuntimeOptions Pdf = new();
        internal readonly IDataTransactionState State = Substitute.For<IDataTransactionState>();
        internal readonly Guid Knowledge = Guid.CreateVersion7(), Document = Guid.CreateVersion7(), Version = Guid.CreateVersion7(), TaskId = Guid.CreateVersion7();
        internal SessionBindingSnapshot Session;
        internal AiKnowledgeIndexEnqueueRecord? Source;
        internal AiKnowledgeIndexSessionRecord? Persisted;
        internal string? Denied;
        internal int Writes, SourceReads, Authorizations, Affected = 1;
        internal bool WriteFailure;
        internal Action? AfterWrite, AfterSession;
        internal Func<int, Task>? AtAuthorization;
        private readonly AiKnowledgeIndexRetryService service;
        internal Fixture(bool scoped = false, string kind = "refresh")
        {
            Guid? tenant = scoped ? Guid.CreateVersion7() : null;
            if (tenant is { } id) Tenant.SetTenant(new(id, "test", "测试")); else Tenant.SetHost();
            Session = new(Guid.CreateVersion7(), tenant, Guid.CreateVersion7(), "sensitive-stamp", "host", "trusted", kind);
            Source = new() { KnowledgeBaseId = Knowledge, DocumentId = Document, VersionId = Version, OwnerUserId = Session.UserId,
                DocumentVersion = 3, KnowledgeBaseVersion = 4, ModelConfigId = Guid.CreateVersion7(), ModelConfigVersion = 5, ContentType = "text/plain" };
            Persisted = new() { Id = Guid.CreateVersion7(), IndexTaskId = TaskId, ActorUserId = Session.UserId, TenantId = Session.TenantId,
                SessionId = Session.SessionId, SessionKind = Session.SessionKind, SecurityStamp = Session.SecurityStamp, ActorScope = Session.ActorScope, EffectiveScope = Session.EffectiveScope };
            var ids = Substitute.For<IIdGenerator>(); var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var authorization = Substitute.For<IBackgroundSessionAuthorization>();
            authorization.AuthorizeAllAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                Assert.IsFalse(State.HasTransaction); foreach (var permission in call.ArgAt<IReadOnlyCollection<string>>(1)) {
                    var count = ++Authorizations;
                    if (AtAuthorization is not null) await AtAuthorization(count);
                }
                return call.ArgAt<IReadOnlyCollection<string>>(1).Contains(Denied!) ? null : new AuthorizedSessionActor(Session.UserId, Session.TenantId, Session.SessionId);
            });
            var command = Substitute.For<ICommandExecutor>();
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Assert.IsFalse(State.HasTransaction); Writes++;
                var p = (Dictionary<string, object?>)call[1]!;
                Assert.AreEqual(Pdf.Enabled ? 1 : 0, p["PdfEnabled"]);
                StringAssert.Contains(call.ArgAt<SqlStatement>(0).Text, "application/pdf");
                StringAssert.Contains(call.ArgAt<SqlStatement>(0).Text, "@PdfEnabled=1");
                Assert.AreEqual(3, p["DocumentVersion"]); Assert.AreEqual(4, p["KnowledgeBaseVersion"]);
                Assert.AreEqual(Persisted!.Id, p["SessionRecordId"]); Assert.AreEqual(TaskId, p["TaskId"]);
                Assert.AreEqual(Tenant.IsHost ? SqlDataScope.HostOnly : SqlDataScope.TenantRequired, call.ArgAt<SqlStatement>(0).Scope);
                if (WriteFailure) throw new InvalidOperationException("测试数据库故障");
                AfterWrite?.Invoke(); return Affected;
            });
            service = new(this, command, State, Tenant, clock, new AiKnowledgeIndexTaskStore(this, command, Tenant, clock, ids), authorization, Microsoft.Extensions.Options.Options.Create(Pdf));
        }
        internal Task<Result<AiKnowledgeIndexEnqueueResponse>> Run(CancellationToken token = default) => service.RetryAsync(Knowledge, Document, Version, TaskId, Session, token);
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Assert.IsFalse(State.HasTransaction);
            Assert.AreEqual(Tenant.IsHost ? SqlDataScope.HostOnly : SqlDataScope.TenantRequired, statement.Scope);
            if (typeof(T) == typeof(AiKnowledgeIndexSessionRecord)) { var row = Persisted; AfterSession?.Invoke(); return Task.FromResult((T?)(object?)row); }
            Assert.AreEqual(typeof(AiKnowledgeIndexEnqueueRecord), typeof(T)); SourceReads++; return Task.FromResult((T?)(object?)Source);
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => throw new AssertFailedException("重试不扫描列表。");
    }
}
