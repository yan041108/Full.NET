using Microsoft.AspNetCore.Builder;

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
}
