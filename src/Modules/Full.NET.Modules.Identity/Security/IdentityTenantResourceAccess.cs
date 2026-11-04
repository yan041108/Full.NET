using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Security;

/// <summary>租户业务授权实时核对统一账号准入；成员退出不撤销其全局账号或其他租户会话。</summary>
internal sealed class IdentityTenantResourceAccess(IQueryExecutor queries, ICurrentTenant tenant)
{
    private static readonly SqlStatement FindAccess = new("identity.tenant_resource.find_access", """
        SELECT 1 FROM fn_identity_user AS actor
        WHERE actor.Id = @UserId AND actor.IsActive = 1 AND actor.ScopeKey = 'host' AND actor.TenantId IS NULL
          AND (EXISTS (SELECT 1 FROM fn_identity_tenant_member AS membership
                WHERE membership.UserId = actor.Id AND membership.TenantId = @TenantId AND membership.Status = 'Active')
            OR EXISTS (SELECT 1 FROM fn_identity_user_role AS assignment
                INNER JOIN fn_identity_role AS roleObject ON roleObject.Id = assignment.RoleId
                WHERE assignment.UserId = actor.Id AND roleObject.ScopeKey = 'host' AND roleObject.TenantId IS NULL
                  AND roleObject.IsActive = 1 AND (roleObject.IsSuperAdministrator = 1 OR EXISTS
                    (SELECT 1 FROM fn_identity_role_permission AS permission WHERE permission.RoleId = roleObject.Id
                     AND permission.PermissionCode = 'tenancy.tenants.switch'))))
        """, SqlDataScope.Global);

    /// <summary>仅恢复动作可脱离成员关系；业务权限集合中只要有其他权限就必须核对当前租户。</summary>
    internal async Task<bool> IsAllowedAsync(ClaimsPrincipal principal, IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken = default)
    {
        if (permissions.Count == 0 || permissions.Any(string.IsNullOrWhiteSpace)
            || principal.Identity?.IsAuthenticated != true) return false;
        var scope = principal.FindFirstValue(IdentityClaimTypes.Scope);
        var actorScope = principal.FindFirstValue(IdentityClaimTypes.ActorScope);
        var tenantClaim = principal.FindFirstValue(IdentityClaimTypes.TenantId);
        if (tenant.IsHost) return scope == "host" && actorScope == "host" && string.IsNullOrEmpty(tenantClaim);
        if (!tenant.IsAvailable || tenant.Id is not { } tenantId || tenantId == Guid.Empty
            || !Guid.TryParse(tenantClaim, out var claimedTenant) || claimedTenant != tenantId
            || scope != $"tenant:{tenantId:N}") return false;

        // 本地租户账号和租户 API Key 的归属已由各自认证管线验证，不冒充统一 Host 账号成员。
        if (actorScope == $"tenant:{tenantId:N}") return true;
        if (actorScope == "tenant" && principal.Identity.AuthenticationType is "FullNET.ApiKey" or "FullNET.Signature") return true;
        if (actorScope != "host" || !Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject), out var userId)
            || userId == Guid.Empty) return false;

        // 本人 Removed 回执与上下文切换保留；切换目的地仍由上下文服务独立核对准入。
        if (permissions.All(code => code is IdentityTenantMembershipPermissions.LeaveSelf or "tenancy.tenants.switch")) return true;

        // 每次授权只查一个可信租户的标量事实，不缓存撤权结果，也不枚举账号的全部成员关系。
        return await IsActorAllowedAsync(userId, actorScope, tenantId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>已被权威会话校验的后台/连接绑定按其冻结租户检查，不读取或修改当前 HTTP 上下文。</summary>
    internal async Task<bool> IsActorAllowedAsync(Guid userId, string actorScope, Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId == Guid.Empty || tenantId == Guid.Empty) return false;
        if (tenantId is not { } id) return actorScope == "host";
        if (actorScope == $"tenant:{id:N}") return true;
        if (actorScope != "host") return false;
        return await queries.QuerySingleOrDefaultAsync<int>(FindAccess,
            IdentitySqlParameters.Create(("UserId", userId), ("TenantId", id)), cancellationToken).ConfigureAwait(false) == 1;
    }
}
