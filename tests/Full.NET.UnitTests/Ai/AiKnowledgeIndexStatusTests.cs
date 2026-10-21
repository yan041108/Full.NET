using System.Text.Json;
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
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>索引元数据独立权限、等待后撤权及可信作用域；完整证明另由真实激活消费者和双库覆盖。</summary>
[TestClass]
public sealed class AiKnowledgeIndexStatusTests
{
    [TestMethod] [DataRow(false, "pending_processing")] [DataRow(true, "pending_processing")]
    [DataRow(false, "queued")] [DataRow(true, "processing")] [DataRow(false, "failed")]
    [DataRow(false, "deleting")] [DataRow(true, "deleted")]
    public async Task States_use_minimal_contract_and_current_reader(bool tenant, string state)
    {
        var f = new Fixture(tenant);
        f.Row = f.Row! with { TaskId = state == "pending_processing" ? null : Guid.CreateVersion7(),
            StateKey = state == "processing" ? "running" : state, IsDeleted = state is "deleting" or "deleted" ? 1 : 0,
            CleanupPending = state == "deleting" ? 1 : 0 };
        var result = await f.Run(); Assert.IsTrue(result.IsSuccess); Assert.AreEqual(state, result.Value!.State);
        Assert.AreEqual(state == "failed" ? "ai.knowledge.index_unavailable" : null, result.Value.FailureCode);
        var context = new AiJsonSerializerContext(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, context.AiKnowledgeIndexStatusResponse));
        CollectionAssert.AreEquivalent(new[] { "versionId", "taskId", "state", "failureCode" }, json.RootElement.EnumerateObject().Select(x => x.Name).ToArray());
        Assert.AreEqual(2, f.Reads); Assert.AreEqual(2, f.RoleReads); Assert.AreEqual(tenant ? 2 : 0, f.OrgReads);
    }

    [TestMethod] [DataRow("ai.knowledge_bases.read")] [DataRow("ai.knowledge_documents.read")]
    [DataRow("ai.knowledge_documents.index_read")]
    public async Task Each_permission_is_required_before_query(string permission)
    {
        var f = new Fixture { Denied = permission }; var result = await f.Run();
        Assert.AreEqual(ErrorType.Forbidden, result.Error!.Type); Assert.AreEqual(0, f.Reads);
    }

    [TestMethod] [DataRow("revoked")] [DataRow("scope")] [DataRow("relationship")] [DataRow("cancelled")]
    [DataRow("transaction")]
    public async Task Waiting_for_source_rechecks_authority_scope_and_cancellation(string fault)
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.AfterRead = () =>
        {
            if (fault == "revoked") f.Denied = AiKnowledgeDocumentPermissions.IndexRead;
            if (fault == "scope") f.Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "其他"));
            if (fault == "relationship") f.Row = null;
            if (fault == "cancelled") cancellation.Cancel();
            if (fault == "transaction") f.Transactions.HasTransaction.Returns(true);
        };
        if (fault == "cancelled") await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Run(cancellation.Token));
        else if (fault == "transaction") await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Run());
        else Assert.AreEqual(fault == "relationship" ? ErrorType.NotFound : ErrorType.Forbidden, (await f.Run()).Error!.Type);
    }

    [TestMethod] [DataRow("stale")] [DataRow("no_pointer")] [DataRow("wrong_pointer")]
    [DataRow("empty_lease")] [DataRow("missing_proof")]
    public async Task Completed_label_alone_never_means_ready(string fault)
    {
        var f = new Fixture(); var task = Guid.CreateVersion7();
        f.Row = f.Row! with { TaskId = task, StateKey = "completed", IsCurrent = fault == "stale" ? 0 : 1,
            ActiveTaskId = fault == "no_pointer" ? null : fault == "wrong_pointer" ? Guid.CreateVersion7() : task,
            ActiveLeaseToken = fault == "empty_lease" ? Guid.Empty : Guid.CreateVersion7() };
        var result = await f.Run(); Assert.IsTrue(result.IsSuccess); Assert.AreEqual("failed", result.Value!.State);
    }

    [TestMethod] [DataRow("initial_cancel")] [DataRow("initial_transaction")] [DataRow("query_fault")]
    public async Task Cancellation_transactions_and_infrastructure_failures_propagate(string fault)
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        if (fault == "initial_cancel") { cancellation.Cancel(); await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Run(cancellation.Token)); }
        else if (fault == "initial_transaction") { f.Transactions.HasTransaction.Returns(true); await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Run()); }
        else { f.AfterRead = () => throw new IOException("database unavailable"); await Assert.ThrowsExactlyAsync<IOException>(() => f.Run()); }
    }

    [TestMethod] [DataRow("permission")] [DataRow("relationship")] [DataRow("metadata")]
    public async Task Proof_wait_rechecks_current_reader_and_resource_before_returning(string change)
    {
        var f = new Fixture(); var task = Guid.CreateVersion7();
        f.Row = f.Row! with { TaskId = task, StateKey = "completed", ActiveTaskId = task, ActiveLeaseToken = Guid.CreateVersion7() };
        f.AfterProof = () =>
        {
            if (change == "permission") f.Denied = AiKnowledgeDocumentPermissions.IndexRead;
            else if (change == "relationship") f.Row = null;
            else f.Row = f.Row! with { KnowledgeBaseVersion = 2 };
        };
        var result = await f.Run(); Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(change == "permission" ? ErrorType.Forbidden : change == "relationship" ? ErrorType.NotFound : ErrorType.Conflict, result.Error!.Type);
    }

    private sealed class Fixture : IQueryExecutor
    {
        internal readonly CurrentTenantAccessor Tenant = new();
        internal readonly IDataTransactionState Transactions = Substitute.For<IDataTransactionState>();
        internal readonly Guid Actor = Guid.CreateVersion7(), Knowledge = Guid.CreateVersion7(), Document = Guid.CreateVersion7(), Version = Guid.CreateVersion7();
        internal AiKnowledgeIndexStatusRecord? Row;
        internal string? Denied;
        internal Action? AfterRead;
        internal Action? AfterProof;
        internal int Reads, RoleReads, OrgReads;
        private readonly AiKnowledgeIndexStatusService service;
        internal Fixture(bool scoped = false)
        {
            if (scoped) Tenant.SetTenant(new(Guid.CreateVersion7(), "test", "测试")); else Tenant.SetHost();
            Row = new() { KnowledgeBaseId = Knowledge, DocumentId = Document, VersionId = Version, OwnerUserId = Guid.CreateVersion7(),
                DocumentVersion = 1, KnowledgeBaseVersion = 1, IsCurrent = 1, ContentType = "text/plain" };
            var auth = Substitute.For<ICurrentSessionAuthorization>(); auth.UsePermissionSetChecks();
            auth.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            { Assert.IsFalse(Transactions.HasTransaction); return call.ArgAt<string>(0) == Denied ? null : new AuthorizedSessionActor(Actor, Tenant.Id, Guid.CreateVersion7()); });
            var roles = Substitute.For<IActiveRoleDirectory>(); var orgs = Substitute.For<ICurrentTenantOrganizationUnitDirectory>();
            roles.ListActiveRoleIdsForUserAsync(Actor, Arg.Any<CancellationToken>()).Returns(_ => { Assert.IsFalse(Transactions.HasTransaction); RoleReads++; return Array.Empty<Guid>(); });
            orgs.ListActiveUnitIdsForUserAsync(Actor, Arg.Any<CancellationToken>()).Returns(_ => { Assert.IsFalse(Transactions.HasTransaction); OrgReads++; return Array.Empty<Guid>(); });
            var command = Substitute.For<ICommandExecutor>(); var clock = Substitute.For<IClock>(); var ids = Substitute.For<IIdGenerator>();
            var options = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
            var activation = new AiKnowledgeIndexActivationStore(this, command, Substitute.For<ICommandTransaction>(), Transactions, Tenant, clock,
                ids, new(this, command, Tenant, clock, ids), Substitute.For<IBackgroundSessionAuthorization>(), options);
            service = new(this, Transactions, Tenant, new(roles, orgs, Tenant), new(auth, Substitute.For<IHttpContextAccessor>(), Substitute.For<ICurrentApiKeyAuthorization>()), activation, options);
        }
        internal Task<Result<AiKnowledgeIndexStatusResponse>> Run(CancellationToken token = default) => service.ReadAsync(Knowledge, Document, Version, Actor, token);
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(AiKnowledgeIndexActivationRecord)) { AfterProof?.Invoke(); return Task.FromResult(default(T)); }
            Assert.AreEqual(typeof(AiKnowledgeIndexStatusRecord), typeof(T)); Assert.IsFalse(Transactions.HasTransaction);
            Assert.AreEqual(Tenant.IsHost ? SqlDataScope.HostOnly : SqlDataScope.TenantRequired, sql.Scope);
            var p = (Dictionary<string, object?>)parameters!; Assert.AreEqual(Actor, p["OwnerUserId"]); Assert.AreEqual(Version, p["VersionId"]);
            Reads++; var row = Row; AfterRead?.Invoke(); return Task.FromResult((T?)(object?)row);
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default) => throw new AssertFailedException("Status is an exact version lookup.");
    }
}
