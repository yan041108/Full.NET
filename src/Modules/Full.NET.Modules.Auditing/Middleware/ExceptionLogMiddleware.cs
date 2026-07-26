using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Auditing.Features.WriteExceptionLogs;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Auditing.Middleware;

/// <summary>
/// 捕获 Endpoint 未处理异常并尽力落库后重抛，交给统一 ExceptionHandler。
/// </summary>
internal sealed class ExceptionLogMiddleware(RequestDelegate next)
{
    private const string TenantItemKey = "FullNet.TenantId";
    private const int MaxTypeLength = 256;
    private const int MaxPathLength = 512;
    private const string SafeExceptionMessage = "Unhandled application exception.";

    public async Task InvokeAsync(HttpContext httpContext, ExceptionLogWriter writer)
    {
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await writer.TryWriteAsync(
                    BuildModel(httpContext, ex),
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    private static ExceptionLogWriteModel BuildModel(HttpContext httpContext, Exception exception)
    {
        Guid? userId = null;
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var subject = httpContext.User.FindFirst(FullNetIdentityClaimTypes.Subject)?.Value;
            if (Guid.TryParse(subject, out var parsedUserId))
            {
                userId = parsedUserId;
            }
        }

        Guid? tenantId = null;
        if (httpContext.Items.TryGetValue(TenantItemKey, out var tenantValue)
            && tenantValue is Guid parsedTenantId)
        {
            tenantId = parsedTenantId;
        }

        var exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        if (exceptionType.Length > MaxTypeLength)
        {
            exceptionType = exceptionType[..MaxTypeLength];
        }

        var method = httpContext.Request.Method;
        if (method.Length > 16)
        {
            method = method[..16];
        }

        var path = httpContext.Request.Path.Value;
        if (path is { Length: > MaxPathLength })
        {
            path = path[..MaxPathLength];
        }

        var traceId = Activity.Current?.TraceId.ToString()
            ?? httpContext.TraceIdentifier;
        if (traceId.Length > 64)
        {
            traceId = traceId[..64];
        }

        return new ExceptionLogWriteModel(
            exceptionType,
            SafeExceptionMessage,
            null,
            method,
            path,
            userId,
            tenantId,
            string.IsNullOrWhiteSpace(traceId) ? null : traceId,
            FingerprintClientIp(httpContext));
    }

    private static string? FingerprintClientIp(HttpContext httpContext)
    {
        var address = httpContext.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(address)));
    }
}
