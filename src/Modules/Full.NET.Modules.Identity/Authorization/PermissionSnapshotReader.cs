using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Authorization;

internal sealed class PermissionSnapshotReader(
    IQueryExecutor queryExecutor,
    AuthorizationCatalog catalog) : IPermissionSnapshotReader
{
    public async Task<IReadOnlyList<string>> ReadAsync(
        Guid userId,
        string scopeKey,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var grantedCodes = await queryExecutor.QueryAsync<string>(
                IdentitySql.GetUserPermissionCodes,
                new { UserId = userId, ScopeKey = scopeKey, TenantId = tenantId },
                cancellationToken)
            .ConfigureAwait(false);
        var requiredScope = string.Equals(
            scopeKey,
            "host",
            StringComparison.Ordinal)
            ? AuthorizationScope.Host
            : AuthorizationScope.Tenant;
        var knownCodes = catalog.Permissions
            .Where(permission => (permission.Scope & requiredScope) != 0)
            .Select(permission => permission.Code)
            .ToHashSet(StringComparer.Ordinal);

        return grantedCodes
            .Where(knownCodes.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
    }
}
