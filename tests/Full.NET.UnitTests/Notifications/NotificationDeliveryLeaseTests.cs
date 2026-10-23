using System.Reflection;
using System.Collections.Concurrent;
using Full.NET.Abstractions.Tenancy;
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
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

/// <summary>失去有效租约的旧 Worker 不得继续读取待发送内容或进入提供程序。</summary>
[TestClass]
public sealed class NotificationDeliveryLeaseTests
{
    /// <summary>并行队列中的取消或失租不得进入载荷读取和提供程序外发。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Parallel_queued_deliveries_stop_before_payload_on_cancellation_or_lost_lease(bool cancel)
    {
        var now = DateTimeOffset.UtcNow;
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var claimed = Enumerable.Range(0, 8).Select(_ => new NotificationDeliveryRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "email", Guid.NewGuid(), null,
            "accepted", 1, "worker", now.AddMinutes(1), 1, null, now, null, null, null, null, null)).ToArray();
        var queries = new LeaseQueries(claimed);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewals = 0;
        var contexts = new ConcurrentBag<CurrentTenantAccessor>();
        var executors = new ConcurrentBag<ICommandExecutor>();
        async Task<int> RenewAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref renewals) == 2) entered.TrySetResult();
            if (cancel) await Task.Delay(Timeout.Infinite, token);
            await Task.Yield();
            return 0;
        }
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenantContextWriter>(_ =>
        {
            var tenant = new CurrentTenantAccessor(); contexts.Add(tenant); return tenant;
        });
        services.AddScoped<NotificationDeliveryBatchProcessor>(sp =>
        {
            var commands = Substitute.For<ICommandExecutor>(); executors.Add(commands);
            commands.ExecuteAsync(NotificationPlatformSql.RenewDeliveryLease, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(call => RenewAsync(call.ArgAt<CancellationToken>(2)));
            return new NotificationDeliveryBatchProcessor(queries, commands, Substitute.For<ICommandTransaction>(), [],
                null!, null!, clock, Substitute.For<IIdGenerator>(),
                Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
                Options.Create(new NotificationDeliveryWorkerOptions { MaxConcurrency = 2, BatchSize = 8 }),
                new NotificationRecipientEndpointProtector(new EphemeralDataProtectionProvider()),
                null!,
                NullLogger<NotificationDeliveryBatchProcessor>.Instance, sp.GetRequiredService<IServiceScopeFactory>());
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        var pending = scope.ServiceProvider.GetRequiredService<NotificationDeliveryBatchProcessor>().ProcessPendingAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (cancel)
        {
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        }
        else Assert.AreEqual(8, await pending);
        Assert.AreEqual(cancel ? 2 : 8, renewals, "取消后不能启动排队项；失租项须各自核验后退出。");
        Assert.AreEqual(cancel ? 2 : 8, contexts.Count);
        Assert.IsTrue(contexts.All(tenant => !tenant.IsHost && tenant.Id is null), "独立作用域退出时必须清理租户上下文。");
        Assert.AreEqual(contexts.Count + 1, executors.Distinct().Count(), "领取与每条执行不得共享命令执行器。");
        Assert.AreEqual(cancel ? 1 : 4, queries.Calls, "只按两个可执行槽位领取，不能读取发送载荷。");
    }

    /// <summary>显式领取替身避免动态代理内部行类型，并在任何载荷读取时立即失败。</summary>
    private sealed class LeaseQueries(IReadOnlyList<NotificationDeliveryRecord> claimed) : IQueryExecutor
    {
        internal int Calls;

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            if (typeof(T) == typeof(NotificationDeliveryRecord) && statement == NotificationPlatformSql.ClaimDeliveriesSqlServer)
            {
                var take = (int)((IReadOnlyDictionary<string, object?>)parameters!)["BatchSize"]!;
                Assert.IsLessThanOrEqualTo(2, take);
                var offset = (Calls - 1) * take;
                return Task.FromResult((IReadOnlyList<T>)(object)claimed.Skip(offset).Take(take).ToArray());
            }
            throw new InvalidOperationException("取消或失租后不得读取发送载荷。");
        }

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("取消或失租后不得读取发送载荷。");
        }
    }

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
        queries.QuerySingleOrDefaultAsync<int>(NotificationPlatformSql.FindLastAttemptNumber,
            Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new AttemptBudgetReachedException());
        var processor = new NotificationDeliveryBatchProcessor(queries, commands,
            Substitute.For<ICommandTransaction>(), [], null!, null!, clock, Substitute.For<IIdGenerator>(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Options.Create(new NotificationDeliveryWorkerOptions()),
            new NotificationRecipientEndpointProtector(new EphemeralDataProtectionProvider()),
            null!,
            NullLogger<NotificationDeliveryBatchProcessor>.Instance, null!);
        var delivery = new NotificationDeliveryRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "email", Guid.NewGuid(), null, "accepted", 1, "worker", now.AddMinutes(1), 1, null, now, null, null, null, null, null);

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
            null!,
            NullLogger<NotificationDeliveryBatchProcessor>.Instance, null!);
        var delivery = new NotificationDeliveryRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "email", Guid.NewGuid(), null,
            "accepted", 1, "old-worker", now.AddSeconds(secondsUntilExpiry), 1, null, now, null, null, null, null, null);

        // 直接进入单条执行边界，模拟批次前项已耗尽后项租约，避免领取逻辑掩盖陈旧快照。
        var task = (Task)typeof(NotificationDeliveryBatchProcessor)
            .GetMethod("ProcessOneAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(processor, [delivery, CancellationToken.None])!;
        await task;

        Assert.AreEqual(0, queries.ReceivedCalls().Count(), "失去所有权时必须在载荷准备之前退出。");
    }
}
