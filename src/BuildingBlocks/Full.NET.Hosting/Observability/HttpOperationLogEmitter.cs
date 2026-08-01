using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// B2 HTTP Operation 有界发射闸门：成功采样与 Priority/BestEffort 容量背压。
/// </summary>
public sealed class HttpOperationLogEmitter
{
    private readonly IOptionsMonitor<HttpOperationLogOptions> _options;
    private readonly IDiagnosticPolicyStore _diagnosticPolicyStore;
    private int _bestEffortInFlight;
    private int _priorityInFlight;

    public HttpOperationLogEmitter(
        IOptionsMonitor<HttpOperationLogOptions> options,
        IDiagnosticPolicyStore diagnosticPolicyStore)
    {
        _options = options;
        _diagnosticPolicyStore = diagnosticPolicyStore;
    }

    public double ResolveSuccessSampleRate()
    {
        var options = _options.CurrentValue;
        return options.SuccessSampleRate
            ?? HttpOperationLogProfile.ResolveSuccessSampleRate(options.CapacityProfile);
    }

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
                   traceId,
                   tenantId)
               ?? ResolveSuccessSampleRate();
    }

    /// <summary>
    /// 基于 RouteKey+TraceId 的确定性成功采样，保证日志与 Trace 可关联。
    /// </summary>
    public bool ShouldSampleSuccess(string routeKey, string? traceId)
    {
        var rate = ResolveSuccessSampleRate();
        if (rate >= 1.0)
        {
            return true;
        }

        if (rate <= 0)
        {
            return false;
        }

        var material = routeKey + "\n" + (traceId ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var bucket = BitConverter.ToUInt32(hash, 0) / (double)uint.MaxValue;
        return bucket < rate;
    }

    public bool TryEnterBestEffort()
    {
        var capacity = _diagnosticPolicyStore.Current.ResolveBestEffortCapacity(
            _options.CurrentValue.BestEffortCapacity);
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

    public void ExitBestEffort() => Interlocked.Decrement(ref _bestEffortInFlight);

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
