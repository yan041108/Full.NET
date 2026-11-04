using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>复用已受理意图及真实 API 场景，验证双库聚合；不额外建库或关闭外键。</summary>
internal static class NotificationIntentDeliveryProgressAssertions
{
    internal static async Task VerifyAsync(IServiceProvider services, NotificationIntentResponse intent,
        string producerKey, string idempotencyKey, CancellationToken cancellationToken = default) =>
        await VerifyOwnerAsync(services, intent, producerKey, idempotencyKey, null, cancellationToken).ConfigureAwait(false);

    internal static async Task VerifyTenantAsync(IServiceProvider services, NotificationIntentResponse intent,
        string producerKey, string idempotencyKey, Guid tenantId, CancellationToken cancellationToken = default) =>
        await VerifyOwnerAsync(services, intent, producerKey, idempotencyKey, tenantId, cancellationToken).ConfigureAwait(false);

    private static async Task VerifyOwnerAsync(IServiceProvider services, NotificationIntentResponse intent,
        string producerKey, string idempotencyKey, Guid? ownerTenantId, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        void SetOwner()
        {
            if (ownerTenantId is { } id)
            {
                Assert.AreNotEqual(Guid.Empty, id);
                tenant.SetTenant(new TenantContext(id, "progress-test", "通知聚合测试租户"));
            }
            else tenant.SetHost();
        }
        SetOwner();
        var directory = scope.ServiceProvider.GetRequiredService<INotificationIntentDeliveryDirectory>();
        var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var empty = await directory.FindByIdempotencyAsync(producerKey, idempotencyKey, cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(empty); Assert.AreEqual(intent.Id, empty.IntentId);
        Assert.AreEqual(0, empty.TotalDeliveryCount); Assert.IsNull(empty.NextAttemptAtUtc);
        Assert.IsNull(await directory.FindByIdempotencyAsync("other-producer", idempotencyKey, cancellationToken).ConfigureAwait(false));
        Assert.IsNull(await directory.FindByIdempotencyAsync(producerKey, "other-idempotency", cancellationToken).ConfigureAwait(false));
        tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "foreign-test", "隔离测试租户"));
        Assert.IsNull(await directory.FindByIdempotencyAsync(producerKey, idempotencyKey, cancellationToken).ConfigureAwait(false));
        SetOwner();

        var now = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        (string Status, DateTimeOffset? Next)[] states =
        [
            ("persisted", null), ("accepted", now.AddMinutes(3)), ("accepted", null),
            ("sent", now.AddDays(-1)), ("delivered", null), ("read", null),
            ("unknown", now.AddMinutes(1)), ("unknown", null), ("failed", null),
            ("suppressed", null), ("dead_lettered", null)
        ];
        var ids = new List<Guid>();
        var recipientIds = new List<Guid>();
        try
        {
            foreach (var state in states)
            {
                // 每个状态使用本夹具的独立收件人行，兼容 SQL Server 对 NULL 唯一组合的约束。
                // 聚合只读取投递状态；夹具收件人不解析地址或触发外部发送，最终精确清理。
                var recipientId = Guid.CreateVersion7(); recipientIds.Add(recipientId);
                Assert.AreEqual(1, await commands.ExecuteAsync(NotificationPlatformSql.InsertRecipient,
                    new Dictionary<string, object?>
                    {
                        ["Id"] = recipientId, ["IntentId"] = intent.Id,
                        ["RecipientTypeKey"] = "test-delivery-progress", ["RecipientKey"] = recipientId.ToString("N"),
                        ["UserId"] = null, ["ResolutionStatusKey"] = "pending", ["CreatedAtUtc"] = now
                    }, cancellationToken).ConfigureAwait(false));
                var id = Guid.CreateVersion7(); ids.Add(id);
                Assert.AreEqual(1, await commands.ExecuteAsync(NotificationPlatformSql.InsertDelivery,
                    new Dictionary<string, object?>
                    {
                        ["Id"] = id, ["IntentId"] = intent.Id, ["RecipientId"] = recipientId,
                        ["ChannelKey"] = "email", ["ProviderProfileVersionId"] = null, ["BindingVersionId"] = null,
                        ["StatusKey"] = state.Status, ["NextAttemptAtUtc"] = state.Next, ["CreatedAtUtc"] = now
                    }, cancellationToken).ConfigureAwait(false));
            }
            var progress = await directory.FindByIdempotencyAsync(producerKey, idempotencyKey, cancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(progress); Assert.AreEqual(intent.Id, progress.IntentId);
            Assert.AreEqual(11, progress.TotalDeliveryCount); Assert.AreEqual(3, progress.PendingDeliveryCount);
            Assert.AreEqual(1, progress.SentDeliveryCount); Assert.AreEqual(1, progress.FailedDeliveryCount);
            Assert.AreEqual(1, progress.DeadLetteredDeliveryCount); Assert.AreEqual(1, progress.UnknownDeliveryCount);
            Assert.AreEqual(0, progress.OtherDeliveryCount); Assert.AreEqual(1, progress.PersistedDeliveryCount);
            Assert.AreEqual(1, progress.DeliveredDeliveryCount); Assert.AreEqual(1, progress.ReadDeliveryCount);
            Assert.AreEqual(1, progress.SuppressedDeliveryCount); Assert.AreEqual(now.AddMinutes(1), progress.NextAttemptAtUtc);
            Assert.IsNull(await directory.FindByIdempotencyAsync("other-producer", idempotencyKey, cancellationToken).ConfigureAwait(false));
            Assert.IsNull(await directory.FindByIdempotencyAsync(producerKey, "other-idempotency", cancellationToken).ConfigureAwait(false));
            tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "foreign-test", "隔离测试租户"));
            Assert.IsNull(await directory.FindByIdempotencyAsync(producerKey, idempotencyKey, cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            SetOwner();
            // 只移除本次添加的精确行，后续原有场景仍看到原始意图与收件箱。
            if (ids.Count > 0) await commands.ExecuteAsync(new SqlStatement("notifications.tests.delivery.delete_owned",
                "DELETE FROM fn_notifications_delivery WHERE IntentId = @IntentId AND Id IN @Ids", SqlDataScope.Global),
                new Dictionary<string, object?> { ["IntentId"] = intent.Id, ["Ids"] = ids.ToArray() }, CancellationToken.None).ConfigureAwait(false);
            if (recipientIds.Count > 0) await commands.ExecuteAsync(new SqlStatement("notifications.tests.recipient.delete_owned",
                "DELETE FROM fn_notifications_recipient WHERE IntentId = @IntentId AND Id IN @Ids", SqlDataScope.Global),
                new Dictionary<string, object?> { ["IntentId"] = intent.Id, ["Ids"] = recipientIds.ToArray() }, CancellationToken.None).ConfigureAwait(false);
        }
        var restored = await directory.FindByIdempotencyAsync(producerKey, idempotencyKey, cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(restored); Assert.AreEqual(0, restored.TotalDeliveryCount); Assert.IsNull(restored.NextAttemptAtUtc);
    }

    internal static async Task VerifyCoexistenceAsync(IServiceProvider services, NotificationIntentResponse hostIntent,
        NotificationIntentResponse tenantIntent, Guid tenantId, CancellationToken cancellationToken = default)
    {
        Assert.AreNotEqual(hostIntent.Id, tenantIntent.Id);
        Assert.AreEqual(hostIntent.ProducerKey, tenantIntent.ProducerKey);
        Assert.AreEqual(hostIntent.IdempotencyKey, tenantIntent.IdempotencyKey);
        await using var scope = services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        var directory = scope.ServiceProvider.GetRequiredService<INotificationIntentDeliveryDirectory>();
        tenant.SetHost();
        var host = await directory.FindByIdempotencyAsync(hostIntent.ProducerKey, hostIntent.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(host); Assert.AreEqual(hostIntent.Id, host.IntentId);
        tenant.SetTenant(new TenantContext(tenantId, "progress-test", "通知聚合测试租户"));
        var own = await directory.FindByIdempotencyAsync(hostIntent.ProducerKey, hostIntent.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(own); Assert.AreEqual(tenantIntent.Id, own.IntentId);
        tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "foreign-test", "隔离测试租户"));
        Assert.IsNull(await directory.FindByIdempotencyAsync(hostIntent.ProducerKey, hostIntent.IdempotencyKey, cancellationToken).ConfigureAwait(false));
    }
}
