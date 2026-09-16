using System.Net;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
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

    internal static async Task<IResult> HandleAuthorizeAsync(
        HttpContext httpContext,
        IdentityOidcAuthorizationService authorizationService,
        IdentityOidcClientConfigResolver clientConfigResolver,
        IOptions<IdentityOidcOptions> oidcOptions,
        IAntiforgery antiforgery,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict request cannot be resolved.");
        var isLoginSubmission = HttpMethods.IsPost(httpContext.Request.Method)
            && httpContext.Request.HasFormContentType
            && (httpContext.Request.Form.ContainsKey("username") || httpContext.Request.Form.ContainsKey("password"));
        if (isLoginSubmission)
        {
            // 必须在凭据处理与任何 Cookie 写入之前验证，防止跨站表单替换中心登录身份。
            try
            {
                await antiforgery.ValidateRequestAsync(httpContext).ConfigureAwait(false);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.StatusCode(StatusCodes.Status400BadRequest);
            }
        }
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
        ClaimsPrincipal? centerPrincipal = null;
        if (isLoginSubmission
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
                    BuildLoginPage(request, "Invalid username or password.", antiforgery.GetAndStoreTokens(httpContext)),
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
            var cookiePrincipal = (await httpContext.AuthenticateAsync(
                        IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)
                    .ConfigureAwait(false))
                .Principal;
            // 新提交的凭据已完成认证；max_age=0 只禁止复用旧 Cookie，不能让表单无限重登。
            if (centerPrincipal is null && !RequiresFreshAuthentication(cookiePrincipal, request.MaxAge, clock.UtcNow))
            {
                centerPrincipal = cookiePrincipal;
            }
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
                BuildLoginPage(request, null, antiforgery.GetAndStoreTokens(httpContext)),
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

    /// <summary>按原始认证时间判断 Cookie 是否满足 RP 的新鲜度要求；缺失时间时失败关闭。</summary>
    internal static bool RequiresFreshAuthentication(ClaimsPrincipal? principal, long? maxAge, DateTimeOffset now)
    {
        if (maxAge is null) return false;
        if (maxAge <= 0 || !long.TryParse(principal?.FindFirstValue(OpenIddictConstants.Claims.AuthenticationTime),
                NumberStyles.None, CultureInfo.InvariantCulture, out var authenticatedAt)) return true;
        var nowSeconds = now.ToUnixTimeSeconds();
        return authenticatedAt < 0 || authenticatedAt > nowSeconds || nowSeconds - authenticatedAt > maxAge.Value;
    }

    private static string BuildLoginPage(OpenIddictRequest request, string? errorMessage, AntiforgeryTokenSet tokens)
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
        builder.Append("<input type=\"hidden\" name=\"")
            .Append(WebUtility.HtmlEncode(tokens.FormFieldName))
            .Append("\" value=\"")
            .Append(WebUtility.HtmlEncode(tokens.RequestToken))
            .Append("\"/>");
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
