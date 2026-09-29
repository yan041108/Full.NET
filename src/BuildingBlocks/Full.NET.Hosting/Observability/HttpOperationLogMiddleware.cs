using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
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
    ILogger<HttpOperationLogMiddleware> logger,
    HttpOperationLogIngress ingress)
{
    public const string EventName = "HttpOperationCompleted";
    public const string DiagnosticGroup = "http.operation";
    public const string LogStream = "http-operation";
    private const string TenantItemKey = "FullNet.TenantId";

    public async Task InvokeAsync(HttpContext httpContext)
    {
        HttpOperationLogOptions options;
        bool shouldCapture;
        try
        {
            options = optionsMonitor.CurrentValue;
            shouldCapture = options.Enabled
                && options.CaptureMode != HttpOperationCaptureMode.Disabled
                && ShouldCapture(httpContext.Request.Path, options);
        }
        catch (Exception)
        {
            // 日志热更新配置失效时直接跳过 B2，不阻断请求。
            await next(httpContext).ConfigureAwait(false);
            return;
        }

        if (!shouldCapture)
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
                try
                {
                    logger.LogDebug(
                        "HttpOperationCompleted emit failed: {ExceptionType}",
                        emitException.GetType().Name);
                }
                catch (Exception)
                {
                    // 诊断 provider 再次失败也不得破坏业务响应。
                }
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
        var hasException = unhandled is not null
            || httpContext.Features.Get<IExceptionHandlerFeature>()?.Error is not null;
        var isError = hasException || statusCode >= 500;
        var outcome = hasException
            ? "Exception"
            : statusCode >= 400
                ? "HttpError"
                : "HttpCompleted";
        var isSlow = elapsed >= options.SlowRequestThreshold;
        var level = isError
            ? LogLevel.Error
            : isSlow
                ? LogLevel.Warning
                : LogLevel.Information;
        if (!logger.IsEnabled(level))
        {
            return;
        }

        var isPriority = (isError && options.AlwaysRecordErrors) || isSlow;

        var routeKey = HttpOperationMetadataBuilder.ResolveRouteTemplate(httpContext);
        if (!isPriority)
        {
            if (!HttpOperationLogSampleKey.ShouldSampleSuccess(
                    httpContext,
                    routeKey,
                    emitter))
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
            var metadata = HttpOperationMetadataBuilder.Capture(
                httpContext,
                options.CaptureThreadId,
                routeKey);
            var method = metadata.HttpMethod;
            // B2 摘要只输出路由模板；实际路径段和 Query 可能包含私密值。
            var url = metadata.RouteTemplate;

            Guid? tenantId = null;
            if (httpContext.Items.TryGetValue(TenantItemKey, out var tenantValue)
                && tenantValue is Guid parsedTenantId)
            {
                tenantId = parsedTenantId;
            }

            string? payload = null;
            string? responsePayload = null;
            if (options.CaptureMode == HttpOperationCaptureMode.SanitizedPayload
                && options.PayloadRouteAllowList.Contains(
                    HttpOperationMetadataBuilder.ResolveFullRouteTemplate(httpContext),
                    StringComparer.OrdinalIgnoreCase))
            {
                if (options.MaxRequestPayloadBytes > 0
                    && HttpOperationPayloadProjection.TryReadB2Result(
                        httpContext,
                        out var approvedPayload))
                {
                    payload = approvedPayload;
                }

                if (options.MaxResponsePayloadBytes > 0
                    && HttpOperationPayloadProjection.TryReadB2ResponseResult(
                        httpContext,
                        out var approvedResponsePayload))
                {
                    responsePayload = approvedResponsePayload;
                }
            }

            var reliability = isPriority ? "Priority" : "BestEffort";
            var fields = new Dictionary<string, object?>
            {
                ["EventName"] = EventName,
                ["log.class"] = LogClassification.HttpOperation,
                ["log.stream"] = LogStream,
                ["reliability.class"] = reliability,
                ["data.classification"] = "Internal",
                ["DiagnosticGroup"] = DiagnosticGroup,
                ["http.method"] = method,
                ["http.route"] = metadata.RouteTemplate,
                ["url"] = url,
                ["http.status_code"] = statusCode,
                ["Outcome"] = outcome,
                ["ElapsedMs"] = (int)Math.Min(int.MaxValue, Math.Round(elapsed.TotalMilliseconds)),
                ["SourceOriginFingerprint"] = metadata.SourceOriginFingerprint,
                ["TraceId"] = metadata.TraceId,
                ["SpanId"] = metadata.SpanId,
                ["RequestId"] = metadata.RequestId,
                ["ClientIpFingerprint"] = metadata.ClientIpFingerprint,
                ["EndpointName"] = metadata.EndpointName,
                ["EndpointDisplayName"] = metadata.EndpointDisplayName,
                ["Controller"] = metadata.Controller,
                ["Action"] = metadata.Action,
                ["Area"] = metadata.Area,
                ["Scheme"] = metadata.Scheme,
                ["HttpProtocol"] = metadata.HttpProtocol,
                ["UserAgentSummary"] = metadata.UserAgentSummary,
                ["Culture"] = metadata.Culture,
                ["AcceptLanguageSummary"] = metadata.AcceptLanguageSummary,
                ["ClientIdFingerprint"] = metadata.ClientIdFingerprint,
                ["CaptureThreadId"] = metadata.CaptureThreadId,
                ["TenantId"] = tenantId,
                ["RequestPayload"] = payload,
                ["ResponsePayload"] = responsePayload,
            };
            var admission = ingress.Emit(new HttpOperationLogRecord(fields), level switch
            {
                LogLevel.Error => Serilog.Events.LogEventLevel.Error,
                LogLevel.Warning => Serilog.Events.LogEventLevel.Warning,
                _ => Serilog.Events.LogEventLevel.Information,
            });

            if (admission is null)
            {
                HttpOperationLogTelemetry.RecordSkipped("missing_ingress");
            }
            else if (admission.Value)
            {
                HttpOperationLogTelemetry.RecordEmitted(reliability);
            }
            else
            {
                HttpOperationLogTelemetry.RecordDropped(
                    isPriority ? "priority_queue" : "best_effort_queue");
            }
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

    internal static bool ShouldCapture(PathString path, HttpOperationLogOptions options)
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
}

/// <summary>
/// 旧字符串入口已停用；保留方法签名供调用方安全迁移到两阶段静态投影。
/// </summary>
public static class HttpOperationLogPayloadCapture
{
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
        // 无法在已构造的原始 JSON 前完成目的地许可和预算，因此一律不留存。
        httpContext.Items.Remove(ItemKey);
    }
}
