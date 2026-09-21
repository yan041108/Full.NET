using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Identity.Middleware;

/// <summary>
/// 在认证之后拦截仍处于强制改密状态的 JWT 会话，仅允许改密、会话维护与当前用户读取。
/// </summary>
internal sealed class PasswordChangeRequiredMiddleware(
    RequestDelegate next,
    IApiResultMapper resultMapper)
{
    private static readonly HashSet<string> AllowedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/me",
        "/api/v1/me/password",
        "/api/v1/auth/refresh",
        "/api/v1/auth/logout",
    };

    public async Task InvokeAsync(HttpContext httpContext)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api")
            || httpContext.User.Identity?.IsAuthenticated != true
            || !RequiresPasswordChange(httpContext.User))
        {
            await next(httpContext).ConfigureAwait(false);
            return;
        }

        var path = httpContext.Request.Path.Value ?? string.Empty;
        if (IsAllowedPath(httpContext, path)
            || IsSelfServiceTenantInvitationPath(httpContext, path))
        {
            await next(httpContext).ConfigureAwait(false);
            return;
        }

        var result = Result<object?>.Failure(new Error(
            IdentityErrorCodes.PasswordChangeRequired,
            "The current account must change its password before accessing this resource.",
            ErrorType.Forbidden));
        await resultMapper.Map(result, httpContext).ExecuteAsync(httpContext).ConfigureAwait(false);
    }

    private static bool RequiresPasswordChange(System.Security.Claims.ClaimsPrincipal principal) =>
        principal.HasClaim(
            claim => claim.Type == FullNetIdentityClaimTypes.PasswordChangeRequired
                     && string.Equals(claim.Value, bool.TrueString, StringComparison.OrdinalIgnoreCase));

    private static bool IsAllowedPath(HttpContext httpContext, string path)
    {
        if (!AllowedPaths.Contains(path))
        {
            return false;
        }

        return path switch
        {
            "/api/v1/me" => HttpMethods.IsGet(httpContext.Request.Method),
            "/api/v1/me/password" => HttpMethods.IsPost(httpContext.Request.Method),
            "/api/v1/auth/refresh" => HttpMethods.IsPost(httpContext.Request.Method),
            "/api/v1/auth/logout" => HttpMethods.IsPost(httpContext.Request.Method),
            _ => false,
        };
    }

    private static bool IsSelfServiceTenantInvitationPath(HttpContext httpContext, string path)
    {
        if (path.Equals("/api/v1/me/tenant-invitations", StringComparison.OrdinalIgnoreCase))
        {
            return HttpMethods.IsGet(httpContext.Request.Method);
        }

        if (!path.StartsWith("/api/v1/me/tenant-invitations/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.EndsWith("/accept", StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsPost(httpContext.Request.Method);
    }
}
