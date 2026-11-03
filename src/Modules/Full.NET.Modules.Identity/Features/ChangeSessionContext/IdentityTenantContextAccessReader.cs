using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ChangeSessionContext;

/// <summary>默认自助权限不等于平台目录授权；跨租户范围只由持久化角色授权产生。</summary>
internal sealed class IdentityTenantContextAccessReader(IQueryExecutor queries) : IIdentityTenantContextAccess
{
    private static readonly SqlStatement GlobalAccess = new("identity.tenant_context.global_access", """
        SELECT 1 FROM fn_identity_user AS actor
        WHERE actor.Id = @UserId AND actor.IsActive = 1 AND actor.ScopeKey = 'host' AND actor.TenantId IS NULL
          AND EXISTS (SELECT 1 FROM fn_identity_user_role AS assignment
            INNER JOIN fn_identity_role AS roleObject ON roleObject.Id = assignment.RoleId
            WHERE assignment.UserId = actor.Id AND roleObject.ScopeKey = 'host' AND roleObject.TenantId IS NULL
              AND roleObject.IsActive = 1 AND (roleObject.IsSuperAdministrator = 1 OR EXISTS
                (SELECT 1 FROM fn_identity_role_permission AS permission WHERE permission.RoleId = roleObject.Id
                 AND permission.PermissionCode = 'tenancy.tenants.switch')))
        """, SqlDataScope.Global);
    private static readonly SqlStatement Memberships = new("identity.tenant_context.memberships", """
        SELECT membership.TenantId FROM fn_identity_tenant_member AS membership
        INNER JOIN fn_identity_user AS actor ON actor.Id = membership.UserId
        WHERE actor.Id = @UserId AND actor.IsActive = 1 AND actor.ScopeKey = 'host' AND actor.TenantId IS NULL
          AND membership.Status = 'Active'
        ORDER BY membership.TenantId
        """, SqlDataScope.Global);

    public async Task<IdentityTenantContextAccess> ReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var parameters = IdentitySqlParameters.Create(("UserId", userId));
        if (await queries.QuerySingleOrDefaultAsync<int>(GlobalAccess, parameters, cancellationToken).ConfigureAwait(false) == 1)
            return new(true, Array.Empty<Guid>());
        return new(false, await queries.QueryAsync<Guid>(Memberships, parameters, cancellationToken).ConfigureAwait(false));
    }
}
