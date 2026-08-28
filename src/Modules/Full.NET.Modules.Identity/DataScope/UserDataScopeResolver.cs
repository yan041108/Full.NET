using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.DataScope;

/// <summary>
/// 从持久化加载用户活动角色并归一化有效数据范围。
/// 解析顺序：
/// 1) 传入的 isSuperAdministrator（JWT 签名保证）为 true → 直接返回无限制；
/// 2) 活动角色列表中若任一行带 IsSuperAdministrator 标志（兜底保护）→ 无限制；
/// 3) 任意角色被授予 All 类数据范围 → 无限制；
/// 4) 否则返回全部活动角色的 DataScopeKind 集合，交由 SqlFilterBuilder
///    组合成具体的机构单元 SQL 过滤片段。
/// 安全边界：解析结果不区分角色优先级——任一角色提升即可拿到更大范围，
/// 因此写角色管理处必须保证不能被低权限用户自我提权。
/// </summary>
internal sealed class UserDataScopeResolver(IQueryExecutor queryExecutor) : IUserDataScopeResolver
{
    /// <summary>
    /// 解析指定用户在当前上下文下的有效数据范围。
    /// </summary>
    /// <param name="userId">目标用户 ID。</param>
    /// <param name="isSuperAdministrator">令牌中直接声明的超管状态；优先于角色判定。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<EffectiveUserDataScope> ResolveAsync(
        Guid userId,
        bool isSuperAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (isSuperAdministrator)
        {
            return new EffectiveUserDataScope(true, []);
        }

        var rows = await queryExecutor.QueryAsync<IdentityUserRoleDataScopeRow>(
                IdentitySql.GetUserActiveRoleDataScopes,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (rows.Any(row => row.IsSuperAdministrator))
        {
            return new EffectiveUserDataScope(true, []);
        }

        var roleScopes = rows
            .Select(row => new RoleDataScopeEntry(row.RoleId, row.DataScopeKind))
            .ToArray();
        if (roleScopes.Any(scope =>
                string.Equals(
                    scope.DataScopeKind,
                    RoleDataScopeKinds.All,
                    StringComparison.Ordinal)))
        {
            return new EffectiveUserDataScope(true, roleScopes);
        }

        return new EffectiveUserDataScope(false, roleScopes);
    }
}
