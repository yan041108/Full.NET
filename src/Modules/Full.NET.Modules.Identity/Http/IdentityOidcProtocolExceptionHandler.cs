using System.Text.Json;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Identity.Http;

/// <summary>为协议端点返回静态序列化的标准错误，不向客户端泄露异常细节。</summary>
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
            JsonSerializer.Serialize(
                new IdentityOidcProtocolError("server_error", "An internal error occurred."),
                IdentityJsonSerializerContext.Default.IdentityOidcProtocolError),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
