using System.Net;
using System.Security.Claims;
using System.Text;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Full.NET.Modules.Identity.Features.OidcAuthorization;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints, IdentityOidcOptions options)
    {
        if (!options.Enable)
        {
            return;
        }

        endpoints.MapMethods(
            "/connect/authorize",
            [HttpMethods.Get, HttpMethods.Post],
            HandleAuthorizeAsync)
            .WithName("identityOidcAuthorize")
            .WithTags("IdentityOidcProtocol");
    }

    private static async Task<IResult> HandleAuthorizeAsync(
        HttpContext httpContext,
        IdentityOidcAuthorizationService authorizationService,
        IOptions<IdentityOidcOptions> oidcOptions,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict request cannot be resolved.");
        ClaimsPrincipal? centerPrincipal = null;
        if (httpContext.Request.Method == HttpMethods.Post
            && !string.IsNullOrWhiteSpace(httpContext.Request.Form["username"])
            && !string.IsNullOrWhiteSpace(httpContext.Request.Form["password"]))
        {
            var signInResult = await authorizationService.SignInCenterAsync(
                    httpContext.Request.Form["username"]!,
                    httpContext.Request.Form["password"]!,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!signInResult.Succeeded)
            {
                return Results.Content(
                    BuildLoginPage(request, "Invalid username or password."),
                    "text/html; charset=utf-8",
                    Encoding.UTF8,
                    (int)HttpStatusCode.Unauthorized);
            }

            centerPrincipal = signInResult.Principal;
            await httpContext.SignInAsync(
                    IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme,
                    signInResult.Principal!,
                    signInResult.Properties)
                .ConfigureAwait(false);
        }

        centerPrincipal ??= (await httpContext.AuthenticateAsync(
                    IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)
                .ConfigureAwait(false))
            .Principal;
        if (centerPrincipal is null)
        {
            return Results.Content(
                BuildLoginPage(request, null),
                "text/html; charset=utf-8",
                Encoding.UTF8);
        }

        var principal = await authorizationService.CreateAuthorizationPrincipalAsync(
                centerPrincipal,
                request,
                cancellationToken)
            .ConfigureAwait(false);
        if (principal is null)
        {
            return Results.Forbid(
                authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        return Results.SignIn(
            principal,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static string BuildLoginPage(OpenIddictRequest request, string? errorMessage)
    {
        var builder = new StringBuilder();
        builder.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>Sign in</title></head><body>");
        builder.Append("<h1>Identity Center</h1>");
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            builder.Append("<p style=\"color:#b00020\">")
                .Append(WebUtility.HtmlEncode(errorMessage))
                .Append("</p>");
        }

        builder.Append("<form method=\"post\" action=\"/connect/authorize\">");
        builder.Append("<input type=\"hidden\" name=\"client_id\" value=\"")
            .Append(WebUtility.HtmlEncode(request.ClientId))
            .Append("\"/>");
        builder.Append("<input type=\"hidden\" name=\"redirect_uri\" value=\"")
            .Append(WebUtility.HtmlEncode(request.RedirectUri))
            .Append("\"/>");
        builder.Append("<input type=\"hidden\" name=\"response_type\" value=\"")
            .Append(WebUtility.HtmlEncode(request.ResponseType))
            .Append("\"/>");
        builder.Append("<input type=\"hidden\" name=\"scope\" value=\"")
            .Append(WebUtility.HtmlEncode(request.Scope))
            .Append("\"/>");
        builder.Append("<input type=\"hidden\" name=\"state\" value=\"")
            .Append(WebUtility.HtmlEncode(request.State))
            .Append("\"/>");
        builder.Append("<input type=\"hidden\" name=\"nonce\" value=\"")
            .Append(WebUtility.HtmlEncode(request.Nonce))
            .Append("\"/>");
        builder.Append("<input type=\"hidden\" name=\"code_challenge\" value=\"")
            .Append(WebUtility.HtmlEncode(request.CodeChallenge))
            .Append("\"/>");
        builder.Append("<input type=\"hidden\" name=\"code_challenge_method\" value=\"")
            .Append(WebUtility.HtmlEncode(request.CodeChallengeMethod))
            .Append("\"/>");
        builder.Append("<label>Username <input name=\"username\" autocomplete=\"username\" required/></label><br/>");
        builder.Append("<label>Password <input name=\"password\" type=\"password\" autocomplete=\"current-password\" required/></label><br/>");
        builder.Append("<button type=\"submit\">Sign in</button></form></body></html>");
        return builder.ToString();
    }
}
