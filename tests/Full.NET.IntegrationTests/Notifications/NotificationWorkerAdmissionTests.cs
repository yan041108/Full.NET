using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Data;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Execution;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>用独立准入实例和真实数据库验证通知在争用期间的提交及恢复。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NotificationWorkerAdmissionTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Worker_profiles_with_release_budget_isolate_admission_and_drain_original_deliveries(DatabaseProvider provider)
    {
        const int permitLimit = 7;
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = budget.Token;
        var database = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var api = new FullNetApiFactory(provider, database, configureTestServices: ConfigureWorker);
        await api.InitializeAsync(token, useSchemaTemplate: false);
        var protection = new EphemeralDataProtectionProvider();
        using var first = await BuildWorkerAsync("FullNet.ReleaseBudget.First");
        using var second = await BuildWorkerAsync("FullNet.ReleaseBudget.Second");
        var firstHarness = first.Services.GetRequiredService<TestNotificationProviderHarness>();
        var secondHarness = second.Services.GetRequiredService<TestNotificationProviderHarness>();
        using var client = api.CreateClientForHost("localhost");
        var access = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(client, token);
        var user = await NotificationProfileBindingAssertions.GetCurrentUserAsync(client, access, token);
        var route = await NotificationDeliveryWorkerAssertions.CreateRouteAsync(client, access, user.Id, token);
        await ProcessAsync(first.Services);
        var deliveries = new List<NotificationDeliveryResponse>();
        for (var i = 0; i < 4; i++)
        {
            var intent = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(client, access, route, user.Id, $"release-budget-{i}", token);
            deliveries.Add(await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(client, access, intent.Id, token));
        }

        var entered = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        firstHarness.OnSendAsync = async (request, cancellationToken) =>
        {
            if (entered.TrySetResult(request.DeliveryId)) await releaseSend.Task.WaitAsync(cancellationToken);
        };
        var allHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePermits = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outcomes = new ConcurrentQueue<string>();
        var backlog = new ConcurrentQueue<long>();
        Meter? saturatedMeter = null;
        long queued = 0;
        int holdersEntered = 0, criticalOpens = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, active) =>
            {
                if (instrument.Name is "fullnet.db.connection.admission.in_use" or "fullnet.db.connection.admission.queued"
                    or "fullnet.db.connection.acquire" or "fullnet.db.connection.phase.duration"
                    or "fullnet.notifications.delivery.backlog" or "fullnet.notifications.delivery.attempts")
                    active.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "fullnet.db.connection.admission.in_use" && value == permitLimit)
                saturatedMeter = instrument.Meter;
            if (instrument.Name == "fullnet.db.connection.admission.queued" && instrument.Meter == saturatedMeter)
                queued = value;
            if (instrument.Name == "fullnet.notifications.delivery.backlog") backlog.Enqueue(value);
            if (instrument.Name == "fullnet.notifications.delivery.attempts" && releaseSend.Task.IsCompleted) committed.TrySetResult();
            if (instrument.Name != "fullnet.db.connection.acquire" || instrument.Meter != saturatedMeter) return;
            foreach (var tag in tags)
                if (tag.Key == "outcome" && tag.Value is "rejected" or "timeout") outcomes.Enqueue((string)tag.Value);
        });
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
        {
            if (instrument.Meter != saturatedMeter || instrument.Name != "fullnet.db.connection.phase.duration") return;
            var critical = false;
            var opening = false;
            foreach (var tag in tags)
            {
                critical |= tag.Key == "access_mode" && tag.Value is "critical";
                opening |= tag.Key == "phase" && tag.Value is "open";
            }
            if (critical && opening) Interlocked.Increment(ref criticalOpens);
        });
        listener.Start();
        var sending = ProcessAsync(first.Services);
        Task[] holders = [];
        Task<ServiceCapacityFailureKind>? waiting = null;
        try
        {
            var original = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            holders = Enumerable.Range(0, permitLimit).Select(_ => HoldAsync()).ToArray();
            await allHeld.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            listener.RecordObservableInstruments();
            Assert.IsNotNull(saturatedMeter, "第一实例必须实际占满普通许可。");
            waiting = ObserveFailureAsync();
            while (queued == 0 && !waiting.IsCompleted)
            {
                listener.RecordObservableInstruments();
                if (queued == 0) await Task.Delay(10, token);
            }
            Assert.AreEqual(1L, queued, "正式预算只允许排队一条。");
            Assert.AreEqual(ServiceCapacityFailureKind.Rejected, await ObserveFailureAsync());
            Assert.AreEqual(ServiceCapacityFailureKind.Timeout, await waiting);
            CollectionAssert.AreEquivalent(new[] { "rejected", "timeout" }, outcomes.ToArray());

            Assert.AreEqual(1, await ProcessAsync(second.Services));
            await SampleAsync(second.Services);
            Assert.IsTrue(backlog.TryDequeue(out var pending) && pending == 2);
            releaseSend.TrySetResult();
            if (await Task.WhenAny(committed.Task, sending).WaitAsync(TimeSpan.FromSeconds(10), token) == sending) await sending;
            Assert.IsTrue(committed.Task.IsCompletedSuccessfully);
            Assert.IsTrue(Volatile.Read(ref criticalOpens) > 0, "第一实例需要通过关键许可实际打开连接。");
            Assert.AreEqual(1L, await ReadSentAttemptsAsync(second.Services, original, token));
        }
        finally
        {
            releasePermits.TrySetResult();
            releaseSend.TrySetResult();
            await Task.WhenAll(holders);
            if (waiting is not null && !waiting.IsCompleted) await waiting;
            if (!sending.IsCompleted) await sending;
        }
        Assert.AreEqual(1, await sending);
        var drained = await Task.WhenAll(ProcessAsync(first.Services), ProcessAsync(second.Services));
        Assert.AreEqual(2, drained.Sum());
        await SampleAsync(second.Services);
        Assert.IsTrue(backlog.TryDequeue(out var remaining) && remaining == 0);
        foreach (var delivery in deliveries)
        {
            Assert.AreEqual(1L, await ReadSentAttemptsAsync(second.Services, delivery.Id, token));
            Assert.AreEqual(1, firstHarness.IdempotencyKeysFor(delivery.Id).Count + secondHarness.IdempotencyKeysFor(delivery.Id).Count);
        }

        Task<Microsoft.Extensions.Hosting.IHost> BuildWorkerAsync(string instance)
        {
            // 用不同连接串池键模拟两个进程的独立连接池，保持 Chart 的容量和等待参数。
            var connection = provider == DatabaseProvider.SqlServer
                ? new SqlConnectionStringBuilder(database) { MaxPoolSize = 10, ApplicationName = instance }.ConnectionString
                : new MySqlConnectionStringBuilder(database) { MaximumPoolSize = 10, ApplicationName = instance }.ConnectionString;
            return IntegrationWorkerHostFactory.BuildAsync(provider, connection, new Dictionary<string, string?>
            {
                ["DatabaseCapacity:Enabled"] = "true", ["DatabaseCapacity:HostRole"] = "Worker",
                ["DatabaseCapacity:PermitLimit"] = permitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["DatabaseCapacity:QueueLimit"] = "1", ["DatabaseCapacity:AcquireTimeoutMilliseconds"] = "1000",
                ["DatabaseCapacity:ExpectedMaxPoolSize"] = "10", ["DatabaseCapacity:HealthReserve"] = "2",
                ["DatabaseCapacity:CriticalWorkerReserve"] = "1", ["DatabaseCapacity:AuthorizationReadReserve"] = "0",
                ["DatabaseCapacity:ApiMaxReplicas"] = "12", ["DatabaseCapacity:ApiMaxPoolSize"] = "40",
                ["DatabaseCapacity:WorkerMaxReplicas"] = "8", ["DatabaseCapacity:WorkerMaxPoolSize"] = "10",
                ["DatabaseCapacity:MigrationReserve"] = "20", ["DatabaseCapacity:TotalBudget"] = "600"
            }, instance, nameof(NotificationDeliveryHostedProcessor), configureTargetServices: services =>
            {
                services.AddSingleton<IDataProtectionProvider>(protection);
                ConfigureWorker(services);
            });
        }

        async Task HoldAsync()
        {
            await using var scope = first.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async cancellationToken =>
            {
                if (Interlocked.Increment(ref holdersEntered) == permitLimit) allHeld.TrySetResult();
                await releasePermits.Task.WaitAsync(cancellationToken);
                return true;
            }, token);
        }

        async Task<ServiceCapacityFailureKind> ObserveFailureAsync() =>
            (await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(() => ProcessAsync(first.Services))).Kind;

        async Task<int> ProcessAsync(IServiceProvider services)
        {
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<NotificationDeliveryBatchProcessor>().ProcessPendingAsync(token);
        }

        async Task SampleAsync(IServiceProvider services)
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<NotificationDeliveryBatchProcessor>().SampleBacklogAsync(token);
        }
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Saturated_worker_persists_sent_result_and_two_workers_drain_original_deliveries(DatabaseProvider provider)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = budget.Token;
        var database = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var connectionString = WorkerAdmissionTestConfiguration.WithSmallPool(provider, database);
        var settings = WorkerAdmissionTestConfiguration.CreateSettings();
        using var first = new FullNetApiFactory(provider, connectionString, settings,
            configureTestServices: ConfigureWorker);
        await first.InitializeAsync(token, useSchemaTemplate: false);
        using var second = new FullNetApiFactory(provider, connectionString, settings,
            configureTestServices: ConfigureWorker);
        var firstHarness = first.Services.GetRequiredService<TestNotificationProviderHarness>();
        var secondHarness = second.Services.GetRequiredService<TestNotificationProviderHarness>();
        using var client = first.CreateClientForHost("localhost");
        var accessToken = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(client, token);
        var user = await NotificationProfileBindingAssertions.GetCurrentUserAsync(client, accessToken, token);
        var route = await NotificationDeliveryWorkerAssertions.CreateRouteAsync(client, accessToken, user.Id, token);
        await NotificationDeliveryWorkerAssertions.ProcessPendingAsync(first, token);
        var deliveries = new List<NotificationDeliveryResponse>();
        for (var index = 0; index < 4; index++)
        {
            var intent = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(client, accessToken, route, user.Id, $"admission-{index}", token);
            deliveries.Add(await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(client, accessToken, intent.Id, token));
        }

        var sendEntered = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        firstHarness.OnSendAsync = async (request, cancellationToken) =>
        {
            if (sendEntered.TrySetResult(request.DeliveryId))
                await releaseSend.Task.WaitAsync(cancellationToken);
        };
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outcomes = new ConcurrentQueue<string>();
        var backlog = new ConcurrentQueue<long>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, active) =>
            {
                if (instrument.Name is "fullnet.db.connection.acquire" or "fullnet.notifications.delivery.backlog"
                    or "fullnet.notifications.delivery.attempts")
                    active.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "fullnet.notifications.delivery.backlog") backlog.Enqueue(value);
            if (instrument.Name == "fullnet.notifications.delivery.attempts" && releaseSend.Task.IsCompleted)
                committed.TrySetResult();
            if (instrument.Name != "fullnet.db.connection.acquire") return;
            foreach (var tag in tags)
                if (tag.Key == "outcome" && tag.Value is "rejected" or "timeout")
                    outcomes.Enqueue((string)tag.Value);
        });
        listener.Start();
        var sending = NotificationDeliveryWorkerAssertions.ProcessPendingAsync(first, token);
        var releasePermit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var permitHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? holding = null;
        var original = Guid.Empty;
        try
        {
            original = await sendEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            holding = HoldNormalPermitAsync();
            await permitHeld.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            var failures = await Task.WhenAll(ObserveFailureAsync(), ObserveFailureAsync());
            CollectionAssert.AreEquivalent(new[] { ServiceCapacityFailureKind.Rejected, ServiceCapacityFailureKind.Timeout }, failures);
            CollectionAssert.AreEquivalent(new[] { "rejected", "timeout" }, outcomes.ToArray());

            Assert.AreEqual(1, await NotificationDeliveryWorkerAssertions.ProcessPendingAsync(second, token));
            await SampleBacklogAsync(second);
            Assert.IsTrue(backlog.TryDequeue(out var waiting) && waiting == 2, "积压应只包含两条可领取的原投递。");

            // 厂商已经成功时，写回原结果必须能在普通许可仍被占用时完成。
            releaseSend.TrySetResult();
            var finished = await Task.WhenAny(committed.Task, sending).WaitAsync(TimeSpan.FromSeconds(10), token);
            if (finished == sending) await sending;
            Assert.IsTrue(committed.Task.IsCompletedSuccessfully, "已发送结果应使用关键许可提交。");
            var sent = await ReadSentAttemptsAsync(second.Services, original, token);
            Assert.AreEqual(1L, sent, "在释放普通许可前应能读回原投递及唯一成功尝试。");
        }
        finally
        {
            releasePermit.TrySetResult();
            releaseSend.TrySetResult();
            if (holding is not null) await holding;
            if (!sending.IsCompleted) await sending;
            firstHarness.OnSendAsync = null;
        }
        Assert.AreEqual(1, await sending);
        Assert.IsTrue(deliveries.Any(delivery => delivery.Id == original));
        var drained = await Task.WhenAll(NotificationDeliveryWorkerAssertions.ProcessPendingAsync(first, token),
            NotificationDeliveryWorkerAssertions.ProcessPendingAsync(second, token));
        Assert.AreEqual(2, drained.Sum());
        await SampleBacklogAsync(second);
        Assert.IsTrue(backlog.TryDequeue(out var remaining) && remaining == 0);
        foreach (var initial in deliveries)
        {
            var delivery = await NotificationDeliveryWorkerAssertions.GetDeliveryAsync(client, accessToken, initial.Id, token);
            Assert.AreEqual(initial.IntentId, delivery.IntentId);
            Assert.AreEqual("sent", delivery.StatusKey);
            Assert.HasCount(1, delivery.Attempts);
            Assert.AreEqual(1, firstHarness.IdempotencyKeysFor(delivery.Id).Count + secondHarness.IdempotencyKeysFor(delivery.Id).Count);
        }

        async Task HoldNormalPermitAsync()
        {
            await using var scope = first.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async cancellationToken =>
            {
                permitHeld.TrySetResult();
                await releasePermit.Task.WaitAsync(cancellationToken);
                return true;
            }, token);
        }

        async Task<ServiceCapacityFailureKind> ObserveFailureAsync()
        {
            var failure = await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(() =>
                NotificationDeliveryWorkerAssertions.ProcessPendingAsync(first, token));
            return failure.Kind;
        }

        async Task SampleBacklogAsync(FullNetApiFactory factory)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<NotificationDeliveryBatchProcessor>().SampleBacklogAsync(token);
        }
    }

    private static async Task<long> ReadSentAttemptsAsync(IServiceProvider services, Guid deliveryId, CancellationToken token)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<long>(
            new SqlStatement("tests.notifications.admission.sent_attempt", """
                SELECT COUNT(1) FROM fn_notifications_delivery delivery
                INNER JOIN fn_notifications_delivery_attempt attempt ON attempt.DeliveryId=delivery.Id
                WHERE delivery.Id=@Id AND delivery.StatusKey='sent' AND attempt.StatusKey='succeeded'
                """, SqlDataScope.Global), new Dictionary<string, object?> { ["Id"] = deliveryId }, token);
    }

    private static void ConfigureWorker(IServiceCollection services)
    {
        NotificationProfileBindingAssertions.ConfigureTestServices(services);
        // 测试主动驱动批次，API 宿主只注册选项默认值，不绑定后台 Worker 配置节。
        services.PostConfigure<NotificationDeliveryWorkerOptions>(options =>
        {
            options.BatchSize = 1;
            options.MaxConcurrency = 1;
        });
    }
}
