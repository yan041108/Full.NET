using System.Data;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>执行器边界约束真实租户、唯一键恢复、有界令牌和取消传播。</summary>
[TestClass]
public sealed class AiKnowledgeIndexTaskTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Duplicate_binding_recovers_only_classified_unique_constraint(bool tenantScope)
    {
        var (store, query, command, _, _) = Create(tenantScope);
        var expected = new AiKnowledgeIndexTaskRecord { Id = Guid.CreateVersion7(), StateKey = "queued" };
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<int>(new DataCommandException(DataCommandFailureKind.UniqueConstraint, new IOException("collision"))));
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTaskRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(expected);
        Assert.AreSame(expected, await store.EnqueueAsync(Binding(), CancellationToken.None));
        var call = command.ReceivedCalls().Single();
        var sql = (SqlStatement)call.GetArguments()[0]!;
        Assert.AreEqual(tenantScope ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, sql.Scope);
        Assert.AreEqual(tenantScope ? SqlTenantBinding.CurrentTenantId : SqlTenantBinding.None, sql.TenantBinding);
        Assert.IsFalse(((Dictionary<string, object?>)call.GetArguments()[1]!).ContainsKey("TenantId"));
    }

    [TestMethod]
    public async Task Unclassified_database_failure_propagates()
    {
        var (store, _, command, _, _) = Create(false);
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromException<int>(new IOException("unavailable")));
        await Assert.ThrowsExactlyAsync<IOException>(() => store.EnqueueAsync(Binding(), CancellationToken.None));
    }

    [TestMethod]
    public async Task Missing_authority_does_not_reuse_old_task()
    {
        var (store, query, command, _, _) = Create(false);
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        Assert.IsNull(await store.EnqueueAsync(Binding(), CancellationToken.None));
        Assert.IsFalse(query.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Acquire_uses_fresh_uuid_and_thirty_second_deadline_and_forwards_cancellation()
    {
        var (store, query, command, clock, ids) = Create(true);
        var now = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(now);
        var expectedToken = Guid.CreateVersion7();
        ids.NewId().Returns(expectedToken);
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTaskRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiKnowledgeIndexTaskRecord { StateKey = "running", LeaseToken = expectedToken, LeaseExpiresAtUtc = now.AddSeconds(30) });
        using var cancellation = new CancellationTokenSource();
        var row = await store.AcquireAsync(Binding(), Guid.CreateVersion7(), cancellation.Token);
        Assert.IsNotNull(row);
        var args = command.ReceivedCalls().Single().GetArguments();
        var parameters = (Dictionary<string, object?>)args[1]!;
        Assert.AreEqual(expectedToken, parameters["LeaseToken"]);
        Assert.AreEqual(now.AddSeconds(30), parameters["LeaseExpiresAtUtc"]);
        Assert.AreEqual(cancellation.Token, args[2]);
    }

    [TestMethod]
    public async Task Cancelled_claim_cannot_return_a_running_lease()
    {
        var (store, query, command, _, _) = Create(false);
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTaskRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new AiKnowledgeIndexTaskRecord { StateKey = "cancelled" });
        Assert.IsNull(await store.AcquireAsync(Binding(), Guid.CreateVersion7(), CancellationToken.None));
    }

    [TestMethod]
    public async Task Unresolved_tenant_context_fails_before_sql()
    {
        var (_, query, command, clock, ids) = Create(false);
        var store = new AiKnowledgeIndexTaskStore(query, command, Substitute.For<ICurrentTenant>(), clock, ids);
        await Assert.ThrowsExactlyAsync<TenantContextMissingException>(() => store.EnqueueAsync(Binding(), CancellationToken.None));
        Assert.IsFalse(command.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Invalid_binding_fails_before_sql()
    {
        var (store, _, command, _, _) = Create(false);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.EnqueueAsync(Binding() with { DocumentVersion = 0 }, CancellationToken.None));
        Assert.IsFalse(command.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Already_cancelled_token_prevents_any_database_write()
    {
        var (store, _, command, _, _) = Create(false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => store.CancelAsync(Binding(), Guid.CreateVersion7(), cancellation.Token));
        Assert.IsFalse(command.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Failure_code_is_closed_and_never_stores_exception_text()
    {
        var (store, _, command, _, _) = Create(false);
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        Assert.IsTrue(await store.FailAsync(Binding(), Guid.CreateVersion7(), Guid.CreateVersion7(), AiKnowledgeIndexFailure.UsageUnknown, CancellationToken.None));
        Assert.AreEqual("ai.knowledge.usage_unknown", ((Dictionary<string, object?>)command.ReceivedCalls().Single().GetArguments()[1]!)["FailureCode"]);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => store.FailAsync(Binding(), Guid.CreateVersion7(), Guid.CreateVersion7(), (AiKnowledgeIndexFailure)999, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Native_reader_preserves_exact_versions_and_nullable_lease(bool mysql)
    {
        using var table = new DataTable();
        foreach (var column in new[] { "Id", "DocumentId", "VersionId", "ModelConfigId", "LeaseToken" }) table.Columns.Add(column, typeof(Guid));
        foreach (var column in new[] { "DocumentVersion", "KnowledgeBaseVersion", "ModelConfigVersion", "AttemptCount" }) table.Columns.Add(column, typeof(int));
        foreach (var column in new[] { "StateKey", "FailureCode" }) table.Columns.Add(column, typeof(string));
        foreach (var column in new[] { "CreatedAtUtc", "UpdatedAtUtc", "LeaseExpiresAtUtc" }) table.Columns.Add(column, mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        var source = table.NewRow();
        foreach (var column in new[] { "Id", "DocumentId", "VersionId", "ModelConfigId" }) source[column] = Guid.CreateVersion7();
        source["DocumentVersion"] = 9;
        source["KnowledgeBaseVersion"] = 6;
        source["ModelConfigVersion"] = 3;
        source["AttemptCount"] = 2;
        source["StateKey"] = mysql ? "running" : "failed";
        source["FailureCode"] = mysql ? DBNull.Value : "ai.knowledge.usage_unknown";
        var lease = Guid.CreateVersion7();
        source["LeaseToken"] = mysql ? lease : DBNull.Value;
        var now = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        source["CreatedAtUtc"] = mysql ? now.UtcDateTime : (object)now;
        source["UpdatedAtUtc"] = mysql ? now.UtcDateTime : (object)now;
        source["LeaseExpiresAtUtc"] = mysql ? now.UtcDateTime : DBNull.Value;
        table.Rows.Add(source);
        using var reader = table.CreateDataReader();
        Assert.IsTrue(reader.Read());
        var row = AiBudgetRowReaders.ReadKnowledgeIndexTask(reader);
        Assert.AreEqual((Guid)source["Id"], row.Id);
        Assert.AreEqual((Guid)source["DocumentId"], row.DocumentId);
        Assert.AreEqual((Guid)source["VersionId"], row.VersionId);
        Assert.AreEqual((Guid)source["ModelConfigId"], row.ModelConfigId);
        Assert.AreEqual(9, row.DocumentVersion);
        Assert.AreEqual(6, row.KnowledgeBaseVersion);
        Assert.AreEqual(3, row.ModelConfigVersion);
        Assert.AreEqual(2, row.AttemptCount);
        Assert.AreEqual(mysql ? "running" : "failed", row.StateKey);
        Assert.AreEqual(mysql ? null : "ai.knowledge.usage_unknown", row.FailureCode);
        Assert.AreEqual(mysql ? lease : null, row.LeaseToken);
        Assert.AreEqual(mysql ? now : null, row.LeaseExpiresAtUtc);
        Assert.AreEqual(now, row.CreatedAtUtc);
        Assert.AreEqual(now, row.UpdatedAtUtc);
    }

    private static AiKnowledgeIndexTaskBinding Binding() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
    private static (AiKnowledgeIndexTaskStore Store, IQueryExecutor Query, ICommandExecutor Command, IClock Clock, IIdGenerator Ids) Create(bool tenantScope)
    {
        var tenant = new CurrentTenantAccessor();
        if (tenantScope) tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "test", "Test")); else tenant.SetHost();
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        return (new AiKnowledgeIndexTaskStore(query, command, tenant, clock, ids), query, command, clock, ids);
    }
}
