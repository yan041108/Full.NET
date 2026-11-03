using System.Net;
using System.Text;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Server.AspNetCore;
using Full.NET.Localization;

namespace Full.NET.Modules.Identity.Features.OidcEndSession;

/// <summary>标准 RP 发起退出：组件验证 hint/回调，用户确认后撤销当前浏览器中心会话。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapMethods(
        "/connect/logout", [HttpMethods.Get, HttpMethods.Post], HandleAsync)
        .WithName("identityOidcEndSession").WithTags("IdentityOidcProtocol").AllowAnonymous();

    private static async Task<IResult> HandleAsync(HttpContext context, IAntiforgery antiforgery,
        IdentityOidcAuthorizationService authorization, ILocaleNormalizer locales, ILocaleContext locale,
        CancellationToken cancellationToken)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict request cannot be resolved.");
        IdentityOidcPages.Secure(context);
        if (HttpMethods.IsPost(context.Request.Method) && context.Request.HasFormContentType
            && context.Request.Form["confirm"] == "yes")
        {
            // GET 与 RP 的普通 POST 只展示确认；防伪验证必须早于撤销或清除 Cookie。
            try { await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            await authorization.SignOutCurrentCenterAsync(context, cancellationToken).ConfigureAwait(false);
            return Results.SignOut(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        return Results.Content(IdentityOidcPages.Resolve(request.UiLocales, locales, locale)
            .Logout(request, antiforgery.GetAndStoreTokens(context)), "text/html; charset=utf-8", Encoding.UTF8);
    }
}
