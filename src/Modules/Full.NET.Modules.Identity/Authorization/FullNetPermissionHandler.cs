using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Authorization;

namespace Full.NET.Modules.Identity.Authorization;

internal sealed class FullNetPermissionHandler
    : AuthorizationHandler<FullNetPermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        FullNetPermissionRequirement requirement)
    {
        var granted = context.User.Claims.Any(claim =>
            string.Equals(
                claim.Type,
                IdentityClaimTypes.Permission,
                StringComparison.Ordinal)
            && string.Equals(
                claim.Value,
                requirement.PermissionCode,
                StringComparison.Ordinal));
        if (granted)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
