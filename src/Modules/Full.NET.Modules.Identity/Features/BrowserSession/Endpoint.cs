using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Client.AspNetCore;
using Full.NET.Hosting.Api;

namespace Full.NET.Modules.Identity.Features.BrowserSession;

/// <summary>同源浏览器边界：协议验证留在服务端，HTTP 响应不返回 OAuth 凭据。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints, IdentityBrowserOptions options)
    {
        if (!options.Enable) return;
        endpoints.MapGet("/api/v1/identity/browser/login", () => Results.Challenge(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictClientAspNetCoreConstants.Properties.RegistrationId] = IdentityBrowserSession.RegistrationId
            }) { RedirectUri = "/#/identity/oidc/callback" },
            [OpenIddictClientAspNetCoreDefaults.AuthenticationScheme]))
            .AllowAnonymous().WithName("identityBrowserLogin").RequireRateLimiting("identity-login");
        endpoints.MapGet("/api/v1/identity/browser/callback", async (HttpContext context,
            IdentityBrowserSession session, CancellationToken token) =>
        {
            var result = await context.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme)
                .ConfigureAwait(false);
            if (!result.Succeeded || result.Principal?.Identity?.IsAuthenticated != true || result.Properties is null)
                return Results.Unauthorized();
            await session.CreateAsync(context,
                result.Properties.GetTokenValue(OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessToken)!,
                result.Properties.GetTokenValue(OpenIddictClientAspNetCoreConstants.Tokens.RefreshToken)!,
                result.Properties.GetTokenValue(OpenIddictClientAspNetCoreConstants.Tokens.BackchannelIdentityToken)!, token)
                .ConfigureAwait(false);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Results.LocalRedirect("/#/identity/oidc/callback");
        }).AllowAnonymous().WithName("identityBrowserCallback");
        endpoints.MapGet("/api/v1/identity/browser/session", (HttpContext context, IdentityBrowserSession session) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return session.Ticket is null ? Results.Unauthorized()
                : Results.Ok(new IdentityBrowserSessionResponse(session.ExpiresAtUtc, session.Ticket.CsrfToken));
        }).RequireAuthorization().WithName("identityBrowserSession");
        endpoints.MapPost("/api/v1/identity/browser/logout", async (HttpContext context,
            IdentityBrowserSession browser, CancellationToken token) =>
        {
            if (!browser.IsActive || !Guid.TryParse(context.User.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId), out var id))
                return Results.Unauthorized();
            await browser.LogoutAsync(context, id, Guid.Parse(context.User.FindFirstValue("sub")!), token).ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization().WithName("identityBrowserLogout");
        endpoints.MapPost("/api/v1/identity/browser/password", async (ChangePasswordRequest request, HttpContext context,
            IdentityBrowserSession browser, IdentityOidcAccountService accounts, IApiResultMapper mapper, CancellationToken token) =>
        {
            if (!browser.IsActive || !Guid.TryParse(context.User.FindFirstValue("sub"), out var userId)) return Results.Unauthorized();
            var changed = await accounts.ChangeAuthenticatedAsync(userId, request.CurrentPassword, request.NewPassword, token)
                .ConfigureAwait(false);
            if (!changed.IsSuccess) return mapper.Map(changed, context);
            await browser.DeleteCookieSessionAsync(context, token).ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization().WithName("identityBrowserChangePassword")
            .RequireRateLimiting(IdentityModule.SessionMutationRateLimitPolicy);
    }
}
