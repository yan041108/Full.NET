using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.OidcSession;

internal sealed class IdentityOidcCenterLoginRequest
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ReturnUrl { get; set; } = string.Empty;
}

internal sealed class IdentityOidcApplicationLogoutRequest
{
    public string ClientId { get; set; } = string.Empty;
}

internal sealed record IdentityOidcSessionOperationResult;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints, IdentityOidcOptions options)
    {
        if (!options.Enable)
        {
            return;
        }

        endpoints.MapPost("/api/v1/identity/oidc/login", HandleLoginAsync)
            .WithName("identityOidcCenterLogin")
            .WithTags("IdentityOidcSession")
            .AllowAnonymous();
        endpoints.MapPost("/api/v1/identity/oidc/logout", HandleLogoutAsync)
            .WithName("identityOidcCenterLogout")
            .WithTags("IdentityOidcSession")
            .AllowAnonymous();
        endpoints.MapPost("/api/v1/identity/oidc/logout/application", HandleApplicationLogoutAsync)
            .WithName("identityOidcApplicationLogout")
            .WithTags("IdentityOidcSession")
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleLoginAsync(
        IdentityOidcCenterLoginRequest request,
        IdentityOidcAuthorizationService authorizationService,
        AllowedOriginValidator originValidator,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IsOriginAllowed(httpContext, originValidator))
        {
            return OriginForbidden(mapper, httpContext);
        }

        var signInResult = await authorizationService.SignInCenterAsync(
                request.Username,
                request.Password,
                cancellationToken)
            .ConfigureAwait(false);
        if (!signInResult.Succeeded)
        {
            return Results.Unauthorized();
        }

        await httpContext.SignInAsync(
                IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme,
                signInResult.Principal!,
                signInResult.Properties)
            .ConfigureAwait(false);
        return Results.Ok(new { returnUrl = request.ReturnUrl });
    }

    private static async Task<IResult> HandleLogoutAsync(
        HttpContext httpContext,
        IdentityOidcAuthorizationService authorizationService,
        AllowedOriginValidator originValidator,
        IApiResultMapper mapper,
        CancellationToken cancellationToken)
    {
        if (!IsOriginAllowed(httpContext, originValidator))
        {
            return OriginForbidden(mapper, httpContext);
        }

        await authorizationService.SignOutCenterAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleApplicationLogoutAsync(
        IdentityOidcApplicationLogoutRequest request,
        HttpContext httpContext,
        IdentityOidcAuthorizationService authorizationService,
        AllowedOriginValidator originValidator,
        IApiResultMapper mapper,
        CancellationToken cancellationToken)
    {
        if (!IsOriginAllowed(httpContext, originValidator))
        {
            return OriginForbidden(mapper, httpContext);
        }

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return Results.BadRequest();
        }

        var signedOut = await authorizationService.SignOutApplicationAsync(
                httpContext,
                request.ClientId,
                cancellationToken)
            .ConfigureAwait(false);
        return signedOut ? Results.NoContent() : Results.Unauthorized();
    }

    private static bool IsOriginAllowed(
        HttpContext httpContext,
        AllowedOriginValidator originValidator)
    {
        var origin = httpContext.Request.Headers.Origin.ToString();
        var requestOrigin = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var referer = httpContext.Request.Headers.Referer.ToString();
        return originValidator.IsAllowed(origin, requestOrigin, referer);
    }

    private static IResult OriginForbidden(IApiResultMapper mapper, HttpContext httpContext) =>
        mapper.Map(
            Result<IdentityOidcSessionOperationResult>.Failure(new Error(
                Code: IdentityErrorCodes.OriginNotAllowed,
                Message: "The request origin is not allowed.",
                Type: ErrorType.Forbidden)),
            httpContext);
}
