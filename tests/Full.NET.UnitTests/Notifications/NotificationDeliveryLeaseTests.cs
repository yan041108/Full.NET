using System.Reflection;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.IntentAttachments;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

/// <summary>失去有效租约的旧 Worker 不得继续读取待发送内容或进入提供程序。</summary>
[TestClass]
public sealed class NotificationDeliveryLeaseTests
{
    /// <summary>SMTP 外发前必须成功持久化停放状态；丢失所有权时禁止调用外部服务。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Smtp_send_requires_durable_inflight_marker(bool markerAccepted)
    {
        var now = DateTimeOffset.UtcNow;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var queries = new PreparedSmtpQueries(now);
        var commands = Substitute.For<ICommandExecutor>();
        var marked = false;
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.Arg<SqlStatement>()!.Name != "notifications.platform.delivery.mark_smtp_inflight") return 1;
                marked = markerAccepted;
                return markerAccepted ? 1 : 0;
            });
        var delivery = new NotificationDeliveryRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "email", Guid.NewGuid(), null, "accepted", 1, "worker", now.AddMinutes(1), 1, null, now, null);
        queries.PoisonAttemptBudget = true;
        var adapter = new ObservedSmtpAdapter(_ =>
        {
            Assert.IsTrue(marked, "外部发送必须发生在持久化标记成功之后。");
            return ValueTask.FromResult(new NotificationProviderResult(false, NotificationDeliveryRetry.Unknown, null, null));
        });
        var processor = new NotificationDeliveryBatchProcessor(queries, commands, Substitute.For<ICommandTransaction>(),
            [adapter], new NotificationAttachmentLoader(queries, null!), null!, clock, Substitute.For<IIdGenerator>(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Options.Create(new NotificationDeliveryWorkerOptions()),
            new NotificationRecipientEndpointProtector(new EphemeralDataProtectionProvider()),
            NullLogger<NotificationDeliveryBatchProcessor>.Instance);
        var task = (Task)typeof(NotificationDeliveryBatchProcessor)
            .GetMethod("ProcessOneAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(processor, [delivery, CancellationToken.None])!;
        if (markerAccepted) await Assert.ThrowsExactlyAsync<AttemptBudgetReachedException>(() => task);
        else await task;
        Assert.AreEqual(markerAccepted ? 1 : 0, adapter.SendCount);
        Assert.AreEqual(markerAccepted, marked);
    }

    /// <summary>真正进入 Worker 外发路径，覆盖结果停放、已知退避、宿主取消、崩溃与标记耗尽租约。</summary>
    [TestMethod]
    [DataRow("unknown")]
    [DataRow("transient")]
    [DataRow("host_cancel")]
    [DataRow("provider_timeout")]
    [DataRow("crash")]
    [DataRow("marker_expired")]
    public async Task Smtp_worker_preserves_uncertain_delivery_and_lease_budget(string scenario)
    {
        var now = DateTimeOffset.UtcNow;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var queries = new PreparedSmtpQueries(now);
        var commands = Substitute.For<ICommandExecutor>();
        var marked = false;
        Dictionary<string, object?>? completion = null;
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (call.Arg<SqlStatement>() == NotificationPlatformSql.MarkSmtpDeliveryInFlight)
            {
                marked = true;
                if (scenario == "marker_expired") clock.UtcNow.Returns(now.AddMinutes(2));
            }
            if (call.Arg<SqlStatement>() == NotificationPlatformSql.CompleteDelivery)
                completion = (Dictionary<string, object?>)call.ArgAt<object>(1)!;
            return 1;
        });
        using var cancellation = new CancellationTokenSource();
        var log = new ObservedLogger();
        var adapter = new ObservedSmtpAdapter(_ =>
        {
            Assert.IsTrue(marked);
            if (scenario == "host_cancel") { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
            if (scenario == "provider_timeout") throw new OperationCanceledException();
            if (scenario == "crash") throw new IOException("private-provider-error");
            return ValueTask.FromResult(new NotificationProviderResult(false, scenario, null, null));
        });
        var transaction = Substitute.For<ICommandTransaction>();
        transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<int>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<int>>>()!(call.Arg<CancellationToken>()));
        var processor = new NotificationDeliveryBatchProcessor(queries, commands, transaction, [adapter],
            new NotificationAttachmentLoader(queries, null!),
            new NotificationIntentAttachmentCoordinator(null!, null!, queries, commands, clock, Substitute.For<IIdGenerator>()),
            clock, Substitute.For<IIdGenerator>(), Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Options.Create(new NotificationDeliveryWorkerOptions()),
            new NotificationRecipientEndpointProtector(new EphemeralDataProtectionProvider()),
            log);
        var delivery = new NotificationDeliveryRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "email",
            Guid.NewGuid(), null, "accepted", 7, "worker", now.AddMinutes(1), 3, null, now, null);
        var task = (Task)typeof(NotificationDeliveryBatchProcessor).GetMethod("ProcessOneAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(processor, [delivery, cancellation.Token])!;
        if (scenario == "host_cancel") await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => task);
        else await task;
        Assert.IsTrue(marked);
        Assert.AreEqual(scenario == "marker_expired" ? 0 : 1, adapter.SendCount);
        if (scenario is "unknown" or "transient" or "provider_timeout")
        {
            Assert.IsNotNull(completion);
            Assert.AreEqual(8L, completion["Revision"]);
            Assert.AreEqual(scenario == "transient" ? "accepted" : "unknown", completion["StatusKey"]);
            Assert.AreEqual(scenario != "transient", completion["NextAttemptAtUtc"] is null);
        }
        else Assert.IsNull(completion);
        if (scenario == "crash")
        {
            Assert.IsNull(log.Exception);
            Assert.IsNotNull(log.Message);
            Assert.IsTrue(log.Message.Contains(nameof(IOException), StringComparison.Ordinal));
            Assert.IsFalse(log.Message.Contains("private-provider-error", StringComparison.Ordinal));
        }
    }

    /// <summary>显式类型投影避免测试代理内部泛型集合时绕过真实外发路径。</summary>
    private sealed class PreparedSmtpQueries(DateTimeOffset now) : IQueryExecutor
    {
        public bool PoisonAttemptBudget { get; set; }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(long) && PoisonAttemptBudget) throw new AttemptBudgetReachedException();
            object? row = typeof(T) == typeof(NotificationProviderProfileVersionRecord)
                ? new NotificationProviderProfileVersionRecord(Guid.NewGuid(), Guid.NewGuid(), 1, "email.smtp", "1.0.0", "{}", null, "hash", Guid.NewGuid(), now)
                : typeof(T) == typeof(NotificationIntentRecord)
                    ? new NotificationIntentRecord(Guid.NewGuid(), null, "host", "host", "test", "test", "key", Guid.NewGuid(), null, "transactional", "immediate", "{}", "{}", "accepted", Guid.NewGuid(), now, 1)
                    : typeof(T) == typeof(NotificationRecipientRecord)
                        ? new NotificationRecipientRecord(Guid.NewGuid(), Guid.NewGuid(), "email", "receiver@example.test", null, null, "resolved", now)
                        : typeof(T) == typeof(NotificationTemplateVersionRecord)
                            ? new NotificationTemplateVersionRecord(Guid.NewGuid(), Guid.NewGuid(), "en", 1, 1, "test", "{}", "{}", "transactional", "hash", Guid.NewGuid(), now)
                            : typeof(T) == typeof(long) ? 0L : typeof(T) == typeof(int) ? (object)1 : null;
            return Task.FromResult(row is null ? default : (T?)row);
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<T>>([]);
    }

    /// <summary>只观察外发调用次数和先后顺序，不实现生产状态机。</summary>
    private sealed class ObservedSmtpAdapter(Func<CancellationToken, ValueTask<NotificationProviderResult>> send) : INotificationProviderAdapter
    {
        public NotificationProviderTypeDescriptor Descriptor { get; } = new("email.smtp", "1.0.0", ["email"], [], [], true, NotificationReceiptModeKeys.None);
        public string? RecipientEndpointKindKey => null;
        public int SendCount { get; private set; }
        public ValueTask<NotificationProviderResult> SendAsync(NotificationProviderRequest request, CancellationToken cancellationToken)
        { SendCount++; return send(cancellationToken); }
    }

    /// <summary>捕获最终日志载荷，确保提供程序原始异常不会进入诊断输出。</summary>
    private sealed class ObservedLogger : ILogger<NotificationDeliveryBatchProcessor>
    {
        public string? Message { get; private set; }
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Exception = exception; Message = formatter(state, exception); }
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
