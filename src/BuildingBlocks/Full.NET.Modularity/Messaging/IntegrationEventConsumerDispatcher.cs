using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Modularity.Messaging;

/// <summary>
/// Kafka Consumer 本地事务管道：Inbox 声明、订阅处理、下游 Outbox 与 processed 标记同事务提交。
/// </summary>
public sealed class IntegrationEventConsumerDispatcher(
    ICommandTransaction commandTransaction,
    IIntegrationEventInbox inbox,
    IntegrationEventSubscriptionCatalog catalog,
    IEventStreamOwnershipGate ownershipGate,
    IEffectiveEventDeliveryOwnerResolver ownerResolver,
    CurrentTenantAccessor currentTenant,
    IEnumerable<IIntegrationEventHandlerRegistry>? handlerRegistries = null)
{
    public async Task<InboxConsumeResult> ConsumeAsync(
        string consumerName,
        IntegrationEventEnvelope envelope,
        IIntegrationEventSubscription handler,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(handler);

        if (!string.Equals(handler.ConsumerName, consumerName, StringComparison.Ordinal)
            || !string.Equals(handler.EventType, envelope.MessageType, StringComparison.Ordinal)
            || handler.SchemaVersion != envelope.SchemaVersion)
        {
            throw new InvalidOperationException(
                "The supplied integration event subscription does not match the consume route.");
        }

        var generated = TryResolveGenerated(
            envelope.MessageType,
            envelope.SchemaVersion,
            consumerName);
        if (generated is IntegrationEventHandlerDescriptor descriptor)
        {
            var registered = catalog.GetByHandlerTypeRequired(descriptor.HandlerType);
            if (!ReferenceEquals(registered, handler))
            {
                throw new InvalidOperationException(
                    "The supplied integration event subscription does not match the generated registration.");
            }
        }
        else
        {
            // 插件与测试订阅可能未启用生成器，显式 Catalog 是唯一兼容回退，不扫描程序集。
            var registered = catalog.GetRequired(
                consumerName,
                envelope.MessageType,
                envelope.SchemaVersion);
            if (!ReferenceEquals(registered, handler))
            {
                throw new InvalidOperationException(
                    "The supplied integration event subscription does not match the catalog registration.");
            }
        }

        RestoreTenantFromEnvelope(envelope);
        try
        {
            using var activity = IntegrationEventConsumerTelemetry.StartTransaction(
                consumerName,
                envelope.MessageType,
                envelope.SchemaVersion);
            return await commandTransaction.ExecuteAsync(
                    async ct => await ConsumeInTransactionAsync(
                        consumerName,
                        envelope,
                        handler,
                        ct).ConfigureAwait(false),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private IntegrationEventHandlerDescriptor? TryResolveGenerated(
        string messageType,
        int schemaVersion,
        string consumerName)
    {
        if (handlerRegistries is null)
        {
            return null;
        }

        foreach (var registry in handlerRegistries)
        {
            if (registry.TryResolve(
                    messageType,
                    schemaVersion,
                    consumerName,
                    out var descriptor))
            {
                return descriptor;
            }
        }

        return null;
    }

    private async Task<InboxConsumeResult> ConsumeInTransactionAsync(
        string consumerName,
        IntegrationEventEnvelope envelope,
        IIntegrationEventSubscription handler,
        CancellationToken cancellationToken)
    {
        var fence = await ownershipGate
            .AcquireConsumerFenceAsync(
                envelope.MessageType,
                envelope.SchemaVersion,
                cancellationToken)
            .ConfigureAwait(false);
        bool ownershipExists;
        EventDeliveryOwner deliveryOwner;
        if (fence.IsSupported)
        {
            ownershipExists = fence.OwnershipExists;
            deliveryOwner = catalog.ResolveDeliveryOwner(
                envelope.MessageType,
                envelope.SchemaVersion,
                fence.CurrentOwner);
        }
        else
        {
            // 第三方或测试 Gate 可继续使用旧接口；Full.NET Dapper 路径固定走单查询 Fence。
            ownershipExists = await ownershipGate
                .AcquireConsumerAsync(
                    envelope.MessageType,
                    envelope.SchemaVersion,
                    cancellationToken)
                .ConfigureAwait(false);
            deliveryOwner = await ownerResolver
                .GetDeliveryOwnerAsync(
                    envelope.MessageType,
                    envelope.SchemaVersion,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!ownershipExists || deliveryOwner is not EventDeliveryOwner.CdcKafka)
        {
            throw new EventDeliveryOwnershipRevokedException(
                envelope.MessageType,
                envelope.SchemaVersion,
                deliveryOwner);
        }

        var claim = await inbox
            .ClaimAsync(consumerName, envelope, cancellationToken)
            .ConfigureAwait(false);
        switch (claim.Status)
        {
            case InboxClaimStatus.AlreadyProcessed:
                return InboxConsumeResult.AlreadyProcessed;
            case InboxClaimStatus.PayloadMismatch:
                throw CreatePermanentException(
                    IntegrationEventFailureCodes.MessageIdPayloadMismatch,
                    "The inbox message identifier collides with a different payload hash.");
            case InboxClaimStatus.Claimed:
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported inbox claim status '{claim.Status}'.");
        }

        var context = new IntegrationEventContext(
            envelope.EventId,
            envelope.MessageType,
            envelope.SchemaVersion,
            envelope.TenantId,
            envelope.TraceParent,
            envelope.OccurredAtUtc);

        await handler
            .HandleAsync(context, envelope.Payload, cancellationToken)
            .ConfigureAwait(false);

        await inbox
            .MarkProcessedAsync(consumerName, envelope.EventId, cancellationToken)
            .ConfigureAwait(false);

        return InboxConsumeResult.Processed;
    }

    /// <summary>
    /// 仅在目录校验通过后，从可信 Envelope 恢复租户数据作用域。
    /// </summary>
    private void RestoreTenantFromEnvelope(IntegrationEventEnvelope envelope)
    {
        if (envelope.TenantId is Guid tenantId)
        {
            var identifier = tenantId.ToString("D");
            currentTenant.SetTenant(new TenantContext(tenantId, identifier, identifier));
            return;
        }

        currentTenant.SetHost();
    }

    private static IntegrationEventPermanentException CreatePermanentException(
        string code,
        string summary) =>
        new(new IntegrationEventFailure(
            IntegrationEventFailure.ResolveKind(code),
            code,
            summary));
}
