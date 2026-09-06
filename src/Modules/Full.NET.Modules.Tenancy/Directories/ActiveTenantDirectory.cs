using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Directories;

/// <summary>
/// 为其他模块提供活动租户存在性只读校验。
/// </summary>
internal sealed class ActiveTenantDirectory(IQueryExecutor queryExecutor)
    : IIdentityActiveTenantDirectory
{
    /// <inheritdoc />
    public async Task<bool> IsActiveTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById,
                TenancySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        return tenant is not null && tenant.IsActive;
    }
}
