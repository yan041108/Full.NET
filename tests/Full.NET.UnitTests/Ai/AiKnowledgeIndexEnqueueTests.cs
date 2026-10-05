using System.Security.Claims;
using System.Text.Json;
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
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实原子会话服务配合执行器 Port，验证公开入队不接受身份或代次覆盖。</summary>
[TestClass]
public sealed class AiKnowledgeIndexEnqueueTests
{
    [TestMethod] [DataRow(false, "refresh")] [DataRow(true, "refresh")]
    [DataRow(false, "oidc-application")] [DataRow(true, "oidc-application")]
    public async Task Enqueue_derives_versions_and_preserves_original_session(bool scoped, string kind)
    {
        var f = new Fixture(scoped, kind);
        var result = await f.Run(); Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(f.Task, result.Value!.TaskId); Assert.AreEqual(f.Version, result.Value.VersionId);
        Assert.AreEqual(2, f.Writes); Assert.AreEqual(2, f.SourceReads);
        Assert.AreEqual(f.Session.SessionId, f.Persisted!.SessionId);
        var json = JsonSerializer.Serialize(result.Value, AiJsonSerializerContext.Default.AiKnowledgeIndexEnqueueResponse);
        using var document = JsonDocument.Parse(json); Assert.AreEqual(2, document.RootElement.EnumerateObject().Count());
        Assert.IsFalse(json.Contains(f.Session.SecurityStamp, StringComparison.Ordinal));
    }

    [TestMethod] [DataRow("ai.knowledge_bases.read")] [DataRow("ai.knowledge_documents.read")]
    [DataRow("ai.knowledge_documents.upload")] [DataRow("ai.knowledge_documents.source_read")]
    [DataRow("ai.knowledge_documents.index_enqueue")]
    public async Task Every_permission_denies_before_resource_query(string permission)
    {
        var f = new Fixture { Denied = permission }; var result = await f.Run();
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.Forbidden, result.Error!.Type);
        Assert.AreEqual(0, f.SourceReads); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("user")] [DataRow("tenant")] [DataRow("session")] [DataRow("kind")]
    [DataRow("stamp")] [DataRow("actor")] [DataRow("scope")]
    public async Task Invalid_frozen_identity_never_reads_or_writes(string field)
    {
        var f = new Fixture();
        f.Session = field switch
        {
            "user" => f.Session with { UserId = Guid.Empty }, "tenant" => f.Session with { TenantId = Guid.CreateVersion7() },
            "session" => f.Session with { SessionId = Guid.Empty }, "kind" => f.Session with { SessionKind = "api-key" },
            "stamp" => f.Session with { SecurityStamp = new string('x', 129) }, "actor" => f.Session with { ActorScope = " " },
            _ => f.Session with { EffectiveScope = new string('x', 65) },
        };
        Assert.IsFalse((await f.Run()).IsSuccess); Assert.AreEqual(0, f.SourceReads); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("null")] [DataRow("owner")] [DataRow("version")] [DataRow("document_version")]
    [DataRow("knowledge_version")] [DataRow("model")] [DataRow("model_version")]
    public async Task Missing_or_malformed_authority_never_creates_task(string field)
    {
        var f = new Fixture();
        f.Source = field switch
        {
            "null" => null, "owner" => f.Source! with { OwnerUserId = Guid.CreateVersion7() },
            "version" => f.Source! with { VersionId = Guid.CreateVersion7() }, "document_version" => f.Source! with { DocumentVersion = 0 },
            "knowledge_version" => f.Source! with { KnowledgeBaseVersion = 0 }, "model" => f.Source! with { ModelConfigId = Guid.Empty },
            _ => f.Source! with { ModelConfigVersion = 0 },
        };
        Assert.IsFalse((await f.Run()).IsSuccess); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("text/plain")] [DataRow("text/markdown")] [DataRow("application/pdf")]
    public async Task Only_worker_supported_text_formats_can_enqueue(string type)
    {
        var f = new Fixture(); f.Source = f.Source! with { ContentType = type };
        var result = await f.Run(); Assert.AreEqual(type != "application/pdf", result.IsSuccess);
        Assert.AreEqual(type == "application/pdf" ? 0 : 2, f.Writes);
    }

    [TestMethod] [DataRow("scope")] [DataRow("revoked")] [DataRow("cancelled")] [DataRow("transaction")]
    public async Task Authority_wait_rechecks_before_first_write(string field)
    {
        using var cancellation = new CancellationTokenSource(); var f = new Fixture();
        f.AfterSource = _ =>
        {
            if (field == "scope") f.Tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "other", "其他"));
            if (field == "revoked") f.Denied = AiKnowledgeDocumentPermissions.IndexEnqueue;
            if (field == "cancelled") cancellation.Cancel();
            if (field == "transaction") f.State.HasTransaction.Returns(true);
        };
        if (field == "cancelled") await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Run(cancellation.Token));
        else if (field == "transaction") await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Run());
        else Assert.IsFalse((await f.Run()).IsSuccess);
        Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("scope")] [DataRow("revoked")] [DataRow("source")]
    public async Task Final_proof_refuses_success_after_local_commit_drift(string field)
    {
        var f = new Fixture(); f.AfterCommit = () =>
        {
            if (field == "scope") f.Tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "other", "其他"));
            if (field == "revoked") f.Denied = AiKnowledgeDocumentPermissions.IndexEnqueue;
            if (field == "source") f.Source = f.Source! with { KnowledgeBaseVersion = 2 };
        };
        Assert.IsFalse((await f.Run()).IsSuccess); Assert.AreEqual(2, f.Writes);
    }

    [TestMethod]
    public async Task Final_authorization_wait_rechecks_source_before_success()
    {
        var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.AtAuthorization = call =>
        {
            if (call != 25) return System.Threading.Tasks.Task.CompletedTask;
            entered.SetResult(); return release.Task;
        };
        var running = f.Run();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Source = f.Source! with { KnowledgeBaseVersion = 5 };
        release.SetResult();
        var result = await running;
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.Conflict, result.Error!.Type);
        Assert.AreEqual(2, f.Writes); Assert.IsNotNull(f.Persisted);
    }

    [TestMethod] [DataRow("cancelled")] [DataRow("transaction")] [DataRow("infrastructure")]
    public async Task Cancellation_transaction_and_query_faults_propagate(string field)
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        if (field == "cancelled") { cancellation.Cancel(); await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Run(cancellation.Token)); }
        else if (field == "transaction") { f.State.HasTransaction.Returns(true); await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Run()); }
        else { f.AfterSource = _ => throw new IOException("source failed"); await Assert.ThrowsExactlyAsync<IOException>(() => f.Run()); }
        Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("refresh")] [DataRow("oidc-application")] [DataRow("FullNET.ApiKey")]
    [DataRow("FullNET.Signature")] [DataRow("api-key-claim")] [DataRow("empty-session")]
    public void Http_binding_uses_authenticated_claims_and_trusted_tenant(string kind)
    {
        var user = Guid.CreateVersion7(); var session = Guid.CreateVersion7(); var tenant = Guid.CreateVersion7();
        List<Claim> claims = [new("sub", user.ToString()), new(FullNetIdentityClaimTypes.SecurityStamp, "stamp"),
            new(FullNetIdentityClaimTypes.ActorScope, "host"), new(FullNetIdentityClaimTypes.Scope, "trusted"),
            new(kind == "oidc-application" ? FullNetIdentityClaimTypes.ApplicationSessionId : FullNetIdentityClaimTypes.SessionId,
                (kind == "empty-session" ? Guid.Empty : session).ToString())];
        if (kind == "oidc-application") claims.Add(new(FullNetIdentityClaimTypes.TokenUse, "access"));
        if (kind == "api-key-claim") claims.Add(new(FullNetIdentityClaimTypes.ApiKeyId, Guid.CreateVersion7().ToString()));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, kind)) };
        var valid = AiKnowledgeIndexHttpBinding.TryCreate(context, tenant, out var binding);
        Assert.AreEqual(kind is "refresh" or "oidc-application", valid);
        if (valid) { Assert.AreEqual(user, binding.UserId); Assert.AreEqual(tenant, binding.TenantId); Assert.AreEqual(session, binding.SessionId); Assert.AreEqual(kind, binding.SessionKind); }
    }

    [TestMethod] [DataRow("malformed")] [DataRow("wrong-token-use")]
    public void Oidc_claims_cannot_fall_back_to_another_refresh_session(string fault)
    {
        List<Claim> claims = [new("sub", Guid.CreateVersion7().ToString()), new(FullNetIdentityClaimTypes.SessionId, Guid.CreateVersion7().ToString()),
            new(FullNetIdentityClaimTypes.ApplicationSessionId, fault == "malformed" ? "bad" : Guid.CreateVersion7().ToString()),
            new(FullNetIdentityClaimTypes.TokenUse, fault == "malformed" ? "access" : "id"), new(FullNetIdentityClaimTypes.SecurityStamp, "stamp"),
            new(FullNetIdentityClaimTypes.ActorScope, "host"), new(FullNetIdentityClaimTypes.Scope, "host")];
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) };
        Assert.IsFalse(AiKnowledgeIndexHttpBinding.TryCreate(context, null, out _));
    }

    private sealed class Fixture : IQueryExecutor
    {
        internal readonly CurrentTenantAccessor Tenant = new();
        internal readonly IDataTransactionState State = Substitute.For<IDataTransactionState>();
        internal readonly Guid Knowledge = Guid.CreateVersion7(), Document = Guid.CreateVersion7(), Version = Guid.CreateVersion7(), Task = Guid.CreateVersion7();
        internal SessionBindingSnapshot Session;
        internal AiKnowledgeIndexEnqueueRecord? Source;
        internal AiKnowledgeIndexSessionRecord? Persisted;
        internal string? Denied;
        internal int SourceReads, Writes;
        internal Action<int>? AfterSource;
        internal Action? AfterCommit;
        internal Func<int, Task>? AtAuthorization;
        private int authorizations;
        private readonly AiKnowledgeIndexEnqueueService service;
        internal Fixture(bool scoped = false, string kind = "refresh")
        {
            Guid? tenant = scoped ? Guid.CreateVersion7() : null;
            if (tenant is { } id) Tenant.SetTenant(new TenantContext(id, "test", "测试")); else Tenant.SetHost();
            Session = new(Guid.CreateVersion7(), tenant, Guid.CreateVersion7(), "sensitive-stamp", "host", "trusted", kind);
            Source = new() { KnowledgeBaseId = Knowledge, DocumentId = Document, VersionId = Version, OwnerUserId = Session.UserId,
                DocumentVersion = 3, KnowledgeBaseVersion = 4, ModelConfigId = Guid.CreateVersion7(), ModelConfigVersion = 5, ContentType = "text/plain" };
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(Task, Guid.CreateVersion7());
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var authorization = Substitute.For<IBackgroundSessionAuthorization>();
            authorization.AuthorizeAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                Assert.IsFalse(State.HasTransaction); var count = ++authorizations;
                if (AtAuthorization is not null) await AtAuthorization(count);
                return call.ArgAt<string>(1) == Denied ? null : new AuthorizedSessionActor(Session.UserId, Session.TenantId, Session.SessionId);
            });
            var command = Substitute.For<ICommandExecutor>();
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Assert.IsTrue(State.HasTransaction); Writes++;
                var p = (Dictionary<string, object?>)call[1]!; Assert.AreEqual(3, p["DocumentVersion"]); Assert.AreEqual(4, p["KnowledgeBaseVersion"]);
                Persisted = new() { Id = Guid.CreateVersion7(), IndexTaskId = Task, ActorUserId = Session.UserId, TenantId = Session.TenantId,
                    SessionId = Session.SessionId, SessionKind = Session.SessionKind, SecurityStamp = Session.SecurityStamp, ActorScope = Session.ActorScope, EffectiveScope = Session.EffectiveScope };
                return 1;
            });
            var sessions = new AiKnowledgeIndexSessionService(this, command, new Transaction(this), State, Tenant, clock, ids,
                new(this, command, Tenant, clock, ids), authorization);
            service = new(this, State, Tenant, sessions, authorization);
        }
        internal Task<Result<AiKnowledgeIndexEnqueueResponse>> Run(CancellationToken token = default) => service.EnqueueAsync(Knowledge, Document, Version, Session, token);
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Assert.IsFalse(State.HasTransaction);
            if (typeof(T) == typeof(AiKnowledgeIndexSessionRecord)) return System.Threading.Tasks.Task.FromResult((T?)(object?)Persisted);
            Assert.AreEqual(typeof(AiKnowledgeIndexEnqueueRecord), typeof(T)); SourceReads++; var row = Source;
            Assert.AreEqual(Tenant.IsHost ? SqlDataScope.HostOnly : SqlDataScope.TenantRequired, statement.Scope);
            AfterSource?.Invoke(SourceReads);
            return System.Threading.Tasks.Task.FromResult((T?)(object?)row);
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => throw new AssertFailedException("Enqueue has no list query.");
        private sealed class Transaction(Fixture f) : ICommandTransaction
        {
            public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
            {
                f.State.HasTransaction.Returns(true);
                try { return await action(cancellationToken); }
                finally { f.State.HasTransaction.Returns(false); f.AfterCommit?.Invoke(); }
            }
        }
    }
}
