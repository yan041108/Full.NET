using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Observability;

/// <summary>请求内固定一次采样键，防止投影许可与最终发射使用不同标识。</summary>
internal static class HttpOperationLogSampleKey
{
    private static readonly object ItemKey = new();
    private static readonly object DecisionItemKey = new();
    private const string TenantItemKey = "FullNet.TenantId";

    public static bool ShouldSampleSuccess(
        HttpContext httpContext,
        string routeKey,
        HttpOperationLogEmitter emitter)
    {
        if (httpContext.Items.TryGetValue(DecisionItemKey, out var existing)
            && existing is SampleDecision decision
            && string.Equals(decision.RouteKey, routeKey, StringComparison.Ordinal)
            && (decision.NextExpiryUtc is null
                || decision.NextExpiryUtc > emitter.UtcNow))
        {
            return decision.Included;
        }

        var tenantId = httpContext.Items.TryGetValue(TenantItemKey, out var value)
            && value is Guid id
                ? id
                : (Guid?)null;
        var evaluated = emitter.EvaluateSuccessSample(
            routeKey,
            Resolve(httpContext),
            ResolveLocallyStartedTraceId(),
            tenantId);
        httpContext.Items[DecisionItemKey] = new SampleDecision(
            routeKey,
            evaluated.Included,
            evaluated.NextExpiryUtc);
        return evaluated.Included;
    }

    public static string Resolve(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue(ItemKey, out var existing)
            && existing is string key)
        {
            return key;
        }

        var value = Activity.Current?.TraceId.ToString()
            ?? HttpOperationLogSanitizer.Truncate(
                HttpOperationLogSanitizer.StripControlChars(httpContext.TraceIdentifier),
                64);
        httpContext.Items[ItemKey] = value;
        return value;
    }

    private static string? ResolveLocallyStartedTraceId()
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return null;
        }

        // 入站 traceparent 可由客户端选择；兼容无 listener 时仅设置 ParentId 的回退路径。
        for (var current = activity; current is not null; current = current.Parent)
        {
            if (current.HasRemoteParent
                || (current.Parent is null
                    && !string.IsNullOrEmpty(current.ParentId)))
            {
                return null;
            }
        }

        return activity.TraceId.ToString();
    }

    private sealed record SampleDecision(
        string RouteKey,
        bool Included,
        DateTimeOffset? NextExpiryUtc);
}

internal readonly record struct HttpOperationSampleDecision(
    bool Included,
    DateTimeOffset? NextExpiryUtc);

/// <summary>
/// B2 HTTP Operation 有界发射闸门：成功采样与 Priority/BestEffort 容量背压。
/// </summary>
public sealed class HttpOperationLogEmitter
{
    private readonly IOptionsMonitor<HttpOperationLogOptions> _options;
    private readonly IDiagnosticPolicyStore _diagnosticPolicyStore;
    private readonly TimeProvider _timeProvider;
    private int _bestEffortInFlight;
    private int _priorityInFlight;

    /// <summary>构造 HTTP Operation Log 发射闸门，注入配置监视器与诊断策略存储。</summary>
    public HttpOperationLogEmitter(
        IOptionsMonitor<HttpOperationLogOptions> options,
        IDiagnosticPolicyStore diagnosticPolicyStore,
        TimeProvider? timeProvider = null)
    {
        _options = options;
        _diagnosticPolicyStore = diagnosticPolicyStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    /// <summary>解析成功请求采样率：优先取配置 SuccessSampleRate，否则按 CapacityProfile 推导默认值。</summary>
    public double ResolveSuccessSampleRate()
    {
        var options = _options.CurrentValue;
        return options.SuccessSampleRate
            ?? HttpOperationLogProfile.ResolveSuccessSampleRate(options.CapacityProfile);
    }

    /// <summary>
    /// 异步解析成功采样率：优先应用诊断策略存储中的按组/端点/Trace/租户覆盖，
    /// 否则回退到配置默认采样率。
    /// </summary>
    public async ValueTask<double> ResolveSuccessSampleRateAsync(
        string? diagnosticGroup,
        string? endpoint,
        string? traceId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _diagnosticPolicyStore.GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);
        return snapshot.ResolveSuccessSampleRateOverride(
                   diagnosticGroup,
                   endpoint,
                   // 调用方给出的 TraceId 没有远端来源证明，不用于放宽采样。
                   traceId: null,
                   tenantId)
               ?? ResolveSuccessSampleRate();
    }

    /// <summary>
    /// 基于 RouteKey+TraceId 的确定性成功采样，保证日志与 Trace 可关联。
    /// </summary>
    public bool ShouldSampleSuccess(string routeKey, string? traceId)
    {
        return EvaluateSuccessSample(routeKey, traceId, null, null).Included;
    }

    internal HttpOperationSampleDecision EvaluateSuccessSample(
        string routeKey,
        string? sampleKey,
        string? policyTraceId,
        Guid? tenantId)
    {
        var policy = _diagnosticPolicyStore.Current.ResolveSuccessSamplePolicy(
            LogClassification.HttpOperation,
            routeKey,
            policyTraceId,
            tenantId,
            UtcNow);
        var rate = policy.Rate ?? ResolveSuccessSampleRate();
        if (rate >= 1.0)
        {
            return new HttpOperationSampleDecision(true, policy.NextExpiryUtc);
        }

        if (rate <= 0)
        {
            return new HttpOperationSampleDecision(false, policy.NextExpiryUtc);
        }

        var material = routeKey + "\n" + (sampleKey ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var bucket = BitConverter.ToUInt32(hash, 0) / (double)uint.MaxValue;
        return new HttpOperationSampleDecision(
            bucket < rate,
            policy.NextExpiryUtc);
    }

    /// <summary>尝试进入 BestEffort 发射通道；CAS 递增在途计数，超过容量时记录 dropped 指标并返回 false。</summary>
    public bool TryEnterBestEffort()
    {
        var capacity = _diagnosticPolicyStore.Current.ResolveBestEffortCapacity(
            _options.CurrentValue.BestEffortCapacity,
            UtcNow);
        while (true)
        {
            var current = Volatile.Read(ref _bestEffortInFlight);
            if (current >= capacity)
            {
                HttpOperationLogTelemetry.RecordDropped("best_effort");
                return false;
            }

            if (Interlocked.CompareExchange(ref _bestEffortInFlight, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    /// <summary>退出 BestEffort 发射通道，原子递减在途计数。</summary>
    public void ExitBestEffort() => Interlocked.Decrement(ref _bestEffortInFlight);

    /// <summary>尝试进入 Priority 发射通道；CAS 递增在途计数，超过容量时记录 dropped 指标并返回 false。</summary>
    public bool TryEnterPriority()
    {
        var capacity = _options.CurrentValue.PriorityCapacity;
        while (true)
        {
            var current = Volatile.Read(ref _priorityInFlight);
            if (current >= capacity)
            {
                HttpOperationLogTelemetry.RecordDropped("priority");
                return false;
            }

            if (Interlocked.CompareExchange(ref _priorityInFlight, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    /// <summary>退出 Priority 发射通道，原子递减在途计数。</summary>
    public void ExitPriority() => Interlocked.Decrement(ref _priorityInFlight);
}

internal static class HttpOperationLogTelemetry
{
    public const string MeterName = "Full.NET.Hosting.HttpOperationLog";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Emitted =
        Meter.CreateCounter<long>("fullnet.http_operation_log.emitted");
    private static readonly Counter<long> Dropped =
        Meter.CreateCounter<long>("fullnet.http_operation_log.dropped");
    private static readonly Counter<long> Skipped =
        Meter.CreateCounter<long>("fullnet.http_operation_log.skipped");

    public static void RecordEmitted(string reliability) =>
        Try(() => Emitted.Add(1, new KeyValuePair<string, object?>("reliability", reliability)));

    public static void RecordDropped(string channel) =>
        Try(() => Dropped.Add(1, new KeyValuePair<string, object?>("channel", channel)));

    public static void RecordSkipped(string reason) =>
        Try(() => Skipped.Add(1, new KeyValuePair<string, object?>("reason", reason)));

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // 指标旁路失败不得影响请求。
        }
    }
}
