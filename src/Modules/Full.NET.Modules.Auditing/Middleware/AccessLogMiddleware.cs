using System.Diagnostics;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Auditing.Features.WriteAccessLogs;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Auditing.Middleware;

/// <summary>
/// 在认证前包裹 API 请求，以便请求完成后同时记录成功、拒绝和匿名访问。
/// 只保存路径模板和元数据，查询参数与请求体永不进入访问日志。
/// </summary>
internal sealed class AccessLogMiddleware(RequestDelegate next)
{
    private const string TenantItemKey = "FullNet.TenantId";

    public async Task InvokeAsync(
        HttpContext httpContext,
        AccessLogWriteQueue queue,
        IClock clock)
    {
        if (!queue.Enabled || !httpContext.Request.Path.StartsWithSegments("/api"))
        {
            await next(httpContext).ConfigureAwait(false);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var unhandled = false;
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        catch (Exception)
        {
            unhandled = true;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            try
            {
                queue.TryEnqueue(BuildModel(httpContext, clock.UtcNow, stopwatch.Elapsed, unhandled));
            }
            catch (Exception)
            {
                // B2 访问日志采集失败不能覆盖业务响应或原始异常。
                AccessLogTelemetry.RecordDropped("capture_failed");
            }
        }
    }

    private static AccessLogWriteModel BuildModel(
        HttpContext httpContext,
        DateTimeOffset occurredAtUtc,
        TimeSpan elapsed,
        bool unhandled)
    {
        var authenticated = httpContext.User.Identity?.IsAuthenticated == true;
        Guid? userId = null;
        if (authenticated && Guid.TryParse(
            httpContext.User.FindFirst(FullNetIdentityClaimTypes.Subject)?.Value,
            out var parsedUserId))
        {
            userId = parsedUserId;
        }

        Guid? tenantId = null;
        if (httpContext.Items.TryGetValue(TenantItemKey, out var tenantValue)
            && tenantValue is Guid parsedTenantId)
        {
            tenantId = parsedTenantId;
        }

        // 路由模板避免把路径中的用户标识或其他动态片段写入日志。
        var path = httpContext.GetEndpoint() is RouteEndpoint endpoint
            ? endpoint.RoutePattern.RawText
            : "/api/{unmatched}";
        path = HttpOperationLogSanitizer.SanitizeUrl(path, 512);
        var method = HttpOperationLogSanitizer.Truncate(httpContext.Request.Method, 16);
        var traceId = Activity.Current?.TraceId.ToString()
            ?? httpContext.TraceIdentifier;
        var fingerprint = HttpOperationLogSanitizer.FingerprintClientIp(
            httpContext.Connection.RemoteIpAddress?.ToString());
        var durationMs = elapsed.TotalMilliseconds > int.MaxValue
            ? int.MaxValue
            : (int)Math.Round(elapsed.TotalMilliseconds);

        return new AccessLogWriteModel(
            occurredAtUtc,
            method,
            path,
            unhandled ? StatusCodes.Status500InternalServerError : httpContext.Response.StatusCode,
            durationMs,
            userId,
            tenantId,
            HttpOperationLogSanitizer.Truncate(traceId, 64),
            string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint,
            authenticated);
    }
}
