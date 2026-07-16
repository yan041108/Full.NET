using Full.NET.Hosting.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Full.NET.Hosting.Api;

public sealed class FullNetExceptionHandler(
    IApiResultMapper mapper,
    ILogger<FullNetExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        HostingLog.UnhandledException(logger, exception, httpContext.Request.Path);
        await mapper.MapException(exception, httpContext).ExecuteAsync(httpContext);
        return true;
    }
}
