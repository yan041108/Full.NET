using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.Login;

internal static class Endpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/login", async (
            LoginRequest request,
            ICommandDispatcher dispatcher,
            IApiResultMapper mapper,
            AllowedOriginValidator originValidator,
            IdentityCookieWriter cookieWriter,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var origin = httpContext.Request.Headers.Origin.ToString();
            var requestOrigin = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var referer = httpContext.Request.Headers.Referer.ToString();
            if (!originValidator.IsAllowed(origin, requestOrigin, referer))
            {
                return mapper.Map(
                    Result<TokenResponse>.Failure(new Error(
                        Code: IdentityErrorCodes.OriginNotAllowed,
                        Message: "The request origin is not allowed.",
                        Type: ErrorType.Forbidden)),
                    httpContext);
            }

            var result = await dispatcher.SendAsync<Command, LoginSessionResult>(
                    new Command(
                        request.Username,
                        request.Password,
                        new ClientRequestContext(
                            httpContext.Connection.RemoteIpAddress?.ToString(),
                            httpContext.Request.Headers.UserAgent.ToString())),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(
                    Result<TokenResponse>.Failure(result.Error!),
                    httpContext);
            }

            cookieWriter.Write(
                httpContext.Response,
                result.Value!.RefreshToken,
                result.Value.CsrfToken);
            return Results.Ok(result.Value.Token);
        })
        .AllowAnonymous()
        .RequireRateLimiting("identity-login");
    }
}
