using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Authorization;

namespace Full.NET.Modules.Identity.Authorization;

internal sealed class FullNetPermissionHandler(PermissionClaimEvaluator evaluator)
    : AuthorizationHandler<FullNetPermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        FullNetPermissionRequirement requirement)
    {
        if (evaluator.HasPermission(context.User, requirement.PermissionCode))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
