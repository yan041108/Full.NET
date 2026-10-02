using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 每个进入 Web 应用的请求在结束时最多生成一条 HttpOperationCompleted（B2）。
/// 不写业务主库、不写 Outbox；关闭本中间件不影响 Metrics/Error/B0/B1。
/// </summary>
public sealed class HttpOperationLogMiddleware(
    RequestDelegate next,
    IOptionsMonitor<HttpOperationLogOptions> optionsMonitor,
    HttpOperationLogEmitter emitter,
    ILogger<HttpOperationLogMiddleware> logger)
{
    /// <summary>Http 操作日志事件名；写入日志与诊断作用域的 EventName 字段，发布后不可改名。</summary>
    public const string EventName = "HttpOperationCompleted";
    /// <summary>Http 操作日志的诊断分组名；用于日志分类与过滤，发布后不可改名。</summary>
    public const string DiagnosticGroup = "http.operation";
    /// <summary>Http 操作日志的日志流标识；用于日志分流与采集，发布后不可改名。</summary>
    public const string LogStream = "http-operation";
    private const string TenantItemKey = "FullNet.TenantId";

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var options = optionsMonitor.CurrentValue;
        if (!options.Enabled
            || options.CaptureMode == HttpOperationCaptureMode.Disabled
            || !ShouldCapture(httpContext.Request.Path, options))
        {
            await next(httpContext).ConfigureAwait(false);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        Exception? unhandled = null;
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            unhandled = exception;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            try
            {
                Emit(httpContext, stopwatch.Elapsed, unhandled, options);
            }
            catch (Exception emitException)
            {
                // B2 fail-open：发射失败不得影响响应或掩盖原异常。
                logger.LogDebug(emitException, "HttpOperationCompleted emit failed.");
            }
        }
    }

    private void Emit(
        HttpContext httpContext,
        TimeSpan elapsed,
        Exception? unhandled,
        HttpOperationLogOptions options)
    {
        var statusCode = httpContext.Response.StatusCode;
        var isError = unhandled is not null || statusCode >= 500;
        var isSlow = elapsed >= options.SlowRequestThreshold;
        var isPriority = (isError && options.AlwaysRecordErrors) || isSlow;

        var routeKey = ResolveRouteKey(httpContext);
        var traceId = Activity.Current?.TraceId.ToString()
            ?? httpContext.TraceIdentifier;

        if (!isPriority)
        {
            if (!emitter.ShouldSampleSuccess(routeKey, traceId))
            {
                HttpOperationLogTelemetry.RecordSkipped("success_sample");
                return;
            }

            if (!emitter.TryEnterBestEffort())
            {
                return;
            }
        }
        else if (!emitter.TryEnterPriority())
        {
            return;
        }

        try
        {
            var method = httpContext.Request.Method;
            if (method.Length > 16)
            {
                method = method[..16];
            }

            var rawUrl = httpContext.Request.Path.Value
                + httpContext.Request.QueryString.Value;
            var url = HttpOperationLogSanitizer.SanitizeUrl(rawUrl);
            var sourceUrl = HttpOperationLogSanitizer.SanitizeSourceUrl(
                httpContext.Request.Headers.Origin.FirstOrDefault()
                ?? httpContext.Request.Headers.Referer.FirstOrDefault());
            var clientIpFingerprint = HttpOperationLogSanitizer.FingerprintClientIp(
                httpContext.Connection.RemoteIpAddress?.ToString());

            Guid? tenantId = null;
            if (httpContext.Items.TryGetValue(TenantItemKey, out var tenantValue)
                && tenantValue is Guid parsedTenantId)
            {
                tenantId = parsedTenantId;
            }

            string? payload = null;
            if (options.CaptureMode == HttpOperationCaptureMode.SanitizedPayload
                && options.PayloadRouteAllowList.Contains(routeKey, StringComparer.OrdinalIgnoreCase)
                && options.MaxRequestPayloadBytes > 0
                && httpContext.Items.TryGetValue(
                    HttpOperationLogPayloadCapture.ItemKey,
                    out var captured)
                && captured is string rawPayload)
            {
                payload = HttpOperationLogSanitizer.ProjectJsonPayload(
                    rawPayload,
                    HttpOperationLogPayloadCapture.DefaultAllowedFields,
                    options.MaxRequestPayloadBytes);
            }

            var reliability = isPriority ? "Priority" : "BestEffort";
            var level = isError
                ? LogLevel.Error
                : isSlow
                    ? LogLevel.Warning
                    : LogLevel.Information;

            using (logger.BeginScope(new Dictionary<string, object?>
            {
                ["EventName"] = EventName,
                ["log.class"] = LogClassification.HttpOperation,
                ["log.stream"] = LogStream,
                ["reliability.class"] = reliability,
                ["data.classification"] = "Internal",
                ["DiagnosticGroup"] = DiagnosticGroup,
                ["http.method"] = method,
                ["http.route"] = routeKey,
                ["url"] = url,
                ["http.status_code"] = statusCode,
                ["ElapsedMs"] = (int)Math.Min(int.MaxValue, Math.Round(elapsed.TotalMilliseconds)),
                ["SourceUrl"] = sourceUrl,
                ["TraceId"] = HttpOperationLogSanitizer.Truncate(traceId, 64),
                ["ClientIpFingerprint"] = clientIpFingerprint,
                ["TenantId"] = tenantId,
                ["RequestPayload"] = payload,
            }))
            {
                logger.Log(
                    level,
                    "{EventName} {HttpMethod} {Route} -> {StatusCode} in {ElapsedMs} ms",
                    EventName,
                    method,
                    routeKey,
                    statusCode,
                    (int)Math.Min(int.MaxValue, Math.Round(elapsed.TotalMilliseconds)));
            }

            HttpOperationLogTelemetry.RecordEmitted(reliability);
        }
        finally
        {
            if (isPriority)
            {
                emitter.ExitPriority();
            }
            else
            {
                emitter.ExitBestEffort();
            }
        }
    }

    private static bool ShouldCapture(PathString path, HttpOperationLogOptions options)
    {
        var value = path.Value ?? "/";
        foreach (var excluded in options.ExcludePathPrefixes)
        {
            if (value.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (options.IncludePathPrefixes.Length == 0)
        {
            return true;
        }

        foreach (var included in options.IncludePathPrefixes)
        {
            if (value.StartsWith(included, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveRouteKey(HttpContext httpContext)
    {
        var endpoint = httpContext.GetEndpoint();
        if (endpoint is RouteEndpoint routeEndpoint)
        {
            var template = routeEndpoint.RoutePattern.RawText;
            if (!string.IsNullOrWhiteSpace(template))
            {
                return HttpOperationLogSanitizer.Truncate(template, 256);
            }
        }

        var path = httpContext.Request.Path.Value ?? "/";
        return HttpOperationLogSanitizer.Truncate(path, 256);
    }
}

/// <summary>
/// SanitizedPayload 显式投影入口；默认不捕获请求体，由选定 Endpoint 写入 Items。
/// </summary>
/// <remarks>
/// 常量字符串发布后不可改名或删除；新增常量只能追加。
/// </remarks>
public static class HttpOperationLogPayloadCapture
{
    /// <summary>HttpContext.Items 中存储捕获请求体 JSON 的键；由 Endpoint 写入、中间件读取，发布后不可改名。</summary>
    public const string ItemKey = "FullNet.HttpOperation.RequestPayload";

    public static readonly string[] DefaultAllowedFields =
    [
        "id",
        "status",
        "code",
        "page",
        "pageSize",
    ];

    public static void CaptureRequestJson(HttpContext httpContext, string json)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        httpContext.Items[ItemKey] = json;
    }
}
