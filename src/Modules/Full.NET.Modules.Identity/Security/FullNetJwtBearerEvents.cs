using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// 在令牌密码学验证完成后补充会话实时校验，避免仅依赖访问令牌有效期延迟撤销。
/// </summary>
internal sealed class FullNetJwtBearerEvents(
    AccessSessionValidator sessionValidator) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var isValid = context.Principal is not null
            && await sessionValidator.IsValidAsync(
                    context.Principal,
                    context.HttpContext.RequestAborted)
                .ConfigureAwait(false);
        if (!isValid)
        {
            // 失败原因不进入认证响应，避免向调用方泄露账号或会话状态细节。
            context.Fail("The access session is no longer active.");
        }
    }
}
