using System.Reflection;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

/// <summary>失去有效租约的旧 Worker 不得继续读取待发送内容或进入提供程序。</summary>
[TestClass]
public sealed class NotificationDeliveryLeaseTests
{
    /// <summary>内部取消必须进入尝试计数路径，不能无限等租约到期重新发送。</summary>
    [TestMethod]
    public async Task Internal_cancellation_reaches_attempt_budget()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        queries.QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns<NotificationProviderProfileVersionRecord?>(_ => throw new OperationCanceledException());
        queries.QuerySingleOrDefaultAsync<long>(NotificationPlatformSql.CountAttemptsByDelivery,
            Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns<long>(_ => throw new AttemptBudgetReachedException());
        var processor = new NotificationDeliveryBatchProcessor(queries, commands,
            Substitute.For<ICommandTransaction>(), [], null!, null!, clock, Substitute.For<IIdGenerator>(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Options.Create(new NotificationDeliveryWorkerOptions()),
            new NotificationRecipientEndpointProtector(new EphemeralDataProtectionProvider()),
            NullLogger<NotificationDeliveryBatchProcessor>.Instance);
        var delivery = new NotificationDeliveryRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "email", Guid.NewGuid(), null, "accepted", 1, "worker", now.AddMinutes(1), 1, null, now, null);

        var task = (Task)typeof(NotificationDeliveryBatchProcessor)
            .GetMethod("ProcessOneAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(processor, [delivery, CancellationToken.None])!;
        await Assert.ThrowsExactlyAsync<AttemptBudgetReachedException>(() => task);
    }

    /// <summary>标记控制流已进入持久化尝试预算，避免测试依赖数据库或附件生命周期。</summary>
    private sealed class AttemptBudgetReachedException : Exception;

    /// <summary>覆盖本地已过期以及数据库拒绝当前所有权两种情形。</summary>
    /// <param name="secondsUntilExpiry">领取快照中的剩余租约秒数。</param>
    [TestMethod]
    [DataRow(-1)]
    [DataRow(60)]
    public async Task Lost_lease_stops_before_preparing_delivery(int secondsUntilExpiry)
    {
        var now = DateTimeOffset.UtcNow;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns<NotificationProviderProfileVersionRecord?>(_ => throw new InvalidOperationException("失去租约后仍读取发送载荷。"));
        var processor = new NotificationDeliveryBatchProcessor(
            queries, Substitute.For<ICommandExecutor>(), Substitute.For<ICommandTransaction>(), [],
            null!, null!, clock, Substitute.For<IIdGenerator>(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Options.Create(new NotificationDeliveryWorkerOptions()),
            new NotificationRecipientEndpointProtector(new EphemeralDataProtectionProvider()),
            NullLogger<NotificationDeliveryBatchProcessor>.Instance);
        var delivery = new NotificationDeliveryRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "email", Guid.NewGuid(), null,
            "accepted", 1, "old-worker", now.AddSeconds(secondsUntilExpiry), 1, null, now, null);

        // 直接进入单条执行边界，模拟批次前项已耗尽后项租约，避免领取逻辑掩盖陈旧快照。
        var task = (Task)typeof(NotificationDeliveryBatchProcessor)
            .GetMethod("ProcessOneAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(processor, [delivery, CancellationToken.None])!;
        await task;

        Assert.AreEqual(0, queries.ReceivedCalls().Count(), "失去所有权时必须在载荷准备之前退出。");
    }
}
