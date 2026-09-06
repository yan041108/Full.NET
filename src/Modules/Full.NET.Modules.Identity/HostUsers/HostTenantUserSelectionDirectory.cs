using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.HostUsers;

/// <summary>在 Host 上下文中按显式租户标识投影 Identity 权威用户归属。</summary>
/// <param name="queryExecutor">受数据作用域保护的只读查询执行器。</param>
/// <param name="databaseOptions">当前数据库提供程序配置。</param>
internal sealed class HostTenantUserSelectionDirectory(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions) : IHostTenantUserSelectionDirectory
{
    /// <inheritdoc />
    public Task<PagedResult<HostTenantUserDirectoryEntry>> ListActiveTenantUsersAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ListCoreAsync(
            tenantId,
            page,
            pageSize,
            IdentitySql.CountHostTenantUserSelections,
            cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<HostTenantUserDirectoryEntry>> ListActiveTenantAdministratorsAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ListCoreAsync(
            tenantId,
            page,
            pageSize,
            IdentitySql.CountHostTenantAdministratorSelections,
            cancellationToken,
            administratorsOnly: true);

    private async Task<PagedResult<HostTenantUserDirectoryEntry>> ListCoreAsync(
        Guid tenantId,
        int page,
        int pageSize,
        SqlStatement countStatement,
        CancellationToken cancellationToken,
        bool administratorsOnly = false)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var scopeKey = BuildTenantScopeKey(tenantId);
        var parameters = IdentitySqlParameters.Create(
            ("TenantId", tenantId),
            ("TenantScopeKey", scopeKey),
            ("Offset", (page - 1) * pageSize),
            ("PageSize", pageSize));
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var listStatement = ResolveListStatement(administratorsOnly);
        var records = await queryExecutor.QueryAsync<HostTenantUserDirectoryRecord>(
                listStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<HostTenantUserDirectoryEntry>(
            records.Select(Map).ToArray(),
            page,
            pageSize,
            total);
    }

    private SqlStatement ResolveListStatement(bool administratorsOnly)
    {
        if (administratorsOnly)
        {
            return databaseOptions.Value.Provider switch
            {
                DatabaseProvider.SqlServer =>
                    IdentitySql.ListHostTenantAdministratorSelectionsSqlServer,
                DatabaseProvider.MySql =>
                    IdentitySql.ListHostTenantAdministratorSelectionsMySql,
                _ => throw new InvalidOperationException(
                    "The configured database provider is not supported."),
            };
        }

        return databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => IdentitySql.ListHostTenantUserSelectionsSqlServer,
            DatabaseProvider.MySql => IdentitySql.ListHostTenantUserSelectionsMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
    }

    private static HostTenantUserDirectoryEntry Map(HostTenantUserDirectoryRecord record) =>
        new(
            record.Id,
            record.Username,
            record.DisplayName,
            record.AccountType,
            record.IsActive,
            record.PreferredLocale);

    private static string BuildTenantScopeKey(Guid tenantId) => $"tenant:{tenantId:N}";
}
