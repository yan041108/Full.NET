using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Authorization;

namespace Full.NET.Modules.Identity.Authorization;

/// <summary>
/// 权限码授权 Handler。调用 PermissionClaimEvaluator.HasPermission 统一判断当前主体
/// 是否持有目标权限；超级管理员通过 Claim 动态投影获得全部在作用域内的权限，
/// 而非在令牌中膨胀式地列出权限码。
/// </summary>
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
