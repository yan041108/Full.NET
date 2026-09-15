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
        IdentityOidcClientConfigResolver clientConfigResolver,
        IOptions<IdentityOidcOptions> oidcOptions,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict request cannot be resolved.");
        if (!string.IsNullOrWhiteSpace(request.ClientId)
            && await clientConfigResolver.IsDisabledAsync(request.ClientId, cancellationToken)
                .ConfigureAwait(false))
        {
            return Results.Redirect(BuildProtocolErrorRedirect(
                request,
                "unauthorized_client",
                "The OIDC client is disabled."));
        }
        var forceCenterLogin = ContainsPromptValue(request.Prompt, "login");
        if (forceCenterLogin)
        {
            await httpContext.SignOutAsync(IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)
                .ConfigureAwait(false);
        }

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

        if (!forceCenterLogin)
        {
            centerPrincipal ??= (await httpContext.AuthenticateAsync(
                        IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)
                    .ConfigureAwait(false))
                .Principal;
        }

        if (centerPrincipal is null)
        {
            if (ContainsPromptValue(request.Prompt, "none"))
            {
                return Results.Redirect(BuildProtocolErrorRedirect(
                    request,
                    "login_required",
                    "The authorization server requires end-user authentication."));
            }

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

    private static bool ContainsPromptValue(string? prompt, string value)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        foreach (var segment in prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(segment, value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildProtocolErrorRedirect(
        OpenIddictRequest request,
        string error,
        string errorDescription)
    {
        var redirectUri = request.RedirectUri;
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return "/connect/authorize";
        }

        var builder = new StringBuilder(redirectUri);
        builder.Append(redirectUri.Contains('?', StringComparison.Ordinal) ? '&' : '?');
        builder.Append("error=").Append(Uri.EscapeDataString(error));
        builder.Append("&error_description=").Append(Uri.EscapeDataString(errorDescription));
        if (!string.IsNullOrWhiteSpace(request.State))
        {
            builder.Append("&state=").Append(Uri.EscapeDataString(request.State));
        }

        return builder.ToString();
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
        if (!string.IsNullOrWhiteSpace(request.Prompt))
        {
            builder.Append("<input type=\"hidden\" name=\"prompt\" value=\"")
                .Append(WebUtility.HtmlEncode(request.Prompt))
                .Append("\"/>");
        }

        if (request.MaxAge is not null)
        {
            builder.Append("<input type=\"hidden\" name=\"max_age\" value=\"")
                .Append(WebUtility.HtmlEncode(request.MaxAge.Value.ToString()))
                .Append("\"/>");
        }

        builder.Append("<label>Username <input name=\"username\" autocomplete=\"username\" required/></label><br/>");
        builder.Append("<label>Password <input name=\"password\" type=\"password\" autocomplete=\"current-password\" required/></label><br/>");
        builder.Append("<button type=\"submit\">Sign in</button></form></body></html>");
        return builder.ToString();
    }
}
