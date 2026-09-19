using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

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

/// <summary>中心登录成功后的业务导航结果，不作为协议重定向使用。</summary>
/// <param name="ReturnUrl">客户端请求的后续导航地址。</param>
internal sealed record IdentityOidcCenterLoginResponse(string ReturnUrl);

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
            .AllowAnonymous()
            .RequireRateLimiting("identity-login");
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
        return Results.Ok(new IdentityOidcCenterLoginResponse(request.ReturnUrl));
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

        var accessUserId = await TryReadBearerUserIdAsync(httpContext).ConfigureAwait(false);
        if (accessUserId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        await authorizationService.SignOutCenterAsync(
                httpContext,
                accessUserId,
                cancellationToken)
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

        var accessUserId = await TryReadBearerUserIdAsync(httpContext).ConfigureAwait(false);
        if (accessUserId == Guid.Empty
            || (accessUserId is not null && !string.Equals(
                httpContext.User.FindFirstValue(FullNetIdentityClaimTypes.OidcClientId),
                request.ClientId, StringComparison.Ordinal)))
        {
            return Results.Unauthorized();
        }

        var signedOut = await authorizationService.SignOutApplicationAsync(
                httpContext,
                request.ClientId,
                accessUserId,
                cancellationToken)
            .ConfigureAwait(false);
        return signedOut ? Results.NoContent() : Results.Unauthorized();
    }

    internal static async Task<Guid?> TryReadBearerUserIdAsync(HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.ContainsKey("Authorization"))
        {
            return null;
        }

        var result = await httpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
        if (!result.Succeeded
            || result.Principal?.Identity?.IsAuthenticated != true
            || result.Principal.FindFirstValue(FullNetIdentityClaimTypes.TokenUse) != "access"
            || !Guid.TryParse(result.Principal.FindFirstValue(
                FullNetIdentityClaimTypes.ApplicationSessionId), out var sessionId)
            || sessionId == Guid.Empty)
        {
            return Guid.Empty;
        }

        httpContext.User = result.Principal;
        var subject = result.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(subject, out var userId) ? userId : Guid.Empty;
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
