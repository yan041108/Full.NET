using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Persistence;

/// <summary>
/// 从权威数据库解析租户准入状态和域名绑定。请求、上下文切换与工具授权共用此边界，
/// 不读取或回填 L1/L2，避免通知丢失、负缓存和迟到回填延长撤销窗口。
/// 查询失败及取消直接传播；已经通过检查的在途操作不在此处主动中止。
/// </summary>
internal sealed class TenantResolver(IQueryExecutor queryExecutor) : ITenantResolver, IActiveTenantContextResolver
{
    public async Task<TenantSummary?> ResolveByDomainAsync(
        string domain,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedDomain = domain.Trim().TrimEnd('.').ToLowerInvariant();
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedDomain);
        var row = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
            TenantSql.FindByDomain,
            TenancySqlParameters.Create(("Domain", normalizedDomain)),
            cancellationToken).ConfigureAwait(false);
        return row?.ToSummary();
    }

    public async Task<TenantSummary?> ResolveByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantId == Guid.Empty) return null;
        var row = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
            TenantSql.FindById,
            TenancySqlParameters.Create(("TenantId", tenantId)),
            cancellationToken).ConfigureAwait(false);
        return row?.ToSummary();
    }

    public async Task<TenantContext?> ResolveActiveByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return tenant is { IsActive: true }
            ? new TenantContext(tenant.Id, tenant.Identifier, tenant.Name)
            : null;
    }

    public async Task<IReadOnlyList<TenantSummary>> GetAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<TenantResolutionRecord>(
            TenantSql.GetAvailable,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return rows.Select(row => row.ToSummary()).ToArray();
    }
}
