using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Auditing.Features.WriteAccessLogs;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Auditing.Middleware;

/// <summary>
/// 在授权之后记录 HTTP 访问汇总行；写库失败不影响响应。
/// </summary>
internal sealed class AccessLogMiddleware(RequestDelegate next)
{
    private const string TenantItemKey = "FullNet.TenantId";
    private const int MaxPathLength = 512;

    public async Task InvokeAsync(HttpContext httpContext, AccessLogWriter writer)
    {
        if (!ShouldCapture(httpContext.Request.Path))
        {
            await next(httpContext).ConfigureAwait(false);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            var model = BuildModel(httpContext, stopwatch.Elapsed);
            // 尽力写入：不传播取消，避免客户端断开导致审计写入被连带取消。
            await writer.TryWriteAsync(model, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static bool ShouldCapture(PathString path)
    {
        if (!path.StartsWithSegments("/api"))
        {
            return false;
        }

        if (path.StartsWithSegments("/health")
            || path.StartsWithSegments("/openapi")
            || path.StartsWithSegments("/scalar"))
        {
            return false;
        }

        return true;
    }

    private static AccessLogWriteModel BuildModel(HttpContext httpContext, TimeSpan elapsed)
    {
        var isAuthenticated = httpContext.User.Identity?.IsAuthenticated == true;
        Guid? userId = null;
        if (isAuthenticated)
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

        var method = httpContext.Request.Method;
        if (method.Length > 16)
        {
            method = method[..16];
        }

        var path = httpContext.Request.Path.Value ?? "/";
        if (path.Length > MaxPathLength)
        {
            path = path[..MaxPathLength];
        }

        var traceId = Activity.Current?.TraceId.ToString()
            ?? httpContext.TraceIdentifier;
        if (traceId.Length > 64)
        {
            traceId = traceId[..64];
        }

        var durationMs = elapsed.TotalMilliseconds > int.MaxValue
            ? int.MaxValue
            : (int)Math.Round(elapsed.TotalMilliseconds);

        return new AccessLogWriteModel(
            method,
            path,
            httpContext.Response.StatusCode,
            durationMs,
            userId,
            tenantId,
            string.IsNullOrWhiteSpace(traceId) ? null : traceId,
            FingerprintClientIp(httpContext),
            isAuthenticated);
    }

    /// <summary>
    /// 仅持久化客户端 IP 指纹，避免明文地址进入审计表。
    /// </summary>
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
