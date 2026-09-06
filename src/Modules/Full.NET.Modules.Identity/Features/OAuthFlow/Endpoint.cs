using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageOAuthProviders;
using Full.NET.Modules.Identity.Features.OAuthFlow;
using Full.NET.Modules.Identity.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.OAuthFlow;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/identity/oauth/providers", async (
            OAuthProviderQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListEnabledPublicAsync(cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithTags("IdentityOAuthPublic")
        .WithName("identityListPublicOAuthProviders")
        .Produces<IReadOnlyList<PublicOAuthProviderResponse>>(StatusCodes.Status200OK)
        .AllowAnonymous();

        endpoints.MapGet("/api/v1/identity/oauth/{providerKey}/authorize", async (
            string providerKey,
            string mode,
            string? returnUrl,
            ClaimsPrincipal principal,
            OAuthFlowService flow,
            IdentityCookieWriter cookieWriter,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var requestOrigin = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var refreshToken = httpContext.Request.Cookies[cookieWriter.RefreshCookieName];
            var result = await flow.BeginAuthorizeAsync(
                    providerKey,
                    mode,
                    returnUrl,
                    requestOrigin,
                    principal,
                    refreshToken,
                    httpContext.Request.Scheme,
                    httpContext.Request.Host.Value ?? string.Empty,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return Results.Redirect(
                    BuildErrorReturnUrl(returnUrl, requestOrigin, result.Error!.Code));
            }

            return Results.Redirect(result.Value!);
        })
        .WithTags("IdentityOAuthPublic")
        .WithName("identityBeginOAuthAuthorization")
        .Produces(StatusCodes.Status302Found)
        .AllowAnonymous();

        endpoints.MapGet("/api/v1/identity/oauth/callback", async (
            string? code,
            string? state,
            OAuthFlowService flow,
            IdentityCookieWriter cookieWriter,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            {
                return Results.Redirect("/?oauth_error=oauth_invalid_state");
            }

            var result = await flow.HandleCallbackAsync(
                    code,
                    state,
                    httpContext.Request.Scheme,
                    httpContext.Request.Host.Value ?? string.Empty,
                    new ClientRequestContext(
                        httpContext.Connection.RemoteIpAddress?.ToString(),
                        httpContext.Request.Headers.UserAgent.ToString()),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return Results.Redirect("/?oauth_error=oauth_invalid_state");
            }

            var callback = result.Value!;
            if (callback.LoginSession is not null)
            {
                cookieWriter.Write(
                    httpContext.Response,
                    callback.LoginSession.RefreshToken,
                    callback.LoginSession.CsrfToken);
            }

            return Results.Redirect(callback.RedirectUrl);
        })
        .WithTags("IdentityOAuthPublic")
        .WithName("identityOAuthCallback")
        .Produces(StatusCodes.Status302Found)
        .AllowAnonymous();
    }

    private static string BuildErrorReturnUrl(
        string? returnUrl,
        string requestOrigin,
        string errorCode)
    {
        var fallback = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
        if (!fallback.StartsWith('/'))
        {
            fallback = "/";
        }

        var oauthError = errorCode switch
        {
            IdentityErrorCodes.OAuthInvalidReturnUrl => OAuthCallbackErrorCodes.InvalidState,
            IdentityErrorCodes.OAuthProviderDisabled => OAuthCallbackErrorCodes.ProviderUnavailable,
            IdentityErrorCodes.OAuthInvalidMode => OAuthCallbackErrorCodes.InvalidState,
            _ => OAuthCallbackErrorCodes.InvalidState,
        };
        return OAuth.OAuthReturnUrlValidator.AppendQuery(fallback, "oauth_error", oauthError);
    }
}
