using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.DataScope;

/// <summary>从持久化加载用户活动角色并归一化有效数据范围。</summary>
internal sealed class UserDataScopeResolver(IQueryExecutor queryExecutor) : IUserDataScopeResolver
{
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
                new { UserId = userId },
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
