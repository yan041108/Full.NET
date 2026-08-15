using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.DataScope;

/// <summary>
/// 将 EffectiveUserDataScope 投影为可直接注入查询的机构单元 SQL 过滤片段与参数。
/// 安全要点：SQL 片段完全由参数占位符 + 受信任的 unitIdColumn 列名拼接，
/// 不拼接任何用户输入，避免 SQL Injection。超管/无限制场景返回 null 表示不加过滤。
/// </summary>
internal sealed class DataScopeSqlFilterBuilder(RoleDataScopeProjection projection)
    : IDataScopeSqlFilterBuilder
{
    /// <summary>
    /// 根据有效范围构造机构维度过滤；无限制或空集返回 null（由调用方解释为不过滤/无数据）。
    /// </summary>
    /// <param name="scope">UserDataScopeResolver 输出的有效范围。</param>
    /// <param name="unitIdColumn">查询表中外键指向机构单元的列名；必须来自代码常量，不得拼接用户输入。</param>
    /// <param name="currentUserId">当前用户 ID，用于 "仅本用户" 类范围。</param>
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

        var fragment = projection.BuildUnionOrganizationUnitFilter(
            scope.RoleScopes,
            unitIdColumn,
            currentUserId);
        return fragment is null
            ? null
            : new DataScopeSqlFilter(fragment.Sql, fragment.Parameters);
    }
}
