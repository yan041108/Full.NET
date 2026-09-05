using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.HostUsers;

/// <summary>从 Identity 权威用户归属与角色关系中解析当前可信 Tenant 的活动用户候选。</summary>
/// <param name="queryExecutor">受数据作用域保护的只读查询执行器。</param>
/// <param name="databaseOptions">当前数据库提供程序配置。</param>
/// <param name="currentTenant">由认证与租户中间件建立的可信当前租户。</param>
internal sealed class TenantUserSelectionDirectory(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    ICurrentTenant currentTenant) : ITenantUserSelectionDirectory
{
    /// <summary>分页读取当前 Tenant 直属用户或拥有当前 Tenant 活动角色的 Host 活动用户。</summary>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">受控单页数量。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>当前 Tenant 用户候选分页结果。</returns>
    public async Task<PagedResult<TenantUserDirectoryEntry>> ListActiveTenantUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var scope = ResolveScope();
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var parameters = IdentitySqlParameters.Create(
            ("TenantId", scope.TenantId),
            ("TenantScopeKey", scope.TenantScopeKey),
            ("Offset", (page - 1) * pageSize),
            ("PageSize", pageSize));
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountActiveTenantUserSelections,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => IdentitySql.ListActiveTenantUserSelectionsSqlServer,
            DatabaseProvider.MySql => IdentitySql.ListActiveTenantUserSelectionsMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var records = await queryExecutor.QueryAsync<HostUserDirectoryRecord>(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<TenantUserDirectoryEntry>(
            records.Select(Map).ToArray(),
            page,
            pageSize,
            total);
    }

    /// <summary>批量查找当前 Tenant 内仍处于活动状态的指定用户。</summary>
    /// <param name="userIds">待校验的稳定用户标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>当前 Tenant 内有效用户的去重字典。</returns>
    public async Task<IReadOnlyDictionary<Guid, TenantUserDirectoryEntry>> FindActiveTenantUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var distinctUserIds = userIds.Distinct().ToArray();
        if (distinctUserIds.Length == 0)
        {
            return new Dictionary<Guid, TenantUserDirectoryEntry>();
        }

        var scope = ResolveScope();
        var records = await queryExecutor.QueryAsync<HostUserDirectoryRecord>(
                IdentitySql.ListActiveTenantUserSelectionsByIds,
                IdentitySqlParameters.Create(
                    ("TenantId", scope.TenantId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserIds", distinctUserIds)),
                cancellationToken)
            .ConfigureAwait(false);
        return records.ToDictionary(record => record.Id, Map);
    }

    /// <summary>将数据库投影转换为不泄露身份内部字段的最小 Contract。</summary>
    /// <param name="record">Identity 内部用户目录投影。</param>
    /// <returns>Tenant 用户候选投影。</returns>
    private static TenantUserDirectoryEntry Map(HostUserDirectoryRecord record) =>
        new(record.Id, record.Username, record.DisplayName, record.PreferredLocale);

    /// <summary>从可信上下文解析 TenantId 与规范作用域键。</summary>
    /// <returns>当前 Tenant 的强类型作用域。</returns>
    /// <exception cref="TenantContextMissingException">当前请求不处于有效 Tenant 上下文时抛出。</exception>
    private TenantSelectionScope ResolveScope()
    {
        if (!currentTenant.IsHost && currentTenant.IsAvailable && currentTenant.Id is { } tenantId)
        {
            return new TenantSelectionScope(tenantId, $"tenant:{tenantId:N}");
        }

        throw new TenantContextMissingException("identity.tenant_context_required");
    }

    /// <summary>封装由可信上下文派生的 Tenant 查询参数。</summary>
    /// <param name="TenantId">当前租户标识。</param>
    /// <param name="TenantScopeKey">规范租户作用域键。</param>
    private readonly record struct TenantSelectionScope(Guid TenantId, string TenantScopeKey);
}
