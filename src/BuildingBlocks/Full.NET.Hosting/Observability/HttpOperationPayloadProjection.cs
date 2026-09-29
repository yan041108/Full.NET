using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Observability;

/// <summary>静态登记的 B2 投影键；调用方不能自定义字段图。</summary>
public static class HttpLogCaptureProjectionKeys
{
    /// <summary>仅包含数值分页参数的投影。</summary>
    public const string Pagination = "http.pagination.v1";
    /// <summary>仅包含数值分页结果的投影。</summary>
    public const string PaginationResult = "http.pagination.result.v1";
}

/// <summary>不含原始 Query、Header 或自由文本的分页参数投影。</summary>
/// <param name="Page">经业务验证后的页码。</param>
/// <param name="PageSize">经业务验证后的每页数量。</param>
public sealed record HttpPaginationLogProjection(int Page, int PageSize);

/// <summary>不含列表项目或原始响应 Body 的分页结果摘要。</summary>
public sealed record HttpPaginationResultLogProjection(
    int Page,
    int PageSize,
    long TotalCount,
    int ItemCount);

/// <summary>HTTP 日志投影使用的静态 JSON 元数据。</summary>
[JsonSerializable(typeof(HttpPaginationLogProjection))]
[JsonSerializable(typeof(HttpPaginationResultLogProjection))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class HttpLogCaptureJsonContext : JsonSerializerContext
{
}

/// <summary>在请求线程上先判断许可和预算，再允许构造受控投影。</summary>
public sealed class HttpOperationPayloadProjection
{
    internal const string B2ResultItemKey = "FullNet.HttpOperation.B2Projection";
    internal const string B2ResponseResultItemKey = "FullNet.HttpOperation.B2ResponseProjection";

    private readonly IOptionsMonitor<HttpOperationLogOptions> _options;
    private readonly HttpLogCaptureBudget _budget;
    private readonly HttpOperationLogEmitter _emitter;
    private readonly ILogger<HttpOperationLogMiddleware> _logger;

    internal HttpOperationPayloadProjection(
        IOptionsMonitor<HttpOperationLogOptions> options,
        HttpLogCaptureBudget budget,
        HttpOperationLogEmitter emitter,
        ILogger<HttpOperationLogMiddleware> logger)
    {
        _options = options;
        _budget = budget;
        _emitter = emitter;
        _logger = logger;
    }

    /// <summary>先取得请求投影许可；拒绝时不返回租约，投影工厂不得提前执行。</summary>
    /// <param name="httpContext">当前请求。</param>
    /// <param name="target">唯一允许的投影目的地。</param>
    /// <param name="projectionKey">静态注册的投影键。</param>
    /// <param name="lease">成功时取得的一次性租约，调用方必须释放。</param>
    /// <returns>已取得预算与目的地许可时为 true。</returns>
    public bool TryBeginCapture(
        HttpContext httpContext,
        HttpLogCaptureTarget target,
        string projectionKey,
        out HttpLogCaptureLease? lease) =>
        TryBeginCapture(httpContext, target, projectionKey, out lease, out _);

    /// <summary>尝试取得投影租约，并返回安全机器状态。</summary>
    /// <param name="httpContext">当前请求。</param>
    /// <param name="target">唯一允许的投影目的地。</param>
    /// <param name="projectionKey">静态注册的投影键。</param>
    /// <param name="lease">成功时取得的一次性租约，调用方必须释放。</param>
    /// <param name="state">拒绝原因；成功时为 Captured，实际投影结果仍以租约返回值为准。</param>
    /// <returns>已取得预算与目的地许可时为 true。</returns>
    public bool TryBeginCapture(
        HttpContext httpContext,
        HttpLogCaptureTarget target,
        string projectionKey,
        out HttpLogCaptureLease? lease,
        out HttpLogCaptureState state)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        lease = null;
        state = HttpLogCaptureState.Failed;
        try
        {
            return TryBeginCaptureCore(
                httpContext,
                target,
                projectionKey,
                out lease,
                out state);
        }
        catch (Exception)
        {
            // B2 许可失败不影响业务响应，也不输出可能包含配置或请求秘密的异常消息。
            lease?.Dispose();
            lease = null;
            state = HttpLogCaptureState.Failed;
            return false;
        }
    }

    private bool TryBeginCaptureCore(
        HttpContext httpContext,
        HttpLogCaptureTarget target,
        string projectionKey,
        out HttpLogCaptureLease? lease,
        out HttpLogCaptureState state)
    {
        lease = null;
        var current = _options.CurrentValue;
        if (!current.Enabled
            || current.CaptureMode != HttpOperationCaptureMode.SanitizedPayload)
        {
            state = HttpLogCaptureState.NotEnabled;
            return false;
        }

        // B1 必须等待 Auditing 提供独立资格、保留和清理健康状态，不能借 B2 配置放行。
        if (target == HttpLogCaptureTarget.B1RestrictedDetail)
        {
            state = HttpLogCaptureState.NotApplicable;
            return false;
        }

        if (target != HttpLogCaptureTarget.B2InternalSummary
            || projectionKey is not (HttpLogCaptureProjectionKeys.Pagination
                or HttpLogCaptureProjectionKeys.PaginationResult))
        {
            state = HttpLogCaptureState.NotAllowed;
            return false;
        }

        var configuredMaxBytes = projectionKey == HttpLogCaptureProjectionKeys.Pagination
            ? current.MaxRequestPayloadBytes
            : current.MaxResponsePayloadBytes;
        if (configuredMaxBytes <= 0)
        {
            state = HttpLogCaptureState.NotEnabled;
            return false;
        }

        // 当前静态投影只来自成功查询；Info 关闭时不为可能的 B2 摘要预构造或占用预算。
        if (!_logger.IsEnabled(LogLevel.Information)
            || !HttpOperationLogMiddleware.ShouldCapture(httpContext.Request.Path, current))
        {
            state = HttpLogCaptureState.NotAllowed;
            return false;
        }

        var fullRoute = HttpOperationMetadataBuilder.ResolveFullRouteTemplate(httpContext);
        if (fullRoute == "<unmatched>"
            || !current.PayloadRouteAllowList.Contains(
                fullRoute,
                StringComparer.OrdinalIgnoreCase))
        {
            state = HttpLogCaptureState.NotAllowed;
            return false;
        }

        // 发射采样仍使用与 Middleware 一致的有界路由标签；白名单只使用完整静态模板。
        var route = HttpOperationMetadataBuilder.ResolveRouteTemplate(httpContext);

        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            state = HttpLogCaptureState.Failed;
            return false;
        }

        if (!HttpOperationLogSampleKey.ShouldSampleSuccess(
                httpContext,
                route,
                _emitter))
        {
            state = HttpLogCaptureState.NotAllowed;
            return false;
        }

        var maxBytes = Math.Min(configuredMaxBytes, 2_048);
        if (!_budget.TryReserve(
                maxBytes,
                current.CaptureMaxEventsPerSecond,
                current.CaptureMaxBytesPerSecond,
                out var reservation))
        {
            state = HttpLogCaptureState.BudgetExceeded;
            return false;
        }

        try
        {
            lease = new HttpLogCaptureLease(
                httpContext,
                target,
                projectionKey,
                reservation!,
                maxBytes);
        }
        catch
        {
            reservation!.Dispose();
            throw;
        }
        state = HttpLogCaptureState.Captured;
        return true;
    }

    internal static bool TryReadB2Result(HttpContext httpContext, out string? payloadJson)
        => TryReadResult(httpContext, B2ResultItemKey, out payloadJson);

    internal static bool TryReadB2ResponseResult(HttpContext httpContext, out string? payloadJson)
        => TryReadResult(httpContext, B2ResponseResultItemKey, out payloadJson);

    private static bool TryReadResult(
        HttpContext httpContext,
        string itemKey,
        out string? payloadJson)
    {
        if (httpContext.Items.TryGetValue(itemKey, out var value)
            && value is HttpLogCaptureResult
            {
                State: HttpLogCaptureState.Captured,
                Target: HttpLogCaptureTarget.B2InternalSummary,
            } result
            && ReferenceEquals(result.Owner, httpContext))
        {
            payloadJson = result.PayloadJson;
            return payloadJson is not null;
        }

        payloadJson = null;
        return false;
    }
}
