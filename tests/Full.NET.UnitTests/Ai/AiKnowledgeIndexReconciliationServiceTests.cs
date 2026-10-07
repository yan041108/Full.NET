using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiKnowledgeIndexReconciliationServiceTests
{
    [TestMethod]
    [DataRow("queued", "queued", "queued", "wait")]
    [DataRow("running", "processing", "processing", "wait")]
    [DataRow("expired", "processing", "lease_expired", "wait")]
    [DataRow("exhausted", "processing", "lease_exhausted", "retry")]
    [DataRow("failed", "failed", "task_failed", "retry")]
    [DataRow("cancelled", "failed", "task_cancelled", "rebuild")]
    [DataRow("completed", "failed", "generation_unavailable", "rebuild")]
    public async Task Persisted_tasks_produce_bounded_hints_without_writes(string task, string state, string reason, string action)
    {
        var f = new Fixture(); f.SetTask(task); var result = await f.Run();
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(state, result.Value!.State);
        Assert.AreEqual(reason, result.Value.Reason); Assert.AreEqual(action, result.Value.RecoveryAction);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
    }

    [TestMethod] [DataRow("lease")] [DataRow("updated")] [DataRow("missing")]
    public async Task Task_changes_during_the_last_query_do_not_publish_a_recovery_hint(string fault)
    {
        var f = new Fixture(); f.SetTask("running");
        f.AfterTask = () =>
        {
            if (f.TaskReads != 2) return;
            if (fault == "missing") f.StoredTask = null;
            else f.SetTask("running", fault == "lease", fault == "updated");
        };
        Assert.AreEqual(ErrorType.Conflict, (await f.Run()).Error!.Type);
    }

    [TestMethod]
    public async Task An_unavailable_source_does_not_recommend_enqueue()
    {
        var f = new Fixture { SourceMissing = true }; var result = await f.Run();
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual("source_unavailable", result.Value!.Reason);
        Assert.AreEqual("none", result.Value.RecoveryAction);
    }

    [TestMethod] [DataRow("queued")] [DataRow("running")] [DataRow("failed")]
    public async Task A_task_without_current_frozen_authority_does_not_suggest_wait_or_original_session_retry(string state)
    {
        var f = new Fixture(); f.SetTask(state); f.Row = f.Row! with { IsCurrent = 0 };
        var result = await f.Run(); Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("failed", result.Value!.State); Assert.AreEqual("generation_unavailable", result.Value.Reason);
        Assert.AreEqual("rebuild", result.Value.RecoveryAction);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Owner_reconciles_current_source_without_writing_or_dispatching(bool scoped)
    {
        var f = new Fixture(scoped); var result = await f.Run();
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual("not_indexed", result.Value!.Reason);
        Assert.AreEqual("enqueue", result.Value.RecoveryAction); Assert.AreEqual(1, result.Value.DocumentVersion);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count()); Assert.AreEqual(4, f.SourceReads);
    }

    [TestMethod] [DataRow("owner")] [DataRow("missing")]
    public async Task Other_owners_and_missing_resources_are_indistinguishable(string fault)
    {
        var f = new Fixture(); f.Row = fault == "missing" ? null : f.Row! with { OwnerUserId = Guid.CreateVersion7() };
        Assert.AreEqual(ErrorType.NotFound, (await f.Run()).Error!.Type); Assert.AreEqual(0, f.SourceReads);
    }

    [TestMethod] [DataRow("permission")] [DataRow("scope")] [DataRow("metadata")]
    public async Task Authority_or_metadata_changes_after_the_proof_do_not_publish_hints(string fault)
    {
        var f = new Fixture();
        if (fault == "permission") f.AfterSource = () => { if (f.SourceReads == 2) f.Denied = AiKnowledgeDocumentPermissions.IndexRead; };
        else f.AfterRow = () =>
        {
            if (f.RowReads != 4) return;
            if (fault == "scope") f.Tenant.SetTenant(new(Guid.CreateVersion7(), "changed", "变化"));
            else f.Row = f.Row! with { DocumentVersion = 2 };
        };
        Assert.AreEqual(fault == "metadata" ? ErrorType.Conflict : ErrorType.Forbidden, (await f.Run()).Error!.Type);
    }

    [TestMethod] [DataRow("lease")] [DataRow("attempts")] [DataRow("metadata")]
    public async Task Changes_during_the_last_permission_wait_are_detected(string fault)
    {
        var f = new Fixture(); f.SetTask("running");
        f.AfterAuthorization = permission =>
        {
            if (permission != AiKnowledgeDocumentPermissions.IndexRead || f.TaskReads != 1) return;
            if (fault == "metadata") f.Row = f.Row! with { DocumentVersion = 2 };
            else f.SetTask(fault == "attempts" ? "exhausted" : "running", fault == "lease");
        };
        Assert.AreEqual(ErrorType.Conflict, (await f.Run()).Error!.Type);
    }

    [TestMethod] [DataRow("permission")] [DataRow("transaction")] [DataRow("cancel")]
    public async Task Entry_guards_stop_reads_before_any_query(string fault)
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        if (fault == "permission") { f.Denied = AiKnowledgePermissions.Read; Assert.AreEqual(ErrorType.Forbidden, (await f.Run()).Error!.Type); }
        else if (fault == "transaction") { f.Transactions.HasTransaction.Returns(true); await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run()); }
        else { cancellation.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => f.Run(cancellation.Token)); }
        Assert.AreEqual(0, f.RowReads);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Infrastructure_faults_and_late_cancellation_propagate(bool cancelled)
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.AfterSource = () => { if (cancelled) cancellation.Cancel(); else throw new IOException("database unavailable"); };
        if (cancelled) await Assert.ThrowsAsync<OperationCanceledException>(() => f.Run(cancellation.Token));
        else await Assert.ThrowsAsync<IOException>(() => f.Run());
    }

    private sealed class Fixture : IQueryExecutor
    {
        internal readonly CurrentTenantAccessor Tenant = new();
        internal readonly IDataTransactionState Transactions = Substitute.For<IDataTransactionState>();
        internal readonly ICommandExecutor Command = Substitute.For<ICommandExecutor>();
        internal readonly Guid Actor = Guid.CreateVersion7(), Knowledge = Guid.CreateVersion7(), Document = Guid.CreateVersion7(), Version = Guid.CreateVersion7();
        internal AiKnowledgeIndexStatusRecord? Row;
        internal AiKnowledgeIndexTaskRecord? StoredTask;
        internal bool SourceMissing;
        internal string? Denied;
        internal Action? AfterRow, AfterSource, AfterTask;
        internal Action<string>? AfterAuthorization;
        internal int RowReads, SourceReads, TaskReads;
        private readonly AiKnowledgeIndexReconciliationService service;
        internal Fixture(bool scoped = false)
        {
            if (scoped) Tenant.SetTenant(new(Guid.CreateVersion7(), "test", "测试")); else Tenant.SetHost();
            var model = Guid.CreateVersion7();
            Row = new() { KnowledgeBaseId = Knowledge, DocumentId = Document, VersionId = Version, OwnerUserId = Actor,
                DocumentVersion = 1, KnowledgeBaseVersion = 1, ContentType = "text/plain", ModelConfigId = model, ModelConfigVersion = 1 };
            var auth = Substitute.For<ICurrentSessionAuthorization>();
            auth.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Assert.IsFalse(Transactions.HasTransaction); var permission = call.ArgAt<string>(0);
                var result = permission == Denied ? null : new AuthorizedSessionActor(Actor, Tenant.Id, Guid.CreateVersion7());
                AfterAuthorization?.Invoke(permission); return result;
            });
            var roles = Substitute.For<IActiveRoleDirectory>(); var orgs = Substitute.For<ICurrentTenantOrganizationUnitDirectory>();
            roles.ListActiveRoleIdsForUserAsync(Actor, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
            orgs.ListActiveUnitIdsForUserAsync(Actor, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
            var access = new AiKnowledgeReadAccess(roles, orgs, Tenant);
            var guard = new AiKnowledgePermissionGuard(auth, Substitute.For<IHttpContextAccessor>(), Substitute.For<ICurrentApiKeyAuthorization>());
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.Parse("2026-10-07T00:00:00Z"));
            var ids = Substitute.For<IIdGenerator>(); var options = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
            var activation = new AiKnowledgeIndexActivationStore(this, Command, Substitute.For<ICommandTransaction>(), Transactions, Tenant, clock,
                ids, new(this, Command, Tenant, clock, ids), Substitute.For<IBackgroundSessionAuthorization>(), options);
            var statuses = new AiKnowledgeIndexStatusService(this, Transactions, Tenant, access, guard, activation, options);
            service = new(this, Transactions, Tenant, access, guard, statuses, clock, options, Options.Create(new AiKnowledgePdfRuntimeOptions()));
        }
        internal Task<Result<AiKnowledgeIndexReconciliationResponse>> Run(CancellationToken token = default) => service.ReadAsync(Knowledge, Document, Version, Actor, token);
        internal void SetTask(string state, bool changeLease = false, bool changeUpdated = false)
        {
            var now = DateTimeOffset.Parse("2026-10-07T00:00:00Z");
            var id = Row!.TaskId ?? Guid.CreateVersion7(); var key = state is "expired" or "exhausted" ? "running" : state;
            Row = Row with { TaskId = id, StateKey = key, IsCurrent = 1 };
            StoredTask = new() { Id = id, DocumentId = Document, VersionId = Version, DocumentVersion = 1, KnowledgeBaseVersion = 1,
                ModelConfigId = Row.ModelConfigId!.Value, ModelConfigVersion = 1, StateKey = key, AttemptCount = state == "exhausted" ? 3 : 1,
                LeaseToken = StoredTask is null || changeLease ? Guid.CreateVersion7() : StoredTask.LeaseToken,
                LeaseExpiresAtUtc = state is "expired" or "exhausted" ? now.AddMinutes(-1) : now.AddMinutes(5),
                CreatedAtUtc = now, UpdatedAtUtc = changeUpdated ? now.AddSeconds(1) : now };
        }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Assert.IsFalse(Transactions.HasTransaction);
            Assert.AreEqual(Tenant.IsHost ? SqlDataScope.HostOnly : SqlDataScope.TenantRequired, sql.Scope);
            var p = (Dictionary<string, object?>)parameters!; Assert.AreEqual(Actor, p["OwnerUserId"]); Assert.AreEqual(Version, p["VersionId"]);
            if (typeof(T) == typeof(AiKnowledgeIndexStatusRecord)) { RowReads++; AfterRow?.Invoke(); return Task.FromResult((T?)(object?)Row); }
            if (typeof(T) == typeof(AiKnowledgeIndexTaskRecord)) { Assert.AreEqual(Row!.TaskId, p["TaskId"]); TaskReads++; AfterTask?.Invoke(); return Task.FromResult((T?)(object?)StoredTask); }
            Assert.AreEqual(typeof(AiKnowledgeIndexEnqueueRecord), typeof(T));
            SourceReads++; AfterSource?.Invoke();
            if (SourceMissing) return Task.FromResult<T?>(default);
            return Task.FromResult((T?)(object?)new AiKnowledgeIndexEnqueueRecord { KnowledgeBaseId = Knowledge, DocumentId = Document,
                VersionId = Version, OwnerUserId = Actor, DocumentVersion = 1, KnowledgeBaseVersion = 1,
                ModelConfigId = Row!.ModelConfigId!.Value, ModelConfigVersion = 1, ContentType = "text/plain" });
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default) => throw new AssertFailedException("Reconciliation is bounded to one exact version.");
    }
}
