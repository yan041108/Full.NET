using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserUnits.Queries;

/// <summary>保留 Identity 权威范围解析与多角色并集，输出本租户的可见机构标识。</summary>
internal sealed class TenantUserUnitListScopeReader(
    IQueryExecutor queryExecutor,
    IUserDataScopeResolver scopeResolver,
    IDataScopeSqlFilterBuilder scopeFilterBuilder,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>返回 null 表示不受限，空数组表示无可见机构；两者不得混同。</summary>
    internal async Task<Guid[]?> ReadAsync(
        Guid currentUserId, bool isSuperAdministrator, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(currentUserId, isSuperAdministrator, cancellationToken)
            .ConfigureAwait(false);
        if (scope.IsUnrestricted)
        {
            return null;
        }

        // 自定义授权仍由 Identity 的组合器生成；跨模块展示 SQL 只接收解析结果，不复制权限表规则。
        var nonSubtree = scope.RoleScopes
            .Where(entry => entry.DataScopeKind != RoleDataScopeKinds.OrganizationSubtree).ToArray();
        var visible = new HashSet<Guid>();
        if (nonSubtree.Length > 0)
        {
            var filter = scopeFilterBuilder.BuildOrganizationUnitFilter(
                new EffectiveUserDataScope(false, nonSubtree), "Id", currentUserId);
            if (filter is null)
            {
                return null;
            }

            var statement = TenantScopedSqlComposer.ApplyDataScopeFilter(TenantUserUnitScopeSql.VisibleUnits, filter);
            var ids = await queryExecutor.QueryAsync<Guid>(statement,
                TenantScopedSqlComposer.MergeParameters(null, filter), cancellationToken).ConfigureAwait(false);
            visible.UnionWith(ids);
        }

        if (scope.RoleScopes.Any(entry => entry.DataScopeKind == RoleDataScopeKinds.OrganizationSubtree))
        {
            var statement = databaseOptions.Value.Provider switch
            {
                DatabaseProvider.SqlServer => TenantUserUnitScopeSql.SubtreeSqlServer,
                DatabaseProvider.MySql => TenantUserUnitScopeSql.SubtreeMySql,
                _ => throw new InvalidOperationException("The configured database provider is not supported."),
            };
            var ids = await queryExecutor.QueryAsync<Guid>(statement,
                OrganizationSqlParameters.Create(("DataScopeUserId", currentUserId)), cancellationToken).ConfigureAwait(false);
            visible.UnionWith(ids);
        }

        return visible.ToArray();
    }
}
