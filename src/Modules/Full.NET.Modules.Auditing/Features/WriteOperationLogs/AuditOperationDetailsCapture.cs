using System.Net;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Retention;
using Full.NET.Modules.Auditing.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.WriteOperationLogs;

/// <summary>仅在 B1 独立清理健康且静态路由获准时构造固定字段详情；不读取请求/响应正文。</summary>
internal sealed class AuditOperationDetailsCapture(
    AuditDetailsCapturePolicyCache health,
    IClock clock,
    IOptionsMonitor<AuditDetailsCaptureOptions> options,
    IOptionsMonitor<AuditingRetentionOptions> retentionOptions)
{
    private const string ExportRoute = "/api/v1/auditing/operation-logs/exports";
    private static readonly object ExportSummaryItemKey = new();

    /// <summary>仅为操作日志导出生成固定字段投影；许可失败时不调用工厂。</summary>
    public void TryCaptureExportSummary(
        HttpContext context,
        Func<AuditLogExportRequest> requestFactory,
        Func<AuditLogExportMetadataResponse> responseFactory)
    {
        try
        {
            if (!TryGetCaptureOptions(context, out var current, out var route)
                || !string.Equals(route, ExportRoute, StringComparison.OrdinalIgnoreCase)
                || context.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            var requestState = current.MaxRequestPayloadBytes > 0
                ? "failed" : "not_enabled";
            OperationLogExportRequestSummaryV1? requestSummary = null;
            if (current.MaxRequestPayloadBytes > 0)
            {
                try
                {
                    var request = requestFactory();
                    var candidate = new OperationLogExportRequestSummaryV1(
                        request.FromUtc.ToUniversalTime(), request.ToUtc.ToUniversalTime(),
                        request.StatusCode, request.Succeeded);
                    if (candidate.FromUtc > candidate.ToUtc)
                    {
                        requestState = "not_allowed";
                    }
                    else if (JsonSerializer.SerializeToUtf8Bytes(candidate,
                            AuditingJsonSerializerContext.Default.OperationLogExportRequestSummaryV1)
                        .Length > current.MaxRequestPayloadBytes)
                    {
                        requestState = "budget_exceeded";
                    }
                    else
                    {
                        requestSummary = candidate;
                        requestState = "captured";
                    }
                }
                catch (Exception)
                {
                    requestState = "failed";
                }
            }

            var responseState = current.MaxResponsePayloadBytes > 0
                ? "failed" : "not_enabled";
            OperationLogExportResponseSummaryV1? responseSummary = null;
            if (current.MaxResponsePayloadBytes > 0)
            {
                try
                {
                    var response = responseFactory();
                    var candidate = new OperationLogExportResponseSummaryV1(
                        response.RowCount, response.Truncated, response.IncludesSensitiveFields);
                    if (candidate.RowCount < 0)
                    {
                        responseState = "not_allowed";
                    }
                    else if (JsonSerializer.SerializeToUtf8Bytes(candidate,
                            AuditingJsonSerializerContext.Default.OperationLogExportResponseSummaryV1)
                        .Length > current.MaxResponsePayloadBytes)
                    {
                        responseState = "budget_exceeded";
                    }
                    else
                    {
                        responseSummary = candidate;
                        responseState = "captured";
                    }
                }
                catch (Exception)
                {
                    responseState = "failed";
                }
            }

            context.Items[ExportSummaryItemKey] = new ExportSummary(
                context, requestState, requestSummary, responseState, responseSummary);
        }
        catch (Exception)
        {
            // 详情投影旁路失败不得影响导出响应，也不得输出原始异常。
        }
    }

    public AuditOperationDetails? TryCapture(HttpContext context)
    {
        try
        {
            if (!TryGetCaptureOptions(context, out var current, out _))
            {
                return null;
            }

            var capturedAtUtc = clock.UtcNow.ToUniversalTime();
            var staged = context.Items.TryGetValue(ExportSummaryItemKey, out var value)
                && value is ExportSummary summary
                && ReferenceEquals(summary.Owner, context)
                    ? summary
                    : null;
            var projection = new OperationLogDetailsContextV1(
                1,
                ReadIp(context.Connection.RemoteIpAddress),
                ReadPort(context.Connection.RemotePort),
                ReadIp(context.Connection.LocalIpAddress),
                ReadPort(context.Connection.LocalPort),
                staged?.RequestState ?? "not_applicable",
                staged?.RequestSummary,
                staged?.ResponseState ?? "not_applicable",
                staged?.ResponseSummary);
            var contextJson = JsonSerializer.Serialize(
                projection,
                AuditingJsonSerializerContext.Default.OperationLogDetailsContextV1);
            if (Encoding.UTF8.GetByteCount(contextJson) > 8192)
            {
                return null;
            }

            return new AuditOperationDetails(
                contextJson,
                capturedAtUtc.AddHours(current.RetentionHours));
        }
        catch (Exception)
        {
            // 配置热更新、路由或序列化异常均只撤销详情，不影响 B1 摘要和业务响应。
            return null;
        }
    }

    private bool TryGetCaptureOptions(
        HttpContext context,
        out AuditDetailsCaptureOptions current,
        out string route)
    {
        current = options.CurrentValue;
        route = string.Empty;
        if (!health.CanCapture()
            || context.User.Identity?.IsAuthenticated != true
            || !HttpMethods.IsPost(context.Request.Method)
                && !HttpMethods.IsPut(context.Request.Method)
                && !HttpMethods.IsPatch(context.Request.Method)
                && !HttpMethods.IsDelete(context.Request.Method))
        {
            return false;
        }

        var retention = retentionOptions.CurrentValue;
        if (!current.Enabled
            || current.RetentionHours is < 1 or > 720
            || retention.OperationRetentionDays < 1
            || current.RetentionHours > retention.OperationRetentionDays * 24L
            || context.GetEndpoint() is not RouteEndpoint endpoint
            || endpoint.RoutePattern.RawText is not { Length: > 0 } rawRoute)
        {
            return false;
        }

        route = "/" + rawRoute.TrimStart('/');
        return route.Length <= 512
            && route.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            && current.CaptureRouteAllowList is not null
            && current.CaptureRouteAllowList.Contains(route, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ExportSummary(
        HttpContext Owner,
        string RequestState,
        OperationLogExportRequestSummaryV1? RequestSummary,
        string ResponseState,
        OperationLogExportResponseSummaryV1? ResponseSummary);

    private static string? ReadIp(IPAddress? address)
    {
        var value = address?.ToString();
        return value is { Length: <= 64 } ? value : null;
    }

    private static int? ReadPort(int port) => port is > 0 and <= 65535 ? port : null;
}
