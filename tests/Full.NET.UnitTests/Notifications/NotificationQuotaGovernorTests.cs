using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

/// <summary>配额恢复不得把调用未知当成未发送，台账 CAS 只有赢家可以调整计数。</summary>
[TestClass]
public sealed class NotificationQuotaGovernorTests
{
    [TestMethod]
    public async Task Abandoned_reservation_parks_without_releasing_or_reserving_again()
    {
        var (governor, query, command) = CreateFixture();
        var deliveryId = Guid.CreateVersion7();
        query.LockedDeliveryId = deliveryId;
        query.Abandoned = [new NotificationQuotaAbandonedReservationRecord(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "producer", DateTimeOffset.UtcNow, 2)];

        var result = await governor.ReserveAsync(deliveryId, 3, "host", "test.notification", "producer", "scene", CancellationToken.None);

        Assert.AreEqual(NotificationQuotaReservationStatus.PendingReconciliation, result.Status);
        await command.Received(1).ExecuteAsync(NotificationPlatformSql.ResolveQuotaReservationById,
            Arg.Is<object?>(parameters => HasState(parameters, NotificationQuotaReservationStates.PendingReconciliation)),
            Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(NotificationPlatformSql.ReleaseQuotaCounter,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(NotificationPlatformSql.InsertQuotaReservation,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());

        // 已转待对账的同一台账仍由旧世代查询命中，后续人工重领也必须暂挂。
        var later = await governor.ReserveAsync(deliveryId, 4, "host", "test.notification", "producer", "scene", CancellationToken.None);
        Assert.AreEqual(NotificationQuotaReservationStatus.PendingReconciliation, later.Status);
        StringAssert.Contains(NotificationPlatformSql.ListAbandonedQuotaReservationsSqlServer.Text,
            "StateKey IN ('reserved', 'pending_reconciliation')");
        StringAssert.Contains(NotificationPlatformSql.ListAbandonedQuotaReservationsMySql.Text,
            "StateKey IN ('reserved', 'pending_reconciliation')");
        await command.DidNotReceive().ExecuteAsync(NotificationPlatformSql.InsertQuotaReservation,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Lost_lease_stops_before_reading_quota_policies()
    {
        var (governor, query, command) = CreateFixture();
        var result = await governor.ReserveAsync(Guid.CreateVersion7(), 3, "host", "provider", "producer", "scene", CancellationToken.None);
        Assert.AreEqual(NotificationQuotaReservationStatus.LostLease, result.Status);
        Assert.AreEqual(0, query.ListCalls);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("delivered", true)]
    [DataRow("delivered", false)]
    [DataRow("failed", true)]
    [DataRow("failed", false)]
    public async Task Receipt_adjusts_counter_only_when_reservation_cas_wins(string mappedStatus, bool wins)
    {
        var (governor, query, command) = CreateFixture();
        query.Reservations = [new NotificationQuotaReservationRecord(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "producer", DateTimeOffset.UtcNow)];
        command.ExecuteAsync(NotificationPlatformSql.ResolveQuotaReservationById,
                Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(wins ? 1 : 0);

        await governor.ReconcileForMessageAsync(Guid.CreateVersion7(), "message", mappedStatus, CancellationToken.None);

        var counter = NotificationPlatformSql.ConsumeQuotaCounter;
        await command.Received(wins ? 1 : 0).ExecuteAsync(counter, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(NotificationPlatformSql.ReleaseQuotaCounter,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private static bool HasState(object? parameters, string state) =>
        parameters is IReadOnlyDictionary<string, object?> values
        && Equals(values["StateKey"], state);

    private static (NotificationQuotaGovernor Governor, QuotaQueries Query, ICommandExecutor Command) CreateFixture()
    {
        var query = new QuotaQueries();
        var command = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        return (new NotificationQuotaGovernor(query, command, new ImmediateTransaction(), clock,
            Substitute.For<IIdGenerator>(), Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer })),
            query, command);
    }

    /// <summary>显式替身避免为内部投影类型生成动态代理，也不扩大生产类型的可见性。</summary>
    private sealed class QuotaQueries : IQueryExecutor
    {
        internal Guid LockedDeliveryId;
        internal int ListCalls;
        internal IReadOnlyList<NotificationQuotaReservationRecord> Reservations = [];
        internal IReadOnlyList<NotificationQuotaAbandonedReservationRecord> Abandoned = [];

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            ListCalls++;
            if (typeof(T) == typeof(NotificationQuotaReservationRecord))
                return Task.FromResult((IReadOnlyList<T>)(object)Reservations);
            if (typeof(T) == typeof(NotificationQuotaAbandonedReservationRecord))
                return Task.FromResult((IReadOnlyList<T>)(object)Abandoned);
            throw new InvalidOperationException($"不应执行查询：{statement.Name}");
        }

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(Guid))
                return Task.FromResult((T?)(object)LockedDeliveryId);
            throw new InvalidOperationException($"不应执行查询：{statement.Name}");
        }
    }

    /// <summary>单测只验证控制流；真实回滚、行锁与计数器一致性由双库集成测试覆盖。</summary>
    private sealed class ImmediateTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default) =>
            action(cancellationToken);

        public Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default) =>
            action(cancellationToken);

        public Task<Result<T>> ExecuteResultAsync<T>(Func<CancellationToken, Task<Result<T>>> action, CancellationToken cancellationToken = default) =>
            action(cancellationToken);
    }
}
