using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.FieldProjection;

/// <summary>
/// 把有效角色授权收敛到代码目录；数据库脏行、退役字段和跨作用域角色均不能扩大结果。
/// </summary>
internal sealed class UserFieldProjectionResolver(
    IQueryExecutor queryExecutor,
    FieldProjectionCatalog catalog) : IUserFieldProjectionResolver
{
    public async Task<UserFieldProjection> ResolveAsync(
        Guid userId,
        Guid? tenantId,
        string resourceKey,
        CancellationToken cancellationToken = default)
    {
        var resource = catalog.GetRequiredResource(resourceKey);
        var rows = await queryExecutor.QueryAsync<UserFieldProjectionGrantRow>(
                IdentitySql.GetUserFieldProjectionGrants,
                new { UserId = userId, ResourceKey = resourceKey },
                cancellationToken)
            .ConfigureAwait(false);

        var effectiveKeys = resource.Fields
            .Where(field =>
                field.DefaultVisibility == FieldProjectionDefaultVisibility.Mandatory)
            .Select(field => field.FieldKey)
            .ToHashSet(StringComparer.Ordinal);
        var assignableKeys = resource.Fields
            .Where(field => field.Assignable)
            .Select(field => field.FieldKey)
            .ToHashSet(StringComparer.Ordinal);

        var validRows = rows.Where(row => IsMatchingScope(resourceKey, tenantId, row));
        if (validRows.Any(row => row.IsSuperAdministrator))
        {
            effectiveKeys.UnionWith(assignableKeys);
        }
        else
        {
            effectiveKeys.UnionWith(validRows
                .Select(row => row.FieldKey)
                .OfType<string>()
                .Where(assignableKeys.Contains));
        }

        return new UserFieldProjection(
            resourceKey,
            effectiveKeys.Order(StringComparer.Ordinal).ToArray());
    }

    private static bool IsMatchingScope(
        string resourceKey,
        Guid? tenantId,
        UserFieldProjectionGrantRow row) =>
        resourceKey switch
        {
            FieldProjectionResourceKeys.HostUsers =>
                tenantId is null
                && string.Equals(row.ScopeKey, "host", StringComparison.Ordinal)
                && row.TenantId is null,
            _ => false,
        };
}

internal sealed record UserFieldProjectionGrantRow(
    string ScopeKey,
    Guid? TenantId,
    bool IsSuperAdministrator,
    string? FieldKey);
