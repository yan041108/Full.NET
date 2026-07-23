using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Caching.Fusion;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.Hosting;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Modules.Tenancy.Features.ProvisionTenant;

internal sealed class TenantProvisioningService(
    ICommandDispatcher dispatcher,
    IFusionCache cache,
    IHostEnvironment environment)
    : ITenantProvisioningService
{
    public async Task<Result<TenantSummary>> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync<ProvisionTenantCommand, TenantSummary>(
                new ProvisionTenantCommand(
                    request.Identifier,
                    request.Name,
                    request.Domain),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess && result.Value is { } tenant)
        {
            // 事务提交后先精确摘除当前节点已知的解析 key，避免本节点在 Outbox 尚未消费前继续命中旧负缓存。
            await cache.RemoveAsync(
                    CacheKeyBuilder.TenantResolutionById(
                        environment.EnvironmentName,
                        tenant.Id),
                    token: cancellationToken)
                .ConfigureAwait(false);
            await cache.RemoveAsync(
                    CacheKeyBuilder.TenantResolutionByDomain(
                        environment.EnvironmentName,
                        tenant.Domain),
                    token: cancellationToken)
                .ConfigureAwait(false);

            // 同时保留 tag 失效，兼顾后续可能新增的别名 key 或同租户关联缓存。
            await cache.RemoveByTagAsync(
                    CacheKeyBuilder.TenantTag(tenant.Id),
                    token: cancellationToken)
                .ConfigureAwait(false);
            await cache.RemoveByTagAsync(
                    CacheKeyBuilder.DomainTag(tenant.Domain),
                    token: cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }
}
