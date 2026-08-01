using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.ProvisionTenant;

internal sealed class TenantProvisioningService(
    ICommandDispatcher dispatcher,
    TenantCacheInvalidator cacheInvalidator)
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
                    request.Domain,
                    request.TenantPackageId),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess && result.Value is { } tenant)
        {
            // 业务已提交：不得把请求取消令牌传给失效路径，否则客户端断开会使负缓存残留。
            await cacheInvalidator.InvalidateAfterCommitAsync(
                    tenant.Id,
                    tenant.Domain,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }
}
