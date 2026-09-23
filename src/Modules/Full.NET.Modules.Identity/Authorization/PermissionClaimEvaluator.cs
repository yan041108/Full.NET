using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Authorization;

/// <summary>
/// 统一解释已签名令牌中的普通权限与超级管理员能力，避免各入口形成不同的授权语义。
/// </summary>
internal sealed class PermissionClaimEvaluator(AuthorizationCatalog catalog)
{
    /// <summary>
    /// 判断当前令牌是否在有效作用域内拥有指定权限。
    /// </summary>
    public bool HasPermission(ClaimsPrincipal principal, string permissionCode)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);

        var effectiveScope = ReadEffectiveScope(principal);
        if (effectiveScope is null)
        {
            return false;
        }

        var permissionScopeMask = ResolvePermissionScopeMask(principal, effectiveScope.Value);
        var definition = catalog.Permissions.SingleOrDefault(permission =>
            string.Equals(permission.Code, permissionCode, StringComparison.Ordinal));
        if (definition is null || (definition.Scope & permissionScopeMask) == 0)
        {
            return false;
        }

        // Host 专属权限必须在 Host 请求上下文中授权；租户内超级管理员需先切回 Host，避免 HostOnly SQL 抛 500。
        if (definition.Scope == AuthorizationScope.Host
            && effectiveScope != AuthorizationScope.Host)
        {
            return false;
        }

        return IsSuperAdministrator(principal)
            || principal.FindAll(IdentityClaimTypes.Permission).Any(claim =>
                string.Equals(claim.Value, permissionCode, StringComparison.Ordinal));
    }

    /// <summary>
    /// 解析当前作用域内的完整权限快照；超级管理员动态跟随代码权限目录。
    /// </summary>
    public IReadOnlyList<string> ResolvePermissions(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var effectiveScope = ReadEffectiveScope(principal);
        if (effectiveScope is null)
        {
            return [];
        }

        var permissionScopeMask = ResolvePermissionScopeMask(principal, effectiveScope.Value);
        var permittedCodes = catalog.Permissions
            .Where(permission => (permission.Scope & permissionScopeMask) != 0)
            .Select(permission => permission.Code)
            .ToHashSet(StringComparer.Ordinal);
        var permissions = IsSuperAdministrator(principal)
            ? permittedCodes
            : principal.FindAll(IdentityClaimTypes.Permission)
                .Select(claim => claim.Value)
                .Where(permittedCodes.Contains)
                .ToHashSet(StringComparer.Ordinal);

        return permissions.OrderBy(code => code, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// 只信任令牌签名覆盖的布尔 Claim，不根据用户名、角色代码或客户端输入推断超级管理员。
    /// </summary>
    public static bool IsSuperAdministrator(ClaimsPrincipal principal) =>
        bool.TryParse(
            principal.FindFirstValue(IdentityClaimTypes.SuperAdministrator),
            out var enabled)
        && enabled;

    private static AuthorizationScope? ReadEffectiveScope(ClaimsPrincipal principal)
    {
        var scope = principal.FindFirstValue(IdentityClaimTypes.Scope);
        if (string.Equals(scope, "host", StringComparison.Ordinal))
        {
            return AuthorizationScope.Host;
        }

        return scope?.StartsWith("tenant:", StringComparison.Ordinal) == true
            ? AuthorizationScope.Tenant
            : null;
    }

    /// <summary>
    /// 超级管理员在租户上下文中仍需执行 Host 平台操作（导航、API 与 OIDC 切租户探针）。
    /// </summary>
    private static AuthorizationScope ResolvePermissionScopeMask(
        ClaimsPrincipal principal,
        AuthorizationScope effectiveScope) =>
        IsSuperAdministrator(principal)
            ? effectiveScope | AuthorizationScope.Host
            : effectiveScope;
}
