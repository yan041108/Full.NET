// OIDC 授权端点映射；浏览器授权码流，不承载业务 API。
using System.Net;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Localization;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
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
            .WithTags("IdentityOidcProtocol")
            .RequireRateLimiting("identity-login");
    }

    internal static async Task<IResult> HandleAuthorizeAsync(
        HttpContext httpContext,
        IdentityOidcAuthorizationService authorizationService,
        IdentityOidcClientConfigResolver clientConfigResolver,
        IOptions<IdentityOidcOptions> oidcOptions,
        IAntiforgery antiforgery,
        IClock clock,
        CancellationToken cancellationToken,
        IdentityOidcAccountService? accounts = null,
        IdentityOidcConsentService? consent = null,
        ILocaleNormalizer? locales = null,
        ILocaleContext? locale = null)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict request cannot be resolved.");
        var tenantParameter = request.GetParameter("fullnet_tenant_id")?.ToString();
        Guid? requestedTenant = null;
        if (tenantParameter is not null)
        {
            if (!Guid.TryParse(tenantParameter, out var parsedTenant) || parsedTenant == Guid.Empty)
                return Results.BadRequest();
            requestedTenant = parsedTenant;
        }
        var pages = locales is not null && locale is not null ? IdentityOidcPages.Resolve(request.UiLocales, locales, locale)
            : new IdentityOidcPages(LocaleCatalog.DefaultLocale);
        IdentityOidcPages.Secure(httpContext);
        var config = string.IsNullOrWhiteSpace(request.ClientId) ? null
            : await clientConfigResolver.ResolveAsync(request.ClientId, cancellationToken).ConfigureAwait(false);
        pages.ConsentOnLogin = requestedTenant is null
            && (config?.IsFirstParty != true || ContainsPromptValue(request.Prompt, "consent"));
        var consentSubmission = HttpMethods.IsPost(httpContext.Request.Method) && httpContext.Request.HasFormContentType
            && httpContext.Request.Form.ContainsKey("consent");
        var isLoginSubmission = HttpMethods.IsPost(httpContext.Request.Method)
            && httpContext.Request.HasFormContentType
            && (httpContext.Request.Form.ContainsKey("username") || httpContext.Request.Form.ContainsKey("password"));
        // 租户授权必须在中心登录后单独确认，不能把凭据提交伪装成对目标的确认。
        if (requestedTenant is not null && isLoginSubmission && consentSubmission)
            return Results.BadRequest();
        if (isLoginSubmission || consentSubmission)
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
        if (consentSubmission && httpContext.Request.Form["consent"] == "deny")
            return Results.Redirect(BuildProtocolErrorRedirect(request, "access_denied", "The user declined consent."));
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
        if (isLoginSubmission && !string.IsNullOrWhiteSpace(httpContext.Request.Form["new_password"]))
        {
            var changed = await (accounts ?? throw new InvalidOperationException("Account service missing."))
                .ChangeByCredentialsAsync(httpContext.Request.Form["username"]!, httpContext.Request.Form["password"]!,
                    httpContext.Request.Form["second_factor"], httpContext.Request.Form["new_password"]!, cancellationToken)
                .ConfigureAwait(false);
            if (changed.IsSuccess)
                await httpContext.SignOutAsync(IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
            return Results.Content(pages.Login(request, antiforgery.GetAndStoreTokens(httpContext),
                pages.Text(changed.IsSuccess ? "passwordChanged" : "invalid"), !changed.IsSuccess), "text/html; charset=utf-8");
        }
        ClaimsPrincipal? centerPrincipal = null;
        if (isLoginSubmission
            && !string.IsNullOrWhiteSpace(httpContext.Request.Form["username"])
            && !string.IsNullOrWhiteSpace(httpContext.Request.Form["password"]))
        {
            var signInResult = await authorizationService.SignInCenterAsync(
                    httpContext.Request.Form["username"]!,
                    httpContext.Request.Form["password"]!,
                    cancellationToken, httpContext.Request.Form["second_factor"])
                .ConfigureAwait(false);
            if (!signInResult.Succeeded)
            {
                return Results.Content(
                    pages.Login(request, antiforgery.GetAndStoreTokens(httpContext),
                        pages.Text(signInResult.Failure?.Message == "password_change_required" ? "passwordRequired" : "invalid"),
                        signInResult.Failure?.Message == "password_change_required"),
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
                if (cookiePrincipal is not null && await authorizationService.IsCenterPrincipalActiveAsync(cookiePrincipal,
                    cancellationToken).ConfigureAwait(false)) centerPrincipal = cookiePrincipal;
                else if (cookiePrincipal is not null)
                    await httpContext.SignOutAsync(IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
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
                pages.Login(request, antiforgery.GetAndStoreTokens(httpContext)),
                "text/html; charset=utf-8",
                Encoding.UTF8);
        }

        var requestedAcr = (request.AcrValues ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestedAcr.Length > 0 && !requestedAcr.Any(value => value is "urn:fullnet:acr:mfa" or "urn:fullnet:acr:password"))
            return Results.Redirect(BuildProtocolErrorRedirect(request, "unmet_authentication_requirements", "Unsupported authentication context."));
        if ((config?.RequireMfa == true || requestedAcr.Contains("urn:fullnet:acr:mfa", StringComparer.Ordinal))
            && centerPrincipal.GetClaim("acr") != "urn:fullnet:acr:mfa")
        {
            if (ContainsPromptValue(request.Prompt, "none"))
                return Results.Redirect(BuildProtocolErrorRedirect(request, "login_required", "Multifactor authentication is required."));
            return Results.Content(pages.Login(request, antiforgery.GetAndStoreTokens(httpContext), pages.Text("mfaRequired")), "text/html; charset=utf-8");
        }
        if (requestedTenant is { } tenantId)
        {
            if (!Guid.TryParse(centerPrincipal.FindFirstValue(IdentityOidcCenterAuthenticationDefaults.CenterSessionIdClaim), out var centerId)
                || centerId == Guid.Empty
                || !Guid.TryParse(centerPrincipal.FindFirstValue(IdentityOidcCenterAuthenticationDefaults.UserIdClaim), out var userId)
                || userId == Guid.Empty)
                return Results.BadRequest();
            var binding = new IdentityOidcTenantConsentBinding(httpContext.RequestServices.GetRequiredService<IDataProtectionProvider>());
            var oneTime = new IdentityOidcTenantConsentTokenStore(
                httpContext.RequestServices.GetRequiredService<IOpenIddictTokenManager>(), clock);
            if (!consentSubmission)
            {
                if (ContainsPromptValue(request.Prompt, "none"))
                    return Results.Redirect(BuildProtocolErrorRedirect(request, "consent_required", "User consent is required."));
                var target = await authorizationService.ResolveTenantForConsentAsync(centerPrincipal, tenantId, cancellationToken)
                    .ConfigureAwait(false);
                if (target is null) return Results.BadRequest();
                var createdConsentId = await oneTime.CreateAsync(userId, cancellationToken).ConfigureAwait(false);
                return Results.Content(pages.Consent(request, antiforgery.GetAndStoreTokens(httpContext),
                    request.ClientId!, $"{target.Name} ({target.Identifier}; {target.Id:D})",
                    binding.Create(request, centerId, tenantId, createdConsentId), createdConsentId.ToString("D")), "text/html; charset=utf-8");
            }
            if (httpContext.Request.Form["consent"] != "accept"
                || !Guid.TryParse(httpContext.Request.Form["fullnet_tenant_consent_id"].ToString(), out var consentId)
                || consentId == Guid.Empty
                || !binding.Matches(httpContext.Request.Form["fullnet_tenant_consent"].ToString(), request, centerId, tenantId, consentId)
                || !await oneTime.TryConsumeAsync(consentId, userId, cancellationToken).ConfigureAwait(false))
                return Results.BadRequest();
        }

        string? grant = null;
        var needsConsent = requestedTenant is null
            && (config?.IsFirstParty != true || ContainsPromptValue(request.Prompt, "consent"));
        if (needsConsent)
        {
            grant = await (consent ?? throw new InvalidOperationException("Consent service missing."))
                .FindAsync(centerPrincipal, request, cancellationToken).ConfigureAwait(false);
            if (consentSubmission && httpContext.Request.Form["consent"] != "accept")
                return Results.Redirect(BuildProtocolErrorRedirect(request, "access_denied", "The user declined consent."));
            if (!consentSubmission && (grant is null || ContainsPromptValue(request.Prompt, "consent")))
            {
                if (ContainsPromptValue(request.Prompt, "none"))
                    return Results.Redirect(BuildProtocolErrorRedirect(request, "consent_required", "User consent is required."));
                return Results.Content(pages.Consent(request, antiforgery.GetAndStoreTokens(httpContext), request.ClientId!), "text/html; charset=utf-8");
            }
        }
        var principal = await authorizationService.CreateAuthorizationPrincipalAsync(
                centerPrincipal,
                request,
                cancellationToken,
                requestedTenant)
            .ConfigureAwait(false);
        if (principal is null)
        {
            return Results.Forbid(
                authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        if (needsConsent && consentSubmission)
            grant = await consent!.GrantAsync(principal, request, cancellationToken).ConfigureAwait(false);
        if (grant is not null) principal.SetAuthorizationId(grant);

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

}
