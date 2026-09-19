using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Serialization;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks.Persistence;
using Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;

namespace Full.NET.Modules.Webhooks.Features.DeliverWebhooks;

internal sealed class WebhookEventEnqueueService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task EnqueueAsync(
        Guid tenantId,
        string eventType,
        Guid eventId,
        WebhookEventEnvelope payload,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await queryExecutor.QueryAsync<WebhookSubscriptionDeliveryRecord>(
                WebhookDeliverySql.ListActiveSubscriptionsByEventType,
                WebhookSqlParameters.Create(
                    ("TenantId", tenantId),
                    ("EventType", eventType)),
                cancellationToken)
            .ConfigureAwait(false);
        if (subscriptions.Count == 0)
        {
            return;
        }

        var payloadBody = JsonSerializer.Serialize(payload, WebhookPayloadJsonSerializerContext.Default.WebhookEventEnvelope);
        var payloadDigest = Delivery.WebhookSignatureHelper.ComputePayloadDigest(payloadBody);
        var now = clock.UtcNow;
        foreach (var subscription in subscriptions)
        {
            var deliveryId = idGenerator.NewId();
            try
            {
                await commandExecutor.ExecuteAsync(
                        WebhookDeliverySql.InsertDelivery,
                        WebhookSqlParameters.Create(
                            ("Id", deliveryId),
                            ("SubscriptionId", subscription.Id),
                            ("EventId", eventId),
                            ("PayloadBody", payloadBody),
                            ("PayloadDigest", payloadDigest),
                            ("Status", WebhookDeliveryStatuses.Pending),
                            ("NextAttemptAtUtc", now),
                            ("CreatedAtUtc", now),
                            ("UpdatedAtUtc", now)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsDuplicateDelivery(exception))
            {
                // Outbox 重试时同一 SubscriptionId + EventId 可能重复入队，保持幂等。
            }
        }
    }

    private static bool IsDuplicateDelivery(Exception exception) =>
        exception.Message.Contains("UX_fn_webhooks_delivery_EventSubscription", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase);
}
