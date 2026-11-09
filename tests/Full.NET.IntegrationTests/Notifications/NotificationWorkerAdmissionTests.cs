using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Data;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>用独立准入实例和真实数据库验证通知在争用期间的提交及恢复。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NotificationWorkerAdmissionTests
{
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
            var sent = await ReadSentAttemptsAsync(second, original, token);
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

    private static async Task<long> ReadSentAttemptsAsync(FullNetApiFactory factory, Guid deliveryId, CancellationToken token)
    {
        await using var scope = factory.Services.CreateAsyncScope();
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
