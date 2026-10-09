using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Messaging;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiKnowledgeAnswerHistoryGuardTests
{
    [TestMethod] [DataRow(false, false)] [DataRow(false, true)] [DataRow(true, false)] [DataRow(true, true)]
    public async Task History_checks_all_sources_with_current_scope_and_exact_operation(bool scoped, bool model)
    {
        var f = new Fixture(scoped); var read = await f.Read(model); Assert.IsNotNull(read);
        Assert.HasCount(2, read.Dependencies); Assert.AreEqual(2, f.Queries);
        Assert.IsTrue(f.Permissions.Contains(model ? AiChatPermissions.Send : AiChatPermissions.Read));
        Assert.IsFalse(f.Permissions.Contains(model ? AiChatPermissions.Read : AiChatPermissions.Send));
        Assert.AreEqual(scoped ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, f.LastSql!.Scope);
    }
    [TestMethod] [DataRow("unreferenced")] [DataRow("referenced")] [DataRow("duplicate")]
    [DataRow("permission")] [DataRow("scope")] [DataRow("version")] [DataRow("missing")]
    public async Task History_refuses_late_change_in_every_dependency_and_identity(string fault)
    {
        var f = new Fixture(); f.AfterFirstRead = () =>
        {
            switch (fault)
            {
                case "unreferenced": f.Rows[1] = f.Rows[1] with { IsDeleted = 1 }; break;
                case "referenced": f.Rows[0] = f.Rows[0] with { CleanupPending = 1 }; break;
                case "duplicate": f.Rows[1] = f.Rows[0]; break;
                case "permission": f.Denied = AiKnowledgeDocumentPermissions.SourceRead; break;
                case "scope": f.Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "另一租户")); break;
                case "version": f.Rows[1] = f.Rows[1] with { DocumentVersion = 4 }; break;
                case "missing": f.Rows = [f.Rows[0]]; break;
            }
        };
        Assert.IsNull(await f.Read());
    }
    [TestMethod] [DataRow(AiChatPermissions.Read)] [DataRow(AiKnowledgePermissions.Read)]
    [DataRow(AiKnowledgeDocumentPermissions.Read)] [DataRow(AiKnowledgeDocumentPermissions.SourceRead)] [DataRow(AiKnowledgeDocumentPermissions.IndexRead)]
    public async Task Missing_precise_permission_prevents_source_query(string permission)
    {
        var f = new Fixture { Denied = permission }; Assert.IsNull(await f.Read()); Assert.AreEqual(0, f.Queries);
    }
    [TestMethod] [DataRow("corrupt")] [DataRow("binding")]
    public async Task Invalid_dependency_refuses_before_authority_ports(string fault)
    {
        var f = new Fixture(); f.Json = fault == "corrupt" ? null : f.Json!.Replace(f.Data.ActorUserId.ToString(), Guid.CreateVersion7().ToString());
        Assert.IsNull(await f.Read()); Assert.AreEqual(0, f.Queries); Assert.HasCount(0, f.Permissions);
    }
    [TestMethod]
    public async Task Authority_ports_are_never_called_in_a_write_transaction()
    {
        var f = new Fixture(); f.Transactions.HasTransaction.Returns(true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Read()); Assert.AreEqual(0, f.Queries);
    }
    [TestMethod]
    public async Task Model_input_requires_send_permission_before_source_query()
    {
        var f = new Fixture { Denied = AiChatPermissions.Send };
        Assert.IsNull(await f.Read(model: true)); Assert.AreEqual(0, f.Queries);
    }
    [TestMethod] [DataRow("authorization")] [DataRow("roles")] [DataRow("organizations")]
    public async Task Scope_change_after_authority_wait_prevents_next_query(string stage)
    {
        var f = new Fixture(true); f.BeforePort = current => { if (current == stage) f.Tenant.SetHost(); };
        Assert.IsNull(await f.Read()); Assert.AreEqual(0, f.Queries);
    }
    [TestMethod]
    public async Task Cancellation_does_not_deliver_late_authority_result()
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource();
        f.AfterFirstRead = () => stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Read(token: stop.Token));
    }
    [TestMethod] [DataRow("success")] [DataRow("permission")] [DataRow("scope")]
    [DataRow("owner")] [DataRow("lease")] [DataRow("revoke")] [DataRow("expiry")]
    public async Task Stored_answer_uses_complete_dependencies_and_never_reports_revoked_success(string fault)
    {
        var f = new Fixture(); var commands = Substitute.For<ICommandExecutor>(); var calls = 0; string? stored = null;
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            calls++; var values = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1);
            stored = (string)values["KnowledgeLineage"]!; Assert.AreEqual("答案", values["Content"]);
            Assert.IsFalse(values.ContainsKey("TenantId"));
            if (fault == "revoke") f.Denied = AiKnowledgeDocumentPermissions.SourceRead;
            return fault == "lease" ? 0 : 1;
        });
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var source = f.Rows[0];
        var snapshot = new AiKnowledgeRetrievalSnapshot(f.Data.ActorUserId, f.Data.TenantId, false,
            new AiModelConfigRecord { ApiKeyProtected = "protected-secret", EndpointBaseUrl = "https://private.example/v1" },
            f.Rows.Select(row => row.KnowledgeBaseId).ToArray(), f.Rows, []);
        var citation = new AiKnowledgeCitation(Guid.CreateVersion7(), new(source, 0, null, 0, 3, 1, "原始文本", ReadOnlyMemory<float>.Empty));
        var answer = new AiKnowledgeExecutedAnswer(new(snapshot, null!), new("答案", [citation], false), Guid.CreateVersion7(), Guid.CreateVersion7());
        if (fault == "permission") f.Denied = AiKnowledgePermissions.Read;
        if (fault == "scope") f.AfterFirstRead = () => f.Tenant.SetTenant(new(Guid.CreateVersion7(), "changed", "另一租户"));
        var saved = await new AiKnowledgeAnswerStore(commands, new ScalarQuery(sql => fault == "expiry" && sql.Name.Contains(".current_", StringComparison.Ordinal)
                ? Guid.Empty : f.Data.MessageId), new InlineTransaction(), f.Tenant, clock, Options.Create(new DatabaseOptions()), f.Guard).CompleteAsync(answer,
            f.Data.MessageId, f.Data.SessionId, fault == "owner" ? Guid.CreateVersion7() : f.Data.ActorUserId, default);
        Assert.AreEqual(fault == "success", saved);
        Assert.AreEqual(fault is "permission" or "scope" or "owner" ? 0 : 1, calls);
        if (stored is not null)
        {
            var lineage = AiKnowledgeAnswerLineageCodec.Decode(stored, f.Data.MessageId, f.Data.SessionId, f.Data.ActorUserId, f.Data.TenantId);
            Assert.HasCount(2, lineage.Dependencies); Assert.HasCount(1, lineage.References);
            Assert.IsFalse(stored.Contains("protected-secret")); Assert.IsFalse(stored.Contains("private.example")); Assert.IsFalse(stored.Contains("原始文本"));
        }
    }
    [TestMethod] [DataRow("missing")] [DataRow("permission")] [DataRow("scope")]
    public async Task Actual_source_proof_is_required_and_followed_by_fresh_authority(string fault)
    {
        var f = new Fixture(); f.BeforeProof = () =>
        { if (fault == "permission") f.Denied = AiKnowledgeDocumentPermissions.Read;
            if (fault == "scope") f.Tenant.SetTenant(new(Guid.CreateVersion7(), "changed", "另一租户")); };
        f.ProofReady = fault != "missing";
        Assert.IsNull(await f.Read()); Assert.IsTrue(f.ProofCalls > 0);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Batch_history_revalidates_all_messages_in_one_authority_boundary(bool lateDelete)
    {
        var f = new Fixture();
        var second = f.Data with { MessageId = Guid.CreateVersion7() };
        if (lateDelete) f.AfterFirstRead = () => f.Rows[1] = f.Rows[1] with { IsDeleted = 1 };
        var result = await f.Guard.AuthorizeBatchAsync([new(f.Data.MessageId, f.Data.SessionId, f.Json),
            new(second.MessageId, second.SessionId, AiKnowledgeAnswerLineageCodec.Encode(second))], f.Data.ActorUserId, default);
        Assert.AreEqual(lateDelete ? 0 : 2, result.Count);
        Assert.AreEqual(2, f.Queries);
    }
    [TestMethod]
    public async Task Batch_corrupt_record_does_not_hide_independent_valid_answer()
    {
        var f = new Fixture(); var invalid = Guid.CreateVersion7();
        var result = await f.Guard.AuthorizeBatchAsync([new(f.Data.MessageId, f.Data.SessionId, f.Json),
            new(invalid, f.Data.SessionId, "{}")], f.Data.ActorUserId, default);
        Assert.IsTrue(result.ContainsKey(f.Data.MessageId)); Assert.IsFalse(result.ContainsKey(invalid));
    }
    [TestMethod] [DataRow("permission")] [DataRow("scope")] [DataRow("proof")] [DataRow("duplicate")] [DataRow("overflow")]
    public async Task Batch_authority_and_bounds_fail_closed_without_truncating_dependencies(string fault)
    {
        var f = new Fixture();
        if (fault == "permission") f.BeforeProof = () => f.Denied = AiKnowledgeDocumentPermissions.SourceRead;
        if (fault == "scope") f.BeforeProof = () => f.Tenant.SetTenant(new(Guid.CreateVersion7(), "changed", "另一租户"));
        if (fault == "proof") f.ProofReady = false;
        var entries = new List<AiKnowledgeAnswerHistoryEntry> { new(f.Data.MessageId, f.Data.SessionId, f.Json) };
        if (fault == "duplicate") entries.Add(entries[0]);
        if (fault == "overflow") for (var i = 0; i < 64; i++)
        { var other = f.Data with { MessageId = Guid.CreateVersion7() }; entries.Add(new(other.MessageId, other.SessionId, AiKnowledgeAnswerLineageCodec.Encode(other))); }
        Assert.HasCount(0, await f.Guard.AuthorizeBatchAsync(entries, f.Data.ActorUserId, default));
        if (fault is "duplicate" or "overflow") Assert.AreEqual(0, f.Queries);
    }
    [TestMethod]
    public async Task Batch_shared_dependencies_are_proven_once_and_independent_answer_survives_changed_source()
    {
        var f = new Fixture(); var second = f.Data with { MessageId = Guid.CreateVersion7(), Dependencies = [f.Data.Dependencies[0]] };
        f.AfterFirstRead = () => f.Rows[1] = f.Rows[1] with { IsDeleted = 1 };
        var result = await f.Guard.AuthorizeBatchAsync([new(f.Data.MessageId, f.Data.SessionId, f.Json),
            new(second.MessageId, second.SessionId, AiKnowledgeAnswerLineageCodec.Encode(second))], f.Data.ActorUserId, default);
        Assert.IsFalse(result.ContainsKey(f.Data.MessageId)); Assert.IsTrue(result.ContainsKey(second.MessageId));
        Assert.AreEqual(2, f.ProofCalls); Assert.AreEqual(2, f.Queries);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Feedback_review_decodes_original_owner_but_proves_current_reviewer(bool scoped)
    {
        var f = new Fixture(scoped); var reviewer = Guid.CreateVersion7(); f.AuthorizingActor = reviewer;
        f.ExpectedReader = reviewer;
        var result = await f.Guard.AuthorizeFeedbackReviewBatchAsync(
            [new(f.Data.MessageId, f.Data.SessionId, f.Data.ActorUserId, f.Json)], reviewer, default);
        Assert.IsTrue(result.ContainsKey(f.Data.MessageId)); Assert.AreEqual(2, f.Queries); Assert.AreEqual(2, f.ProofCalls);
    }

    [TestMethod] [DataRow("owner")] [DataRow("permission")] [DataRow("scope")] [DataRow("proof")]
    public async Task Feedback_review_never_borrows_original_owner_authority(string fault)
    {
        var f = new Fixture(); var reviewer = Guid.CreateVersion7(); f.AuthorizingActor = reviewer; f.ExpectedReader = reviewer;
        if (fault == "permission") f.BeforeProof = () => f.Denied = AiKnowledgeDocumentPermissions.SourceRead;
        if (fault == "scope") f.BeforeProof = () => f.Tenant.SetTenant(new(Guid.CreateVersion7(), "changed", "变更范围"));
        if (fault == "proof") f.ProofReady = false;
        var result = await f.Guard.AuthorizeFeedbackReviewBatchAsync(
            [new(f.Data.MessageId, f.Data.SessionId, fault == "owner" ? reviewer : f.Data.ActorUserId, f.Json)], reviewer, default);
        Assert.HasCount(0, result);
        if (fault == "owner") Assert.AreEqual(0, f.Queries);
    }

    private sealed class Fixture
    {
        internal readonly CurrentTenantAccessor Tenant = new(); internal readonly IDataTransactionState Transactions = Substitute.For<IDataTransactionState>();
        internal AiKnowledgeAnswerLineage Data; internal AiKnowledgeIndexStatusRecord[] Rows; internal string? Json;
        internal string? Denied; internal int Queries; internal Action? AfterFirstRead; internal Action<string>? BeforePort;
        internal readonly List<string> Permissions = []; internal SqlStatement? LastSql; internal readonly AiKnowledgeAnswerHistoryGuard Guard;
        internal Action? BeforeProof; internal bool ProofReady = true; internal int ProofCalls;
        internal Guid AuthorizingActor; internal Guid? ExpectedReader;
        internal Fixture(bool scoped = false)
        {
            if (scoped) Tenant.SetTenant(new(Guid.CreateVersion7(), "tenant", "当前租户")); else Tenant.SetHost();
            Rows = [AiKnowledgeAnswerLineageTests.Source(), AiKnowledgeAnswerLineageTests.Source()];
            Rows = Rows.Select(row => row with { ActiveTaskId = row.TaskId }).ToArray();
            Data = new(1, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Tenant.Id,
                Rows.Select(AiKnowledgeAnswerLineageTests.Dependency).ToArray(),
                [new(Guid.CreateVersion7(), Rows[0].DocumentId, Rows[0].VersionId, 0, null, 0, 10)]);
            Json = AiKnowledgeAnswerLineageCodec.Encode(Data);
            AuthorizingActor = Data.ActorUserId;
            var auth = Substitute.For<ICurrentSessionAuthorization>();
            auth.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            { var p = call.ArgAt<string>(0); Permissions.Add(p); BeforePort?.Invoke("authorization");
                return p == Denied ? null : new AuthorizedSessionActor(AuthorizingActor, Tenant.Id, Guid.CreateVersion7()); });
            var roles = Substitute.For<IActiveRoleDirectory>(); var units = Substitute.For<ICurrentTenantOrganizationUnitDirectory>();
            roles.ListActiveRoleIdsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => { BeforePort?.Invoke("roles"); return Array.Empty<Guid>(); });
            units.ListActiveUnitIdsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => { BeforePort?.Invoke("organizations"); return Array.Empty<Guid>(); });
            var query = new Query(sql =>
            {
                Assert.IsFalse(Transactions.HasTransaction); Queries++; LastSql = sql;
                var captured = Rows.ToArray(); if (Queries == 1) AfterFirstRead?.Invoke(); return captured;
            });
            Guard = new(query, Transactions, Tenant, Options.Create(new DatabaseOptions()), new(roles, units, Tenant),
                new(auth, new HttpContextAccessor(), Substitute.For<ICurrentApiKeyAuthorization>()), new Proof(() =>
                { ProofCalls++; BeforeProof?.Invoke(); return ProofReady; }, () => ExpectedReader));
        }
        internal Task<AiKnowledgeAnswerLineage?> Read(bool model = false, CancellationToken token = default) =>
            Guard.AuthorizeAsync(Json, Data.MessageId, Data.SessionId, Data.ActorUserId, model, token);
    }
    private sealed class Proof(Func<bool> verify, Func<Guid?>? expectedReader = null) : IAiKnowledgeAnswerSourceProof
    {
        public Task<bool> VerifyAsync(AiKnowledgeIndexStatusRecord source, Dictionary<string, object?> parameters, CancellationToken token)
        {
            if (expectedReader?.Invoke() is { } actor) Assert.AreEqual(actor, parameters["OwnerUserId"]);
            return Task.FromResult(verify());
        }
    }
    private sealed class ScalarQuery(Func<SqlStatement, Guid> read) : IQueryExecutor
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult((T?)(object)read(statement));
    }
    private sealed class InlineTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => action(cancellationToken);
    }
    // 显式替身避免动态代理为 internal 投影创建不可访问的自动返回类型。
    private sealed class Query(Func<SqlStatement, AiKnowledgeIndexStatusRecord[]> read) : IQueryExecutor
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<T>)(object)read(statement));
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
