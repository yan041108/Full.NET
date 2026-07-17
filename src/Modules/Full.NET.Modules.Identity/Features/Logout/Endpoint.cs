using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.Logout;

internal static class Endpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/logout", async (
            ICommandDispatcher dispatcher,
            IApiResultMapper mapper,
            IdentityCookieWriter cookieWriter,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var csrfCookie = httpContext.Request.Cookies[IdentityCookieWriter.CsrfCookieName];
            var csrfHeader = httpContext.Request.Headers["X-CSRF-Token"].ToString();
            if (!CsrfTokenValidator.IsValid(csrfCookie, csrfHeader))
            {
                return mapper.Map(
                    Result<LogoutResult>.Failure(new Error(
                        "identity.csrf_validation_failed",
                        "CSRF validation failed.",
                        ErrorType.Forbidden)),
                    httpContext);
            }

            var refreshToken = httpContext.Request.Cookies[IdentityCookieWriter.RefreshCookieName]
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                cookieWriter.Delete(httpContext.Response);
                return Results.NoContent();
            }

            var result = await dispatcher.SendAsync<Command, LogoutResult>(
                    new Command(
                        refreshToken,
                        new ClientRequestContext(
                            httpContext.Connection.RemoteIpAddress?.ToString(),
                            httpContext.Request.Headers.UserAgent.ToString())),
                    cancellationToken)
                .ConfigureAwait(false);
            cookieWriter.Delete(httpContext.Response);
            return result.IsSuccess
                ? Results.NoContent()
                : mapper.Map(result, httpContext);
        }).AllowAnonymous();
    }
}
