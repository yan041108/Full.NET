using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.DataScope;

/// <summary>将有效数据范围投影为机构单元 SQL 过滤。</summary>
internal sealed class DataScopeSqlFilterBuilder : IDataScopeSqlFilterBuilder
{
    public DataScopeSqlFilter? BuildOrganizationUnitFilter(
        EffectiveUserDataScope scope,
        string unitIdColumn,
        Guid currentUserId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(unitIdColumn);

        if (scope.IsUnrestricted)
        {
            return null;
        }

        var fragment = RoleDataScopeProjection.BuildUnionOrganizationUnitFilter(
            scope.RoleScopes,
            unitIdColumn,
            currentUserId);
        return fragment is null
            ? null
            : new DataScopeSqlFilter(fragment.Sql, fragment.Parameters);
    }
}
