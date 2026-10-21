using Microsoft.Extensions.Options;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Serialization;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks.Persistence;
using Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;

/// <summary>Webhooks 模块应用服务；编排事务、权限与持久化。</summary>
namespace Full.NET.Modules.Webhooks.Features.DeliverWebhooks;

internal sealed class WebhookEventEnqueueService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator, IOptions<DatabaseOptions>? databaseOptions = null)
{
    public async Task EnqueueAsync(
        Guid tenantId,
        string eventType,
        Guid eventId,
        WebhookEventEnvelope payload,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await queryExecutor.QueryAsync<Guid>(
                databaseOptions?.Value.Provider == DatabaseProvider.MySql ? WebhookDeliverySql.ListTargetIdsMySql : WebhookDeliverySql.ListTargetIdsSqlServer,
                WebhookSqlParameters.Create(
                    ("TenantId", tenantId),
                    ("EventType", eventType)),
                cancellationToken)
            .ConfigureAwait(false);
        if (subscriptions.Count == 0)
        {
            return;
        }

        if (subscriptions.Count > 500)
            throw new InvalidOperationException("Webhook fanout exceeds the 500-target budget; subscription configuration must be corrected.");

        var payloadBody = JsonSerializer.Serialize(payload, WebhookPayloadJsonSerializerContext.Default.WebhookEventEnvelope);
        var payloadDigest = Delivery.WebhookSignatureHelper.ComputePayloadDigest(payloadBody);
        var now = clock.UtcNow;
        var rows = subscriptions.Select(subscriptionId => new WebhookDeliveryInsertRow(idGenerator.NewId(), subscriptionId)).ToArray();
        await commandExecutor.ExecuteAsync(databaseOptions?.Value.Provider == DatabaseProvider.MySql
                ? WebhookDeliverySql.InsertDeliveryBatchMySql : WebhookDeliverySql.InsertDeliveryBatchSqlServer,
            WebhookSqlParameters.Create(("Rows", JsonSerializer.Serialize(rows,
                    WebhooksJsonSerializerContext.Default.WebhookDeliveryInsertRowArray)),
                ("EventId", eventId), ("PayloadBody", payloadBody), ("PayloadDigest", payloadDigest),
                ("Status", WebhookDeliveryStatuses.Pending), ("NowUtc", now)), cancellationToken).ConfigureAwait(false);
    }
}
/// <summary>应用生成的投递标识和订阅标识；事件正文在批量参数中只传一次。</summary>
internal sealed record WebhookDeliveryInsertRow(Guid Id, Guid SubscriptionId);
