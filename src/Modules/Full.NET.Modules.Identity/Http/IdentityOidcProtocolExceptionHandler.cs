using System.Text.Json;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Identity.Http;

/// <summary>Returns OAuth-style JSON errors for OIDC protocol paths.</summary>
internal sealed class IdentityOidcProtocolExceptionHandler(
    ILogger<IdentityOidcProtocolExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (!IdentityOidcProtocolPaths.IsProtocolPath(httpContext.Request.Path))
        {
            return false;
        }

        logger.LogError(
            exception,
            "OIDC protocol endpoint failed for {Path}.",
            httpContext.Request.Path);
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json;charset=UTF-8";
        await httpContext.Response.WriteAsync(
            JsonSerializer.Serialize(new
            {
                error = "server_error",
                error_description = "An internal error occurred.",
            }),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
