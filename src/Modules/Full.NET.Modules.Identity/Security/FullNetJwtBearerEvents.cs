using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// 在令牌密码学验证完成后补充会话实时校验，避免仅依赖访问令牌有效期延迟撤销。
/// </summary>
internal sealed class FullNetJwtBearerEvents(
    AccessSessionValidator sessionValidator,
    ICurrentTenantContextWriter tenantContextWriter,
    IOptions<IdentityOidcOptions> oidcOptions) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        EstablishHostContextForOidcAccessToken(context.Principal);
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

    private void EstablishHostContextForOidcAccessToken(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return;
        }

        var issuer = principal.FindFirstValue(JwtRegisteredClaimNames.Iss);
        var hasApplicationSession = !string.IsNullOrWhiteSpace(
            principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId));
        if (!hasApplicationSession
            && (!oidcOptions.Value.Enable
                || string.IsNullOrWhiteSpace(oidcOptions.Value.Issuer)
                || !string.Equals(issuer, oidcOptions.Value.Issuer, StringComparison.Ordinal)))
        {
            return;
        }

        // 当前 OIDC 授权流仅签发 Host 会话；在 HostOnly 会话 SQL 查询前必须切换到 Host 上下文。
        tenantContextWriter.SetHost();
    }
}
