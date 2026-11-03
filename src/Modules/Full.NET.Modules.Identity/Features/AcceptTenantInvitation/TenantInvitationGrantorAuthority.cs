using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.AcceptTenantInvitation;

/// <summary>按签发根的可信身份重新核对授权，后台恢复不使用过期请求声明或会话缓存。</summary>
internal interface ITenantInvitationGrantorAuthority
{
    Task<bool> IsAuthorizedAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
}

internal sealed class TenantInvitationGrantorAuthority(IQueryExecutor queries) : ITenantInvitationGrantorAuthority
{
    private static readonly SqlStatement CanInvite = new("identity.tenant_invitation.grantor_authority", """
        SELECT 1 FROM fn_identity_user AS actor
        WHERE actor.Id = @UserId AND actor.IsActive = 1
          AND ((actor.ScopeKey = 'host' AND actor.TenantId IS NULL AND EXISTS (
            SELECT 1 FROM fn_identity_user_role AS assignment
            INNER JOIN fn_identity_role AS roleObject ON roleObject.Id = assignment.RoleId
            WHERE assignment.UserId = actor.Id AND roleObject.IsActive = 1
              AND roleObject.ScopeKey = 'host' AND roleObject.TenantId IS NULL
              AND (roleObject.IsSuperAdministrator = 1 OR EXISTS
                (SELECT 1 FROM fn_identity_role_permission WHERE RoleId = roleObject.Id AND PermissionCode = @PermissionCode))
          ) AND (EXISTS (
            SELECT 1 FROM fn_identity_user_role AS contextAssignment
            INNER JOIN fn_identity_role AS contextRole ON contextRole.Id = contextAssignment.RoleId
            WHERE contextAssignment.UserId = actor.Id AND contextRole.IsActive = 1
              AND contextRole.ScopeKey = 'host' AND contextRole.TenantId IS NULL
              AND (contextRole.IsSuperAdministrator = 1 OR EXISTS
                (SELECT 1 FROM fn_identity_role_permission WHERE RoleId = contextRole.Id AND PermissionCode = 'tenancy.tenants.switch'))
          ) OR EXISTS (
            SELECT 1 FROM fn_identity_tenant_member AS membership
            WHERE membership.TenantId = @TenantId AND membership.UserId = actor.Id AND membership.Status = 'Active'
          ))) OR ((actor.TenantId = @TenantId AND actor.ScopeKey = @TenantScopeKey) AND EXISTS (
            SELECT 1 FROM fn_identity_user_role AS assignment
            INNER JOIN fn_identity_role AS roleObject ON roleObject.Id = assignment.RoleId
            INNER JOIN fn_identity_role_permission AS permission ON permission.RoleId = roleObject.Id
            WHERE assignment.UserId = actor.Id AND roleObject.IsActive = 1
              AND roleObject.TenantId = @TenantId AND roleObject.ScopeKey = @TenantScopeKey
              AND permission.PermissionCode = @PermissionCode
          )))
        """, SqlDataScope.Global);

    public async Task<bool> IsAuthorizedAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
        await queries.QuerySingleOrDefaultAsync<int>(CanInvite, IdentitySqlParameters.Create(("UserId", userId),
            ("TenantId", tenantId), ("TenantScopeKey", $"tenant:{tenantId:N}"),
            ("PermissionCode", IdentityTenantMembershipPermissions.Invite)), cancellationToken).ConfigureAwait(false) == 1;
}
