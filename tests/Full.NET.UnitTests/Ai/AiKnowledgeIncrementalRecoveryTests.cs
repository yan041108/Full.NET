using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>恢复只补本范围最新来源意图；权限等待、分页与重复不能隐式创建收费工作。</summary>
[TestClass]
public sealed class AiKnowledgeIncrementalRecoveryTests
{
    [TestMethod]
    [DataRow(false, "normal")]
    [DataRow(true, "normal")]
    [DataRow(false, "existing")]
    [DataRow(true, "existing")]
    [DataRow(false, "empty")]
    [DataRow(true, "empty")]
    [DataRow(false, "deny")]
    [DataRow(true, "deny")]
    [DataRow(false, "revoke")]
    [DataRow(true, "revoke")]
    [DataRow(false, "scope")]
    [DataRow(true, "scope")]
    [DataRow(false, "missing")]
    [DataRow(true, "missing")]
    public async Task Recovery_rechecks_authority_and_limits_writes_to_the_returned_page(bool scoped, string scenario)
    {
        var tenant = new CurrentTenantAccessor();
        if (scoped) tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "acme", "Acme")); else tenant.SetHost();
        var knowledge = Guid.CreateVersion7(); var actor = Guid.CreateVersion7(); var read = false;
        var rows = Enumerable.Range(0, 3).Select(_ => new AiKnowledgeSourceConfirmed(knowledge, Guid.CreateVersion7(), Guid.CreateVersion7(), tenant.Id)).ToArray();
        if (scenario == "empty") rows = [];
        var query = new RecoveryQuery(scenario != "missing", rows, () =>
        {
            read = true;
            if (scenario == "scope") tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "other", "Other"));
        });
        var auth = Substitute.For<ICurrentSessionAuthorization>(); auth.UsePermissionSetChecks();
        auth.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            scenario == "deny" || (scenario == "revoke" && read) ? null : new AuthorizedSessionActor(actor, tenant.Id, Guid.CreateVersion7()));
        var guard = new AiKnowledgePermissionGuard(auth, Substitute.For<IHttpContextAccessor>(), Substitute.For<ICurrentApiKeyAuthorization>());
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            scenario != "existing" && call.Arg<SqlStatement>()!.Name.Contains("insert_", StringComparison.Ordinal) ? 1 : 0);
        var tx = Substitute.For<ICommandTransaction>();
        tx.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>()).Returns(call => (call.Arg<Func<CancellationToken, Task<bool>>>() ?? throw new InvalidOperationException("Expected transaction callback."))(call.Arg<CancellationToken>()));
        var writer = new AiKnowledgeIncrementalIndexWriter(query, command, tx, tenant, Options.Create(new DatabaseOptions()), Substitute.For<IClock>(), Substitute.For<IIdGenerator>());
        var service = new AiKnowledgeIncrementalRecoveryService(query, Substitute.For<IDataTransactionState>(), writer, tenant, guard, Options.Create(new DatabaseOptions()));
        var result = await service.RecoverAsync(knowledge, actor, new(null, 2), CancellationToken.None);
        if (scenario is not ("normal" or "existing" or "empty"))
        {
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(scenario == "missing" ? "ai.knowledge.not_found" : "authorization.permission_denied", result.Error!.Code);
            Assert.AreEqual(0, command.ReceivedCalls().Count());
        }
        else
        {
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(scenario == "empty" ? 0 : 2, result.Value!.Scanned);
            Assert.AreEqual(scenario is "existing" or "empty" ? 0 : 2, result.Value.Updated);
            Assert.AreEqual(scenario == "empty" ? null : rows[1].DocumentId, result.Value.NextDocumentId);
            Assert.AreEqual(scenario == "empty" ? 0 : 4, command.ReceivedCalls().Count());
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(51)]
    [DataRow(-1)]
    public async Task Recovery_rejects_unbounded_pages_without_database_work(int size)
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetHost();
        var query = new RecoveryQuery(true, [], () => Assert.Fail("Query must not run."));
        var service = new AiKnowledgeIncrementalRecoveryService(query, Substitute.For<IDataTransactionState>(), null!, tenant, null!, Options.Create(new DatabaseOptions()));
        Assert.IsFalse((await service.RecoverAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), new(null, size), CancellationToken.None)).IsSuccess);
    }

    [TestMethod]
    public async Task Recovery_honors_cancellation_and_rejects_ambient_transactions()
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetHost();
        var state = Substitute.For<IDataTransactionState>(); state.HasTransaction.Returns(true);
        var service = new AiKnowledgeIncrementalRecoveryService(null!, state, null!, tenant, null!, Options.Create(new DatabaseOptions()));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.RecoverAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), new(), CancellationToken.None));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.RecoverAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), new(), cancel.Token));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Writer_rolls_back_before_writes_when_scope_changes_during_document_lock(bool scoped)
    {
        var tenant = new CurrentTenantAccessor();
        if (scoped) tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "acme", "Acme")); else tenant.SetHost();
        var source = new AiKnowledgeSourceConfirmed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), tenant.Id);
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>(); var tx = Substitute.For<ICommandTransaction>();
        query.QuerySingleOrDefaultAsync<int>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        { tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "other", "Other")); return 1; });
        tx.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>()).Returns(call => (call.Arg<Func<CancellationToken, Task<bool>>>() ?? throw new InvalidOperationException("Expected transaction callback."))(call.Arg<CancellationToken>()));
        var writer = new AiKnowledgeIncrementalIndexWriter(query, command, tx, tenant, Options.Create(new DatabaseOptions()), Substitute.For<IClock>(), Substitute.For<IIdGenerator>());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => writer.ApplyAsync(source, CancellationToken.None));
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    // 使用具体集合而非动态代理，保持内部事件类型的生产可见性不变。
    private sealed class RecoveryQuery(bool owned, AiKnowledgeSourceConfirmed[] rows, Action onRead) : IQueryExecutor
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        { onRead(); return Task.FromResult((IReadOnlyList<T>)(object)rows); }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult((T?)(object)(statement.Name.Contains("owner_", StringComparison.Ordinal) ? (owned ? 1 : 0) : 1));
    }
}
