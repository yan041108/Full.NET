using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AI：Agent Run Cancellation。</summary>
[TestClass]
public sealed class AiAgentRunCancellationTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Cancellation_commits_event_and_unknown_usage_in_one_transaction(bool tenantScope)
    {
        var f = new Fixture(tenantScope);
        f.Budget.When(x => x.SettleAsync(f.RunId, Arg.Any<AiOperationUsage>(), "cancelled", Arg.Any<CancellationToken>()))
            .Do(_ => Assert.IsTrue(f.Coordinator.HasTransaction, "预算必须与取消事件一起提交。"));
        var result = await f.Service.CancelAsync(f.RunId, f.UserId);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, f.Coordinator.CommitCount);
        await f.Commands.Received(1).ExecuteAsync(AiAgentRunSql.CancelOwned,
            Arg.Is<object?>(p => Matches(p, "ScopeKey", f.Scope) && Matches(p, "ActorUserId", f.UserId)), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(AiAgentRunSql.InsertEvent,
            Arg.Is<object?>(p => Matches(p, "RunId", f.RunId) && Matches(p, "Sequence", 8L)
                && Matches(p, "EventType", "run.cancelled") && Matches(p, "Payload", "{}")), Arg.Any<CancellationToken>());
        await f.Budget.Received(1).SettleAsync(f.RunId,
            Arg.Is<AiOperationUsage>(u => u != null && u.InputTokens == null && u.OutputTokens == null), "cancelled", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Missing_budget_rolls_back_cancellation_and_propagates_failure()
    {
        var f = new Fixture();
        f.Budget.SettleAsync(f.RunId, Arg.Any<AiOperationUsage>(), "cancelled", Arg.Any<CancellationToken>())
            .Returns(_ => throw new AiBudgetException("ai.budget.operation_not_found"));
        var error = await Assert.ThrowsAsync<AiBudgetException>(() => f.Service.CancelAsync(f.RunId, f.UserId));
        Assert.AreEqual("ai.budget.operation_not_found", error.Code);
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
        Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    [TestMethod]
    [DataRow("event")] [DataRow("sequence")] [DataRow("budget")]
    public async Task Persistence_failure_rolls_back_without_retrying_settlement(string stage)
    {
        var f = new Fixture();
        if (stage == "event") f.Commands.ExecuteAsync(AiAgentRunSql.InsertEvent, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new IOException("event persistence failed")));
        if (stage == "sequence") f.Queries.QuerySingleOrDefaultAsync<long>(AiAgentRunSql.FindLatestEventSequence, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<long>(new IOException("sequence read failed")));
        if (stage == "budget") f.Budget.SettleAsync(f.RunId, Arg.Any<AiOperationUsage>(), "cancelled", Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("budget persistence failed"));
        await Assert.ThrowsAsync<IOException>(() => f.Service.CancelAsync(f.RunId, f.UserId));
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
        Assert.AreEqual(0, f.Coordinator.CommitCount);
        await f.Budget.Received(stage == "budget" ? 1 : 0).SettleAsync(f.RunId, Arg.Any<AiOperationUsage>(), "cancelled", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("status")] [DataRow("event")] [DataRow("budget")]
    public async Task Cancellation_ignored_by_dependency_still_rolls_back(string stage)
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        if (stage == "budget") f.Budget.When(x => x.SettleAsync(f.RunId, Arg.Any<AiOperationUsage>(), "cancelled", Arg.Any<CancellationToken>()))
            .Do(_ => cancellation.Cancel());
        else f.Commands.When(x => x.ExecuteAsync(stage == "status" ? AiAgentRunSql.CancelOwned : AiAgentRunSql.InsertEvent,
            Arg.Any<object?>(), Arg.Any<CancellationToken>())).Do(_ => cancellation.Cancel());
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.CancelAsync(f.RunId, f.UserId, cancellation.Token));
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
        Assert.AreEqual(0, f.Coordinator.CommitCount);
        if (stage != "budget") await f.Budget.DidNotReceive().SettleAsync(f.RunId, Arg.Any<AiOperationUsage>(), "cancelled", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Losing_status_update_never_writes_event_or_settles(bool exists)
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(AiAgentRunSql.CancelOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        f.Queries.QuerySingleOrDefaultAsync<AiAgentRunRecord>(AiAgentRunSql.FindOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(exists ? new AiAgentRunRecord { StatusKey = "completed" } : null);
        var result = await f.Service.CancelAsync(f.RunId, f.UserId);
        Assert.AreEqual(exists ? AiErrorCodes.AgentRunNotCancellable : AiErrorCodes.AgentRunNotFound, result.Error!.Code);
        await f.Commands.DidNotReceive().ExecuteAsync(AiAgentRunSql.InsertEvent, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Already_cancelled_request_never_writes()
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.CancelAsync(f.RunId, f.UserId, cancellation.Token));
        await f.Commands.DidNotReceive().ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private static bool Matches(object? parameters, string key, object value) =>
        parameters is IReadOnlyDictionary<string, object?> map && map.TryGetValue(key, out var actual) && Equals(actual, value);

    private sealed class Fixture
    {
        internal readonly Guid RunId = Guid.CreateVersion7(), UserId = Guid.CreateVersion7();
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly IAiOperationBudgetStore Budget = Substitute.For<IAiOperationBudgetStore>();
        internal readonly RecordingDbTransactionCoordinator Coordinator = new();
        internal readonly AiAgentRunManagementService Service;
        internal readonly string Scope;

        internal Fixture(bool tenantScope = false)
        {
            var tenant = Substitute.For<ICurrentTenant>();
            Guid? tenantId = tenantScope ? Guid.CreateVersion7() : null;
            tenant.Id.Returns(tenantId); tenant.IsHost.Returns(!tenantScope);
            Scope = tenantId?.ToString("N") ?? "host";
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
            var transaction = new DapperCommandTransaction(Coordinator);
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Queries.QuerySingleOrDefaultAsync<long>(AiAgentRunSql.FindLatestEventSequence, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(7L);
            var store = new AiAgentRunStore(Queries, Commands, transaction, ids, clock, Options.Create(new DatabaseOptions()));
            Service = new(store, null!, null!, null!, null!, null!, Budget, tenant, clock, ids, Options.Create(new AiAgentRuntimeOptions()), transaction);
        }
    }
}
