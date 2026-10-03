using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Roles;

/// <summary>每次从 Identity 自有表读取候选，不缓存授权目录，不借 Workflow 扫描成员。</summary>
/// <param name="query">受数据作用域保护的只读执行器。</param>
/// <param name="database">成对分页语法的提供程序。</param>
/// <param name="tenant">认证管线建立的可信当前范围。</param>
internal sealed class ActiveRoleDirectory(IQueryExecutor query, IOptions<DatabaseOptions> database, ICurrentTenant tenant) : IActiveRoleDirectory
{
    /// <inheritdoc/>
    public async Task<PagedResult<ActiveRoleDirectoryEntry>> ListActiveRolesAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var offset = ((long)page - 1) * pageSize;
        if (page < 1 || pageSize is < 1 or > 100 || offset > 100_000)
            throw new ArgumentOutOfRangeException(nameof(page), "Invalid bounded role directory page.");
        var parameters = IdentitySqlParameters.Create(("Offset", offset), ("PageSize", pageSize));
        // 非 Host 上下文必须完整，不得在租户缺失时退回 Host 目录。
        if (!tenant.IsHost)
        {
            if (!tenant.IsAvailable || tenant.Id is not { } tenantId || tenantId == Guid.Empty)
                throw new TenantContextMissingException("identity.tenant_context_required");
            parameters.Add("TenantScopeKey", $"tenant:{tenantId:N}");
        }
        var statement = database.Value.Provider switch
        {
            DatabaseProvider.SqlServer => tenant.IsHost ? ActiveRoleDirectorySql.ListSqlServerHost : ActiveRoleDirectorySql.ListSqlServerTenant,
            DatabaseProvider.MySql => tenant.IsHost ? ActiveRoleDirectorySql.ListMySqlHost : ActiveRoleDirectorySql.ListMySqlTenant,
            _ => throw new InvalidOperationException("Unsupported role directory database provider."),
        };
        var total = await query.QuerySingleOrDefaultAsync<long>(tenant.IsHost ? ActiveRoleDirectorySql.CountHost : ActiveRoleDirectorySql.CountTenant,
            parameters, cancellationToken).ConfigureAwait(false);
        var rows = await query.QueryAsync<ActiveRoleDirectoryRow>(statement, parameters, cancellationToken).ConfigureAwait(false);
        return new(rows.Select(row => new ActiveRoleDirectoryEntry(row.Id, row.Code, row.Name)).ToArray(), page, pageSize, total);
    }
}
