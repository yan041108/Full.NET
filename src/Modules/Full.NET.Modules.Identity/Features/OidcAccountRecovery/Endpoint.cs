using Full.NET.Localization;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.OidcAccountRecovery;

/// <summary>预先生成的恢复码是重置凭据；未验证的资料邮箱不作为账号恢复身份。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapMethods("/connect/recover",
        [HttpMethods.Get, HttpMethods.Post], async (HttpContext context, IAntiforgery antiforgery,
            ILocaleNormalizer locales, ILocaleContext locale, IdentityOidcAccountService accounts, CancellationToken token) =>
        {
            IdentityOidcPages.Secure(context);
            var preferred = context.Request.Query["ui_locales"].ToString();
            string? message = null;
            if (HttpMethods.IsPost(context.Request.Method))
            {
                try { await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false); }
                catch (AntiforgeryValidationException) { return Results.BadRequest(); }
                preferred = context.Request.Form["ui_locales"].ToString();
                var result = await accounts.RecoverAsync(context.Request.Form["username"]!, context.Request.Form["recovery_code"]!,
                    context.Request.Form["new_password"]!, token).ConfigureAwait(false);
                message = IdentityOidcPages.Resolve(preferred, locales, locale).Text(result.IsSuccess ? "passwordChanged" : "invalid");
                if (result.IsSuccess)
                    await context.SignOutAsync(IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
            }
            return Results.Content(IdentityOidcPages.Resolve(preferred, locales, locale)
                .Recovery(antiforgery.GetAndStoreTokens(context), message), "text/html; charset=utf-8");
        }).AllowAnonymous().WithName("identityOidcAccountRecovery").RequireRateLimiting("identity-login");
}
