using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Authorization;

internal static class AuthorizationEndpointExtensions
{
    public static RouteHandlerBuilder RequireFullNetPermission(
        this RouteHandlerBuilder builder,
        string permissionCode)
    {
        return builder.RequireAuthorization(
            FullNetPermissionPolicyProvider.CreatePolicyName(permissionCode));
    }

    /// <summary>
    /// 开放读取 Endpoint：在 Smart（Bearer/ApiKey）之外显式接受请求签名认证。
    /// </summary>
    public static RouteHandlerBuilder RequireOpenAccessAuthentication(
        this RouteHandlerBuilder builder,
        string permissionCode)
    {
        return builder.RequireAuthorization(
            FullNetPermissionPolicyProvider.CreateOpenAccessPolicyName(permissionCode));
    }
}
