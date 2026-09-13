using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using Full.NET.Hosting.Api;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>预算拒绝、幂等和失败回滚通过真实存储服务与事务协调器验证。</summary>
[TestClass]
public sealed class AiOperationBudgetStoreTests
{
    [TestMethod]
    public async Task Hard_cost_without_verified_metering_never_reserves()
    {
        var (store, _, commands, _, _) = Create();
        var error = await Assert.ThrowsAsync<AiBudgetException>(() => store.ReserveAsync(Request() with { RequireHardCostLimit = true }));
        Assert.AreEqual("ai.budget.hard_cost_unavailable", error.Code);
        Assert.AreEqual(0, commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Existing_operation_returns_receipt_and_conflicting_digest_is_rejected()
    {
        var (store, queries, commands, _, _) = Create();
        var request = Request();
        queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiOperationBudgetRecord { Id = request.OperationId, RequestHash = AiOperationBudgetStore.Fingerprint(request), ReservedTokens = 30, UsageStatus = "unknown" });
        var receipt = await store.ReserveAsync(request);
        Assert.IsFalse(receipt.IsNew);
        Assert.AreEqual("unknown", receipt.UsageStatus);
        await Assert.ThrowsAsync<AiBudgetException>(() => store.ReserveAsync(request with { InputTokenLimit = 21 }));
        Assert.IsFalse(commands.ReceivedCalls().Any(call => call.GetArguments()[0] is SqlStatement sql && sql == AiOperationBudgetSql.Insert));
    }

    [TestMethod]
    public async Task Invalid_usage_does_not_touch_the_database()
    {
        var (store, queries, commands, _, _) = Create();
        await Assert.ThrowsAsync<AiBudgetException>(() => store.SettleAsync(Guid.NewGuid(), new(-1, 1), "failed"));
        Assert.AreEqual(0, queries.ReceivedCalls().Count());
        Assert.AreEqual(0, commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Compatibility_and_new_ledger_share_the_reservation_month()
    {
        var (store, queries, commands, tenant, clock) = Create();
        tenant.SetTenant(new TenantContext(Guid.NewGuid(), "tenant", "租户"));
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero), new DateTimeOffset(2026, 10, 1, 0, 0, 1, TimeSpan.Zero));
        queries.QuerySingleOrDefaultAsync<AiTenantQuotaRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiTenantQuotaRecord { IsEnabled = true });
        await store.ReserveAsync(Request());
        var calls = commands.ReceivedCalls().Where(call => call.GetArguments()[0] is SqlStatement sql
            && (sql == AiOperationBudgetSql.Insert || sql == AiQuotaReservationSql.Insert)).ToArray();
        Assert.HasCount(2, calls);
        var months = calls.Select(call => ((IReadOnlyDictionary<string, object?>)call.GetArguments()[1]!)["QuotaMonthKey"]).ToArray();
        Assert.AreEqual(months[0], months[1]);
    }

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("known")]
    [DataRow("duplicate")]
    [DataRow("conflict")]
    [DataRow("missing")]
    [DataRow("write_failure")]
    public async Task Settlement_preserves_reservation_or_applies_one_receipt(string scenario)
    {
        var (store, queries, commands, _, _) = Create();
        var id = Guid.NewGuid();
        queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(scenario == "missing" ? null : new AiOperationBudgetRecord
            {
                Id = id, QuotaMonthKey = "2026-09", ReservedTokens = 30, UsageStatus = scenario is "duplicate" or "conflict" ? "known" : "unknown",
                InputTokens = 2, OutputTokens = 1, CachedInputTokens = 1, Outcome = "failed", PriceVersionId = Guid.NewGuid(),
                InputPerMillion = 2, OutputPerMillion = 4, CachedInputPerMillion = 1, Currency = "USD", ReservedCost = 0.00008m
            });
        if (scenario == "write_failure") commands.ExecuteAsync(AiOperationBudgetSql.Settle, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var usage = scenario == "unknown" ? new AiOperationUsage(null, null) : new(2, 1, 1);
        if (scenario is "conflict" or "missing" or "write_failure")
        {
            await Assert.ThrowsAsync<AiBudgetException>(() => store.SettleAsync(id, usage, scenario == "conflict" ? "succeeded" : "failed"));
            return;
        }
        await store.SettleAsync(id, usage, "failed");
        var writes = commands.ReceivedCalls().Where(call => call.GetArguments()[0] is SqlStatement sql && sql == AiOperationBudgetSql.Settle).ToArray();
        if (scenario is "duplicate" or "unknown") Assert.HasCount(0, writes);
        else
        {
            Assert.HasCount(1, writes);
            var parameters = (IReadOnlyDictionary<string, object?>)writes[0].GetArguments()[1]!;
            Assert.AreEqual(3L, parameters["ChargedTokens"]);
            Assert.AreEqual(0.000007m, parameters["ChargedCost"]);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Monthly_or_run_exhaustion_never_creates_an_operation(bool run)
    {
        var (store, queries, commands, _, _) = Create();
        queries.QuerySingleOrDefaultAsync<AiOperationBudgetTotals>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiOperationBudgetTotals { MonthlyRequests = run ? 0 : 1000, RunRequests = 100 });
        var error = await Assert.ThrowsAsync<AiBudgetException>(() => store.ReserveAsync(Request() with { RunId = run ? Guid.NewGuid() : null }));
        Assert.AreEqual("ai.budget.limit_exceeded", error.Code);
        Assert.IsFalse(commands.ReceivedCalls().Any(call => call.GetArguments()[0] is SqlStatement sql && sql == AiOperationBudgetSql.Insert));
    }

    private static AiOperationRequest Request() => new(Guid.NewGuid(), null, Guid.NewGuid(), "chat", new string('A', 64), 20, 10, ProviderKey: "ollama", ModelId: "model");
    private static (AiOperationBudgetStore Store, IQueryExecutor Queries, ICommandExecutor Commands, CurrentTenantAccessor Tenant, IClock Clock) Create()
    {
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var tenant = new CurrentTenantAccessor(); tenant.SetHost();
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.NewGuid());
        var transaction = new DapperCommandTransaction(new RecordingDbTransactionCoordinator());
        return (new(queries, commands, transaction, tenant, clock, ids, Options.Create(new DatabaseOptions()),
            Options.Create(new AiOperationBudgetOptions()), new AiChatQuotaGuard(queries, commands, transaction, clock)), queries, commands, tenant, clock);
    }
}
