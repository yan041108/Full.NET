using Full.NET.Abstractions.Messaging;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.Hosting;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Modules.Tenancy;

internal sealed class TenantProvisionedCacheInvalidationHandler(
    IIntegrationEventSerializer serializer,
    IFusionCache cache,
    IHostEnvironment environment) : IIntegrationEventHandler
{
    private static readonly string[] LegacyEventTypesValue =
        ["fullnet.tenancy.tenant-provisioned"];

    public string EventType => "fullnet.tenancy.tenant.provisioned";

    public IReadOnlyList<string> LegacyEventTypes => LegacyEventTypesValue;

    public int SchemaVersion => 1;

    public async Task HandleAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var integrationEvent = serializer
            .Deserialize<TenantProvisionedIntegrationEvent>(payload);
        var invalidationOptions = cache.DefaultEntryOptions.Duplicate();
        // Outbox 只有在跨节点失效广播已经完成后才能确认消息；否则 Worker 退出或作用域释放会丢失后台广播。
        invalidationOptions.AllowBackgroundBackplaneOperations = false;
        // 可靠消费者必须感知广播失败，交给 Outbox 释放租约并安排重试，禁止把远端未失效误记为已处理。
        invalidationOptions.ReThrowBackplaneExceptions = true;
        await cache
            .RemoveAsync(
                CacheKeyBuilder.TenantResolutionById(
                    environment.EnvironmentName,
                    integrationEvent.TenantId),
                invalidationOptions,
                token: cancellationToken)
            .ConfigureAwait(false);
        await cache
            .RemoveAsync(
                CacheKeyBuilder.TenantResolutionByDomain(
                    environment.EnvironmentName,
                    integrationEvent.Domain),
                invalidationOptions,
                token: cancellationToken)
            .ConfigureAwait(false);
        await cache
            .RemoveByTagAsync(
                CacheKeyBuilder.TenantTag(integrationEvent.TenantId),
                invalidationOptions,
                token: cancellationToken)
            .ConfigureAwait(false);
        await cache
            .RemoveByTagAsync(
                CacheKeyBuilder.DomainTag(integrationEvent.Domain),
                invalidationOptions,
                token: cancellationToken)
            .ConfigureAwait(false);
    }
}
