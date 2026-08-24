using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using global::Dapper;

namespace Full.NET.Modules.Identity.Authorization;

internal sealed class PermissionSnapshotReader(
    IQueryExecutor queryExecutor,
    AuthorizationCatalog catalog) : IPermissionSnapshotReader
{
    public async Task<PermissionSnapshot> ReadAsync(
        Guid userId,
        string scopeKey,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var authorizationParameters = new DynamicParameters();
        authorizationParameters.Add("UserId", userId);
        authorizationParameters.Add("ScopeKey", scopeKey);
        authorizationParameters.Add("TenantId", tenantId);
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
