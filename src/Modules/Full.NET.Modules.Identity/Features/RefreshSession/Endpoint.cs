using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.RefreshSession;

internal static class Endpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/refresh", async (
            ICommandDispatcher dispatcher,
            IApiResultMapper mapper,
            IdentityCookieWriter cookieWriter,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var refreshToken = httpContext.Request.Cookies[IdentityCookieWriter.RefreshCookieName];
            var csrfCookie = httpContext.Request.Cookies[IdentityCookieWriter.CsrfCookieName];
            var csrfHeader = httpContext.Request.Headers["X-CSRF-Token"].ToString();
            if (!CsrfTokenValidator.IsValid(csrfCookie, csrfHeader))
            {
                return mapper.Map(
                    Result<TokenResponse>.Failure(new Error(
                        "identity.csrf_validation_failed",
                        "CSRF validation failed.",
                        ErrorType.Forbidden)),
                    httpContext);
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return mapper.Map(
                    Result<TokenResponse>.Failure(new Error(
                        "identity.invalid_refresh_token",
                        "The refresh token is invalid or expired.",
                        ErrorType.Unauthorized)),
                    httpContext);
            }

            var result = await dispatcher.SendAsync<Command, RefreshSessionResult>(
                    new Command(
                        refreshToken,
                        new ClientRequestContext(
                            httpContext.Connection.RemoteIpAddress?.ToString(),
                            httpContext.Request.Headers.UserAgent.ToString())),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                cookieWriter.Delete(httpContext.Response);
                return mapper.Map(
                    Result<TokenResponse>.Failure(result.Error!),
                    httpContext);
            }

            cookieWriter.Write(
                httpContext.Response,
                result.Value!.RefreshToken,
                result.Value.CsrfToken);
            return Results.Ok(result.Value.Token);
        }).AllowAnonymous();
    }
}
