using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.GetAvailableTenants;

/// <summary>
/// 读取当前认证主体可切换到的租户上下文列表。
/// </summary>
internal sealed record Query : IQuery<TenantContextSummary[]>;

/// <summary>
/// 将租户解析器返回的可达租户集合投影为上下文切换所需的最小摘要。
/// </summary>
/// <param name="tenantResolver">负责依据当前会话解析可访问租户集合的解析器。</param>
internal sealed class Handler(ITenantResolver tenantResolver)
    : IQueryHandler<Query, TenantContextSummary[]>
{
    /// <summary>
    /// 读取并投影当前主体可切换的租户集合。
    /// </summary>
    /// <param name="query">当前查询对象；该查询不携带额外参数，访问范围完全来自认证上下文。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>返回当前主体可见的租户上下文摘要数组。</returns>
    public async Task<Result<TenantContextSummary[]>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var tenants = await tenantResolver.GetAvailableAsync(cancellationToken)
            .ConfigureAwait(false);
        // 这里只向调用方暴露上下文切换真正需要的字段，避免把持久化记录细节泄漏到公开契约。
        return Result<TenantContextSummary[]>.Success(tenants
            .Select(tenant => new TenantContextSummary(
                tenant.Id,
                tenant.Identifier,
                tenant.Name,
                tenant.Domain))
            .ToArray());
    }
}
