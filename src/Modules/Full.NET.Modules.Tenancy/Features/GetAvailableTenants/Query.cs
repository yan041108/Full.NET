using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Tenancy.Features.GetAvailableTenants;

/// <summary>
/// 读取当前认证主体可切换到的租户上下文列表。
/// </summary>
internal sealed record Query(Guid UserId) : IQuery<TenantContextSummary[]>;

/// <summary>
/// 将租户解析器返回的可达租户集合投影为上下文切换所需的最小摘要。
/// </summary>
/// <param name="tenantResolver">负责依据当前会话解析可访问租户集合的解析器。</param>
/// <param name="accessReader">从 Identity 权威读取当前主体的成员范围及显式平台授权。</param>
internal sealed class Handler(ITenantResolver tenantResolver, IIdentityTenantContextAccess accessReader)
    : IQueryHandler<Query, TenantContextSummary[]>
{
    /// <summary>
    /// 读取并投影当前主体可切换的租户集合。
    /// </summary>
    /// <param name="query">由 Endpoint 从认证主体构造，不接受普通请求覆写用户标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>返回当前主体可见的租户上下文摘要数组。</returns>
    public async Task<Result<TenantContextSummary[]>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await accessReader.ReadAsync(query.UserId, cancellationToken).ConfigureAwait(false);
        if (!access.AllTenants && access.TenantIds.Count == 0)
            return Result<TenantContextSummary[]>.Success(Array.Empty<TenantContextSummary>());
        var allowedIds = access.TenantIds.ToHashSet();
        var tenants = await tenantResolver.GetAvailableAsync(cancellationToken)
            .ConfigureAwait(false);
        // 这里只向调用方暴露上下文切换真正需要的字段，避免把持久化记录细节泄漏到公开契约。
        return Result<TenantContextSummary[]>.Success(tenants.Where(tenant => access.AllTenants || allowedIds.Contains(tenant.Id))
            .Select(tenant => new TenantContextSummary(
                tenant.Id,
                tenant.Identifier,
                tenant.Name,
                tenant.Domain))
            .ToArray());
    }
}
