using System.Diagnostics.Metrics;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>指标只观察已提交账本；嵌套事务返回不构成提交凭证。</summary>
[TestClass, DoNotParallelize]
public sealed class AiBudgetCommitObservationTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Reservation_or_settlement_is_observed_only_after_outer_commit(bool settlement)
    {
        using var f = new Fixture();
        await f.Transaction.ExecuteAsync(async token =>
        {
            await f.WriteAsync(settlement, token);
            Assert.HasCount(0, f.Measurements);
            Assert.IsTrue(f.Coordinator.HasTransaction);
            return true;
        }, CancellationToken.None);
        Assert.AreEqual(1, f.Coordinator.CommitCount);
        Assert.AreEqual(settlement ? 2 : 1, f.Measurements.Count);
        Assert.IsTrue(f.Measurements.All(m => !m.InTransaction));
        Assert.AreEqual(settlement ? "succeeded" : "reserved", f.Measurements[0].Tags.Single().Value);
        if (settlement) Assert.AreEqual("USD", f.Measurements[1].Tags.Single().Value);
    }

    [TestMethod]
    [DataRow(false, false)] [DataRow(true, false)]
    [DataRow(false, true)] [DataRow(true, true)]
    public async Task Outer_exception_or_failure_result_does_not_publish_budget_facts(bool settlement, bool resultFailure)
    {
        using var f = new Fixture();
        if (resultFailure)
        {
            var result = await f.Transaction.ExecuteResultAsync(async token =>
            {
                await f.WriteAsync(settlement, token);
                return Result<bool>.Failure(new Error("test.failure", "Failure.", ErrorType.BusinessRule));
            }, CancellationToken.None);
            Assert.IsFalse(result.IsSuccess);
        }
        else
            await Assert.ThrowsAsync<IOException>(() => f.Transaction.ExecuteAsync<bool>(async token =>
            {
                await f.WriteAsync(settlement, token);
                throw new IOException("late failure");
            }, CancellationToken.None));
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
        Assert.HasCount(0, f.Measurements);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Commit_failure_does_not_publish_reserved_or_charged_facts(bool settlement)
    {
        using var f = new Fixture(new FailingCommit());
        await Assert.ThrowsAsync<IOException>(() => f.WriteAsync(settlement, CancellationToken.None));
        Assert.HasCount(0, f.Measurements.Where(m => m.Tags.Any(t => t.Value is "reserved" or "succeeded" or "USD")).ToArray());
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Duplicate_receipt_does_not_publish_another_fact(bool settlement)
    {
        using var f = new Fixture(known: true);
        await f.WriteAsync(settlement, CancellationToken.None);
        Assert.HasCount(0, f.Measurements);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Throwing_listener_cannot_change_a_committed_budget_result(bool settlement)
    {
        using var f = new Fixture(throwOnMeasurement: true);
        await f.WriteAsync(settlement, CancellationToken.None);
        Assert.AreEqual(1, f.Coordinator.CommitCount);
        Assert.AreEqual(0, f.Coordinator.RollbackCount);
        if (settlement) Assert.IsTrue(f.Measurements.Any(m => m.Name == "fullnet.ai.charged_cost"));
    }

    private sealed class FailingCommit : RecordingDbTransactionCoordinator
    {
        public override Task CommitAsync(CancellationToken token) => Task.FromException(new IOException("commit failed"));
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly RecordingDbTransactionCoordinator Coordinator;
        internal readonly DapperCommandTransaction Transaction;
        internal readonly List<(string Name, bool InTransaction, KeyValuePair<string, object?>[] Tags)> Measurements = [];
        internal readonly AiOperationRequest Request = new(Guid.NewGuid(), null, Guid.NewGuid(), "agent", new string('A', 64), 20, 10,
            ProviderKey: "ollama", ModelId: "model");
        internal readonly AiOperationBudgetRecord Row;
        private readonly AiOperationBudgetStore _store;
        private readonly MeterListener _listener = new();

        internal Fixture(RecordingDbTransactionCoordinator? coordinator = null, bool throwOnMeasurement = false, bool known = false)
        {
            Coordinator = coordinator ?? new(); Transaction = new(Coordinator);
            var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Row = new() { Id = Request.OperationId, UsageStatus = known ? "known" : "reserved", QuotaMonthKey = "2026-10", ReservedTokens = 30,
                RequestHash = AiOperationBudgetStore.Fingerprint(Request), InputTokens = 2, OutputTokens = 1, Outcome = "succeeded",
                PriceVersionId = Guid.NewGuid(), Currency = "USD", InputPerMillion = 2, OutputPerMillion = 4, CachedInputPerMillion = 1 };
            query.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => _settling || Row.UsageStatus == "known" ? Row : null);
            var tenant = new CurrentTenantAccessor(); tenant.SetHost();
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.NewGuid());
            _store = new(query, command, Transaction, tenant, clock, ids, Options.Create(new DatabaseOptions()),
                Options.Create(new AiOperationBudgetOptions()), new AiChatQuotaGuard(query, command, Transaction, clock), Transaction);
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AiOperationBudgetStore.MeterName) listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Record(instrument, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Record(instrument, tags));
            _listener.Start();
            void Record(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            {
                Measurements.Add((instrument.Name, Coordinator.HasTransaction, tags.ToArray()));
                if (throwOnMeasurement) throw new InvalidOperationException("observer failed");
            }
        }

        private bool _settling;
        internal async Task WriteAsync(bool settlement, CancellationToken token)
        {
            _settling = settlement;
            if (settlement) await _store.SettleAsync(Request.OperationId, new(2, 1), "succeeded", token);
            else await _store.ReserveAsync(Request, token);
        }
        public void Dispose() => _listener.Dispose();
    }
}
