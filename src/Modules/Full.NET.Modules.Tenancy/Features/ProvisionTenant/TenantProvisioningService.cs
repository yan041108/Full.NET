using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.ProvisionTenant;

/// <summary>
/// 通过命令调度器在事务中开通租户并播种基线数据；准入读取权威数据库，
/// 不产生缓存专用事件，也不执行无消费者的提交后缓存清理。
/// </summary>
internal sealed class TenantProvisioningService(
    ICommandDispatcher dispatcher)
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


        return result;
    }
}
