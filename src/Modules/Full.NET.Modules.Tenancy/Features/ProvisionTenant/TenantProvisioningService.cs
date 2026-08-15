using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.ProvisionTenant;

/// <summary>
/// 租户开通编排服务。事务顺序：
/// 1) 通过 CommandDispatcher 派发 ProvisionTenantCommand 执行核心开通验证
///    （Domain/Identifier 唯一性、套餐校验）→ 事务内写入租户 → 播种租户基线数据
///    → 写 TenantProvisionedIntegrationEvent Outbox；
/// 2) 事务提交成功后调用 TenantCacheInvalidator.InvalidateAfterCommitAsync 直接失效
///    当前实例 L1 与共享 L2/Backplane，不再依赖 Outbox 做缓存专用消息排空；
/// 3) 失效路径必须忽略调用方 CancellationToken，否则客户端主动断开会让负缓存残留。
/// </summary>
internal sealed class TenantProvisioningService(
    ICommandDispatcher dispatcher,
    TenantCacheInvalidator cacheInvalidator)
    : ITenantProvisioningService
{
    /// <summary>
    /// 执行租户开通编排；返回开通后的 TenantSummary（包含新生成 ID）。
    /// </summary>
    /// <param name="request">开通请求，包含标识、名称、域名与套餐 ID。</param>
    /// <param name="cancellationToken">业务取消令牌；提交后的缓存失效不使用此令牌。</param>
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
