using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Authorization;

internal sealed class PermissionSnapshotReader(
    IQueryExecutor queryExecutor,
    AuthorizationCatalog catalog) : IPermissionSnapshotReader
{
    /// <summary>
    /// 读取用户在指定身份作用域内的有效权限，并按授权目录过滤未知权限码。
    /// </summary>
    /// <param name="userId">待查询的用户标识。</param>
    /// <param name="scopeKey">当前身份作用域键。</param>
    /// <param name="tenantId">租户作用域下的租户标识；Host 作用域传入空值。</param>
    /// <param name="cancellationToken">用于取消数据库查询的令牌。</param>
    /// <returns>包含有效权限码和超级管理员状态的权限快照。</returns>
    public async Task<PermissionSnapshot> ReadAsync(
        Guid userId,
        string scopeKey,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var authorizationParameters = IdentitySqlParameters.Create(
            ("UserId", userId),
            ("ScopeKey", scopeKey),
            ("TenantId", tenantId));
        var authorization = await queryExecutor.QueryAsync<IdentityAuthorizationRow>(
                IdentitySql.GetUserAuthorization,
                authorizationParameters,
                cancellationToken)
            .ConfigureAwait(false);
        var requiredScope = tenantId.HasValue
            ? AuthorizationScope.Tenant
            : string.Equals(scopeKey, "host", StringComparison.Ordinal)
                ? AuthorizationScope.Host
                : AuthorizationScope.Tenant;
        var knownPermissions = catalog.Permissions
            .Where(permission => (permission.Scope & requiredScope) != 0)
            .ToArray();
        var isSuperAdministrator = authorization.Any(item =>
            item.IsSuperAdministrator);
        var knownCodes = knownPermissions
            .Select(permission => permission.Code)
            .ToHashSet(StringComparer.Ordinal);
        var permissions = isSuperAdministrator
            ? knownPermissions.Select(permission => permission.Code)
            : authorization
                .Select(item => item.PermissionCode)
                .Where(code => code is not null && knownCodes.Contains(code))
                .Select(code => code!);

        return new PermissionSnapshot(
            permissions
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray(),
            isSuperAdministrator);
    }
}
